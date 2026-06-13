using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CancelCop.Analyzer;

/// <summary>
/// Analyzer that detects a locally-created <c>CancellationTokenSource</c> that is never disposed
/// and never escapes its method, leaking the unmanaged timer/handle it owns.
/// </summary>
/// <remarks>
/// <para>
/// <b>Rule ID:</b> CC014
/// </para>
/// <para>
/// <b>Why this matters:</b>
/// <c>CancellationTokenSource</c> is <c>IDisposable</c> — it can own a <c>Timer</c> and a
/// <c>WaitHandle</c>. A source created in a method and used only locally must be disposed, or those
/// resources leak until finalization. The cleanest fix is a <c>using</c> declaration.
/// </para>
/// <para>
/// <b>What it detects:</b> a local variable initialized with <c>new CancellationTokenSource(...)</c>
/// or <c>CancellationTokenSource.CreateLinkedTokenSource(...)</c> that is not already a <c>using</c>
/// declaration, is never disposed (<c>Dispose</c>/<c>DisposeAsync</c>), and never escapes (it is not
/// returned, assigned out, passed as an argument, or captured by a nested function).
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Violation:
/// var cts = new CancellationTokenSource();    // CC014: never disposed
/// await DoAsync(cts.Token);
///
/// // Fixed:
/// using var cts = new CancellationTokenSource();
/// await DoAsync(cts.Token);
/// </code>
/// </example>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class UndisposedTokenSourceAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// The diagnostic ID for this analyzer rule.
    /// </summary>
    public const string DiagnosticId = "CC014";

    private static readonly LocalizableString Title = "CancellationTokenSource is never disposed";
    private static readonly LocalizableString MessageFormat = "CancellationTokenSource '{0}' is never disposed; use a 'using' declaration or dispose it";
    private static readonly LocalizableString Description = "A locally-owned CancellationTokenSource holds disposable resources and should be disposed, ideally via a 'using' declaration.";
    private const string Category = "Usage";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: Description);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(AnalyzeLocalDeclaration, SyntaxKind.LocalDeclarationStatement);
    }

    private void AnalyzeLocalDeclaration(SyntaxNodeAnalysisContext context)
    {
        var declaration = (LocalDeclarationStatementSyntax)context.Node;

        // A `using var cts = ...;` is already disposed deterministically.
        if (!declaration.UsingKeyword.IsKind(SyntaxKind.None))
            return;

        foreach (var declarator in declaration.Declaration.Variables)
        {
            if (declarator.Initializer?.Value is not { } initializer)
                continue;

            // The initializer must produce a CancellationTokenSource (covers both `new` and
            // CreateLinkedTokenSource, which returns one).
            var type = context.SemanticModel.GetTypeInfo(initializer, context.CancellationToken).Type;
            if (!IsCancellationTokenSource(type))
                continue;

            if (context.SemanticModel.GetDeclaredSymbol(declarator, context.CancellationToken) is not ILocalSymbol local)
                continue;

            var scope = declaration.FirstAncestorOrSelf<SyntaxNode>(IsFunctionScope);
            if (scope == null)
                continue;

            if (IsDisposedOrEscapes(local, declaration, scope, context.SemanticModel, context.CancellationToken))
                continue;

            context.ReportDiagnostic(Diagnostic.Create(
                Rule, declarator.Identifier.GetLocation(), declarator.Identifier.Text));
        }
    }

    private static bool IsCancellationTokenSource(ITypeSymbol? type)
    {
        return type?.Name == "CancellationTokenSource" &&
               type.ContainingNamespace?.ToDisplayString() == "System.Threading";
    }

    private static bool IsFunctionScope(SyntaxNode node) =>
        node is MethodDeclarationSyntax or LocalFunctionStatementSyntax or
            AnonymousFunctionExpressionSyntax or AccessorDeclarationSyntax or
            ConstructorDeclarationSyntax;

    /// <summary>
    /// Returns true when the source is disposed within <paramref name="scope"/>, or when it escapes
    /// (return, out-assignment, argument, or capture by a nested function) so that disposal cannot
    /// be proven absent. Either case suppresses the diagnostic.
    /// </summary>
    private static bool IsDisposedOrEscapes(
        ILocalSymbol local,
        LocalDeclarationStatementSyntax declaration,
        SyntaxNode scope,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        var declarationScope = declaration.FirstAncestorOrSelf<SyntaxNode>(IsFunctionScope);

        foreach (var reference in scope.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            if (reference.Identifier.Text != local.Name)
                continue;

            if (!SymbolEqualityComparer.Default.Equals(
                    semanticModel.GetSymbolInfo(reference, cancellationToken).Symbol, local))
                continue;

            // Captured by a nested function (could be disposed there or outlive the scope).
            if (reference.FirstAncestorOrSelf<SyntaxNode>(IsFunctionScope) != declarationScope)
                return true;

            var parent = reference.Parent;

            // cts.Dispose() / cts.DisposeAsync()
            if (parent is MemberAccessExpressionSyntax memberAccess &&
                memberAccess.Expression == reference &&
                (memberAccess.Name.Identifier.Text == "Dispose" ||
                 memberAccess.Name.Identifier.Text == "DisposeAsync"))
            {
                return true;
            }

            // Passed as an argument, returned, or used as a using-statement resource -> escapes.
            if (parent is ArgumentSyntax or ReturnStatementSyntax or ArrowExpressionClauseSyntax or
                YieldStatementSyntax or UsingStatementSyntax or EqualsValueClauseSyntax)
            {
                return true;
            }

            // Assigned to something else (field/property/out variable) -> escapes.
            if (parent is AssignmentExpressionSyntax assignment && assignment.Right == reference)
                return true;
        }

        return false;
    }
}
