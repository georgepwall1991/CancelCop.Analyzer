using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CancelCop.Analyzer;

/// <summary>
/// Analyzer that detects a <c>CancellationTokenSource</c> field created by its declaring type and
/// never disposed by it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Rule ID:</b> CC033
/// </para>
/// <para>
/// <b>Why this matters:</b>
/// A <c>CancellationTokenSource</c> owns unmanaged-adjacent state — a timer when a delay is set, and
/// a registration list that every linked token and every <c>Register</c> callback adds to. A field
/// keeps that alive for the whole lifetime of the owning object, so a source that is never disposed
/// leaks for as long as its owner does. Linked sources are worse: an undisposed child stays attached
/// to its parent's callback list, so a long-lived parent accumulates every child ever created.
/// </para>
/// <para>
/// <b>How this differs from CC014:</b> CC014 covers a <i>local</i> source, where the fix is
/// mechanical — make it a <c>using</c> declaration. A field's lifetime is the object's, so the
/// resolution is to implement <c>IDisposable</c> (or dispose it in the existing one) and have the
/// owner's own disposal cascade. That is a design change, so CC033 is analyzer-only, like CC017,
/// CC020, CC024, CC027, CC031, and CC032.
/// </para>
/// <para>
/// <b>Conservative by design:</b> the rule only fires when the declaring type <i>creates</i> the
/// source — an injected or assigned-from-elsewhere source is owned by whoever created it, and
/// disposing it would be a bug. It stays quiet if any member disposes the field, if the field
/// escapes (returned, or passed as an argument, so something else may own it), and for
/// <c>static</c> fields, whose lifetime is the process and which are typically deliberate.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public class Worker
/// {
///     // CC033: created here, never disposed — the timer and registration list outlive their use
///     private readonly CancellationTokenSource _cts = new CancellationTokenSource();
/// }
/// </code>
/// </example>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class UndisposedTokenSourceFieldAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// The diagnostic ID for this analyzer rule.
    /// </summary>
    public const string DiagnosticId = "CC033";

    private static readonly LocalizableString Title =
        "CancellationTokenSource field is never disposed";
    private static readonly LocalizableString MessageFormat =
        "Field '{0}' creates a CancellationTokenSource that is never disposed";
    private static readonly LocalizableString Description =
        "A CancellationTokenSource field owns a timer and a registration list for the lifetime of its owner; if the declaring type creates it, the type should dispose it.";
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

        // Analysing the whole type at once is what makes "never disposed" answerable: disposal
        // almost always lives in a different member from the creation.
        context.RegisterSymbolAction(AnalyzeNamedType, SymbolKind.NamedType);
    }

    private static void AnalyzeNamedType(SymbolAnalysisContext context)
    {
        var type = (INamedTypeSymbol)context.Symbol;

        var candidates = type.GetMembers()
            .OfType<IFieldSymbol>()
            .Where(field =>
                !field.IsStatic
                && !field.IsImplicitlyDeclared
                && IsCancellationTokenSource(field.Type)
            )
            .ToList();

        if (candidates.Count == 0)
            return;

        var bodies = type
            .DeclaringSyntaxReferences.Select(reference =>
                reference.GetSyntax(context.CancellationToken)
            )
            .ToList();
        if (bodies.Count == 0)
            return;

        foreach (var field in candidates)
        {
            var declaration = field.DeclaringSyntaxReferences.FirstOrDefault();
            if (declaration is null)
                continue;

            if (!bodies.Any(body => CreatesSource(body, field, context)))
                continue;

            if (bodies.Any(body => DisposesOrEscapes(body, field, context)))
                continue;

            context.ReportDiagnostic(Diagnostic.Create(Rule, field.Locations[0], field.Name));
        }
    }

    /// <summary>
    /// Returns <c>true</c> when the declaring type constructs the source itself — the only case in
    /// which it owns the source and is responsible for disposing it.
    /// </summary>
    private static bool CreatesSource(
        SyntaxNode body,
        IFieldSymbol field,
        SymbolAnalysisContext context
    )
    {
        var model = context.Compilation.GetSemanticModel(body.SyntaxTree);

        foreach (var creation in body.DescendantNodes().OfType<ObjectCreationExpressionSyntax>())
        {
            if (
                model.GetTypeInfo(creation, context.CancellationToken).Type is { } created
                && IsCancellationTokenSource(created)
                && AssignsTo(creation, field, model, context)
            )
                return true;
        }

        // CreateLinkedTokenSource is a factory rather than a constructor, and its result is owned by
        // the caller in exactly the same way.
        foreach (var invocation in body.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (
                model.GetSymbolInfo(invocation, context.CancellationToken).Symbol
                    is IMethodSymbol { Name: "CreateLinkedTokenSource" } factory
                && IsCancellationTokenSource(factory.ContainingType)
                && AssignsTo(invocation, field, model, context)
            )
                return true;
        }

        return false;
    }

    /// <summary>
    /// Returns <c>true</c> when <paramref name="expression"/> is the value given to
    /// <paramref name="field"/>, either in its initializer or by an assignment.
    /// </summary>
    private static bool AssignsTo(
        ExpressionSyntax expression,
        IFieldSymbol field,
        SemanticModel model,
        SymbolAnalysisContext context
    )
    {
        var parent = expression.Parent;

        if (parent is EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax declarator })
        {
            return SymbolEqualityComparer.Default.Equals(
                model.GetDeclaredSymbol(declarator, context.CancellationToken),
                field
            );
        }

        return parent is AssignmentExpressionSyntax assignment
            && assignment.Right == expression
            && SymbolEqualityComparer.Default.Equals(
                model.GetSymbolInfo(assignment.Left, context.CancellationToken).Symbol,
                field
            );
    }

    /// <summary>
    /// Returns <c>true</c> when the type disposes the field, or lets it escape so that something
    /// else may own it.
    /// </summary>
    /// <remarks>
    /// Escape is treated as exoneration rather than as a finding: once the source is handed out, who
    /// disposes it is no longer decidable from this type alone, and guessing would produce noise.
    /// This mirrors CC014's conservative escape analysis.
    /// </remarks>
    private static bool DisposesOrEscapes(
        SyntaxNode body,
        IFieldSymbol field,
        SymbolAnalysisContext context
    )
    {
        var model = context.Compilation.GetSemanticModel(body.SyntaxTree);

        foreach (var identifier in body.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            if (identifier.Identifier.Text != field.Name)
                continue;

            if (
                !SymbolEqualityComparer.Default.Equals(
                    model.GetSymbolInfo(identifier, context.CancellationToken).Symbol,
                    field
                )
            )
                continue;

            // `_cts.Dispose()` / `_cts.DisposeAsync()`, however the receiver is spelled.
            var access = identifier.Parent as MemberAccessExpressionSyntax;
            if (
                access?.Name.Identifier.Text is "Dispose" or "DisposeAsync"
                && access.Expression == identifier
            )
                return true;

            // Returned, or passed to something that may take ownership.
            if (
                identifier.Parent
                is ArgumentSyntax
                    or ReturnStatementSyntax
                    or ArrowExpressionClauseSyntax
            )
                return true;
        }

        return false;
    }

    private static bool IsCancellationTokenSource(ITypeSymbol? type) =>
        type is { ContainingType: null, Name: "CancellationTokenSource" }
        && type.ContainingNamespace?.ToDisplayString() == "System.Threading";
}
