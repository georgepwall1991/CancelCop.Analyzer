using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CancelCop.Analyzer;

/// <summary>
/// Analyzer that suggests <c>await using</c> over a plain <c>using</c> for an
/// <c>IAsyncDisposable</c> resource in async code.
/// </summary>
/// <remarks>
/// <para>
/// <b>Rule ID:</b> CC025
/// </para>
/// <para>
/// <b>Why this matters:</b>
/// A type that implements <c>IAsyncDisposable</c> releases its resources asynchronously. Disposing
/// it through a synchronous <c>using</c> calls <c>Dispose()</c> (if present) — which typically
/// blocks on the async cleanup — or, for an async-only disposable, fails to compile. In async code,
/// <c>await using</c> awaits <c>DisposeAsync()</c> so the cleanup does not block the thread.
/// Reported as <b>Info</b>.
/// </para>
/// <para>
/// <b>What it detects:</b> a <c>using</c> statement or declaration (without <c>await</c>) over a
/// resource whose type implements <c>System.IAsyncDisposable</c>, inside an <c>async</c> method,
/// local function, lambda, anonymous method, or top-level program whose synthesized entry point
/// contains <c>await</c>.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public async Task RunAsync()
/// {
///     using var resource = new AsyncResource();   // CC025 -> await using
///     await resource.UseAsync();
/// }
/// </code>
/// </example>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class AwaitUsingAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// The diagnostic ID for this analyzer rule.
    /// </summary>
    public const string DiagnosticId = "CC025";

    /// <summary>
    /// Property key set when the diagnostic is correct but inserting an <c>await</c> here would not
    /// compile, so the code fix must not offer a rewrite.
    /// </summary>
    public const string NoFixProperty = "NoFix";

    private static readonly LocalizableString Title = "Prefer await using for IAsyncDisposable";
    private static readonly LocalizableString MessageFormat = "Resource is IAsyncDisposable; use 'await using' so DisposeAsync is awaited";
    private static readonly LocalizableString Description = "An IAsyncDisposable resource should be disposed with 'await using' in async code so DisposeAsync runs without blocking.";
    private const string Category = "Usage";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: Description,
        helpLinkUri: DiagnosticHelp.LinkUri);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(AnalyzeUsingStatement, SyntaxKind.UsingStatement);
        context.RegisterSyntaxNodeAction(AnalyzeLocalDeclaration, SyntaxKind.LocalDeclarationStatement);
    }

    private void AnalyzeUsingStatement(SyntaxNodeAnalysisContext context)
    {
        var usingStatement = (UsingStatementSyntax)context.Node;
        if (!usingStatement.AwaitKeyword.IsKind(SyntaxKind.None))
            return;

        var resourceType = usingStatement.Declaration != null
            ? GetDeclarationType(usingStatement.Declaration, context)
            : usingStatement.Expression != null
                ? context.SemanticModel.GetTypeInfo(usingStatement.Expression, context.CancellationToken).Type
                : null;

        Evaluate(context, resourceType, usingStatement.UsingKeyword, usingStatement);
    }

    private void AnalyzeLocalDeclaration(SyntaxNodeAnalysisContext context)
    {
        var declaration = (LocalDeclarationStatementSyntax)context.Node;
        // Only a `using` declaration (without `await`) is relevant.
        if (declaration.UsingKeyword.IsKind(SyntaxKind.None) || !declaration.AwaitKeyword.IsKind(SyntaxKind.None))
            return;

        Evaluate(context, GetDeclarationType(declaration.Declaration, context), declaration.UsingKeyword, declaration);
    }

    private static ITypeSymbol? GetDeclarationType(VariableDeclarationSyntax declaration, SyntaxNodeAnalysisContext context)
    {
        var firstVariable = declaration.Variables.FirstOrDefault();
        if (firstVariable == null)
            return null;

        return context.SemanticModel.GetDeclaredSymbol(firstVariable, context.CancellationToken) is ILocalSymbol local
            ? local.Type
            : null;
    }

    private static void Evaluate(
        SyntaxNodeAnalysisContext context, ITypeSymbol? resourceType, SyntaxToken usingKeyword, SyntaxNode node)
    {
        if (!ImplementsAsyncDisposable(resourceType))
            return;

        // `await using` is only valid in async code.
        if (!CancellationTokenHelpers.IsInAsyncFunction(node))
            return;

        // The construct is just as problematic either way, but where an inserted await would not
        // compile — a lock body, an exception filter, an unsafe context, most query clauses, or
        // across a ref-like lifetime — the diagnostic is reported without a fix.
        var construct = usingKeyword.Parent ?? context.Node;
        var properties = CancellationTokenHelpers.AwaitInsertionIsUnsafe(
            context.SemanticModel,
            construct,
            DisposalPositionOf(construct),
            construct.SpanStart
        )
            ? ImmutableDictionary<string, string?>.Empty.Add(
                NoFixProperty,
                CancellationTokenHelpers.AwaitUnsafeReason
            )
            : ImmutableDictionary<string, string?>.Empty;

        context.ReportDiagnostic(
            Diagnostic.Create(Rule, usingKeyword.GetLocation(), properties)
        );
    }

    private static bool ImplementsAsyncDisposable(ITypeSymbol? type)
    {
        if (type == null)
            return false;

        if (IsAsyncDisposable(type))
            return true;

        return type.AllInterfaces.Any(IsAsyncDisposable);
    }

    private static bool IsAsyncDisposable(ITypeSymbol type) =>
        type.Name == "IAsyncDisposable" && type.ContainingNamespace?.ToDisplayString() == "System";

    /// <summary>
    /// Where the <c>await</c> introduced by <c>await using</c> actually runs: at disposal, which is
    /// the end of the <c>using</c> statement's body or the end of the declaration's enclosing scope
    /// — not at the <c>using</c> keyword.
    /// </summary>
    /// <remarks>
    /// Checking ref-like lifetimes at the keyword is too strict: a <c>Span&lt;T&gt;</c> read between
    /// the <c>using</c> and scope exit finishes before disposal awaits, so the rewrite compiles and
    /// the fix should still be offered.
    /// </remarks>
    private static int DisposalPositionOf(SyntaxNode construct)
    {
        if (construct is UsingStatementSyntax usingStatement)
            return usingStatement.Statement.Span.End;

        // A using declaration disposes at the end of its enclosing scope.
        var scope = construct
            .Ancestors()
            .FirstOrDefault(a => a is BlockSyntax or SwitchSectionSyntax or CompilationUnitSyntax);
        return scope?.Span.End ?? construct.Span.End;
    }
}
