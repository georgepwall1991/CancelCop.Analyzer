using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CancelCop.Analyzer;

/// <summary>
/// Analyzer that detects a <c>ParallelOptions</c> created without a <c>CancellationToken</c> while
/// one is in scope.
/// </summary>
/// <remarks>
/// <para>
/// <b>Rule ID:</b> CC034
/// </para>
/// <para>
/// <b>Why this matters:</b>
/// <c>ParallelOptions.CancellationToken</c> is the <i>only</i> way to cancel a <c>Parallel</c> loop.
/// Without it the loop runs every partition to completion no matter what the caller wants, and a
/// long parallel loop over a large collection is precisely the work most worth stopping.
/// </para>
/// <para>
/// <b>The gap this closes:</b> CC002 fires on a <i>call</i> that has a token-accepting overload.
/// Here the token is neither an argument nor an overload — it is a property set in an object
/// initializer, and <c>Parallel.ForEach</c> has no token-taking overload at all. There is nothing
/// for CC002 to match on, so this omission is invisible to it.
/// </para>
/// <para>
/// <b>Conservative by design:</b> the rule only fires when a token is actually in scope, using the
/// same walk as CC002/CC012 — with no token available there is nothing to suggest. It stays quiet
/// when the token is assigned to the options afterwards
/// (<c>options.CancellationToken = cancellationToken;</c>), which is equally correct and common when
/// the options are built conditionally.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public async Task RunAsync(int[] items, CancellationToken cancellationToken)
/// {
///     // CC034: nothing can stop this loop
///     var options = new ParallelOptions { MaxDegreeOfParallelism = 4 };
///     Parallel.ForEach(items, options, Process);
/// }
/// </code>
/// </example>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class ParallelOptionsTokenAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// The diagnostic ID for this analyzer rule.
    /// </summary>
    public const string DiagnosticId = "CC034";

    /// <summary>
    /// Property key carrying the in-scope token parameter name to the code fix provider.
    /// </summary>
    public const string TokenNameProperty = "TokenName";

    private static readonly LocalizableString Title =
        "ParallelOptions should set CancellationToken";
    private static readonly LocalizableString MessageFormat =
        "ParallelOptions should set CancellationToken to '{0}'; a Parallel loop cannot be cancelled any other way";
    private static readonly LocalizableString Description =
        "ParallelOptions.CancellationToken is the only way to cancel a Parallel loop; when a token is in scope it should be set.";
    private const string Category = "Usage";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: Description,
        helpLinkUri: DiagnosticHelp.LinkUri
    );

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(
            AnalyzeCreation,
            SyntaxKind.ObjectCreationExpression,
            SyntaxKind.ImplicitObjectCreationExpression
        );
    }

    private static void AnalyzeCreation(SyntaxNodeAnalysisContext context)
    {
        var creation = (BaseObjectCreationExpressionSyntax)context.Node;

        if (
            context.SemanticModel.GetTypeInfo(creation, context.CancellationToken).Type
                is not { } createdType
            || !IsParallelOptions(createdType)
        )
            return;

        if (SetsTokenInInitializer(creation, context))
            return;

        // With no token available there is nothing to suggest, which is the same gate CC002 and
        // CC012 apply.
        var tokenParameter = CancellationTokenHelpers.FindEnclosingCancellationTokenParameter(
            creation,
            context.SemanticModel
        );
        if (tokenParameter is null)
            return;

        if (TokenAssignedAfterwards(creation, context))
            return;

        var properties = ImmutableDictionary<string, string?>.Empty.Add(
            TokenNameProperty,
            tokenParameter.Name
        );

        context.ReportDiagnostic(
            Diagnostic.Create(Rule, creation.GetLocation(), properties, tokenParameter.Name)
        );
    }

    private static bool SetsTokenInInitializer(
        BaseObjectCreationExpressionSyntax creation,
        SyntaxNodeAnalysisContext context
    ) =>
        creation.Initializer?.Expressions.Any(expression =>
            expression
                is AssignmentExpressionSyntax
                {
                    Left: IdentifierNameSyntax { Identifier.Text: "CancellationToken" }
                } assignment
            && CancelsSomething(assignment.Right, context)
        ) == true;

    /// <summary>
    /// Returns <c>true</c> when the assigned value can actually cancel.
    /// </summary>
    /// <remarks>
    /// <c>CancellationToken = default</c> and <c>= CancellationToken.None</c> satisfy the property
    /// while leaving the loop exactly as uncancellable as before. CC012 covers those spellings only
    /// as invocation arguments, so nothing else would report them here.
    /// </remarks>
    private static bool CancelsSomething(ExpressionSyntax value, SyntaxNodeAnalysisContext context)
    {
        var expression = value;
        while (expression is ParenthesizedExpressionSyntax parenthesized)
            expression = parenthesized.Expression;

        if (
            expression.IsKind(SyntaxKind.DefaultLiteralExpression)
            || expression is DefaultExpressionSyntax
        )
            return false;

        return context.SemanticModel.GetSymbolInfo(expression, context.CancellationToken).Symbol
                is not IPropertySymbol { Name: "None" } none
            || !CancellationTokenHelpers.IsCancellationToken(none.ContainingType);
    }

    /// <summary>
    /// Returns <c>true</c> when the token is assigned to the created options later —
    /// <c>options.CancellationToken = cancellationToken;</c> — which is equally correct and common
    /// when the options are built conditionally.
    /// </summary>
    private static bool TokenAssignedAfterwards(
        BaseObjectCreationExpressionSyntax creation,
        SyntaxNodeAnalysisContext context
    )
    {
        // Only a named target can be assigned to afterwards.
        if (
            creation.Parent
                is not EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax declarator }
            || context.SemanticModel.GetDeclaredSymbol(declarator, context.CancellationToken)
                is not ILocalSymbol local
        )
            return false;

        var scope = creation
            .Ancestors()
            .FirstOrDefault(node =>
                node
                    is BaseMethodDeclarationSyntax
                        or LocalFunctionStatementSyntax
                        or AnonymousFunctionExpressionSyntax
                        or AccessorDeclarationSyntax
                        or CompilationUnitSyntax
            );
        if (scope is null)
            return false;

        // Where the options are first handed to something. An assignment after that point is too
        // late — the loop it was passed to already ran uncancellable.
        var firstUse = scope
            .DescendantNodes()
            .OfType<ArgumentSyntax>()
            .Where(argument =>
                SymbolEqualityComparer.Default.Equals(
                    context
                        .SemanticModel.GetSymbolInfo(argument.Expression, context.CancellationToken)
                        .Symbol,
                    local
                )
            )
            .Select(argument => (int?)argument.SpanStart)
            .FirstOrDefault();

        return scope
            .DescendantNodes()
            .OfType<AssignmentExpressionSyntax>()
            .Any(assignment =>
                assignment.Left
                    is MemberAccessExpressionSyntax
                    {
                        Name.Identifier.ValueText: "CancellationToken"
                    } target
                && (firstUse is null || assignment.SpanStart < firstUse)
                // An assignment inside a nested function may never run at all.
                && !assignment
                    .Ancestors()
                    .TakeWhile(node => node != scope)
                    .Any(node =>
                        node is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax
                    )
                && CancelsSomething(assignment.Right, context)
                && SymbolEqualityComparer.Default.Equals(
                    context
                        .SemanticModel.GetSymbolInfo(target.Expression, context.CancellationToken)
                        .Symbol,
                    local
                )
            );
    }

    private static bool IsParallelOptions(ITypeSymbol type) =>
        type is { ContainingType: null, Name: "ParallelOptions" }
        && type.ContainingNamespace?.ToDisplayString() == "System.Threading.Tasks";
}
