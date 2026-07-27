using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CancelCop.Analyzer;

/// <summary>
/// Analyzer that detects a <c>catch (OperationCanceledException)</c> whose body is empty, silently
/// turning a cancellation into an apparent success.
/// </summary>
/// <remarks>
/// <para>
/// <b>Rule ID:</b> CC035
/// </para>
/// <para>
/// <b>Why this matters:</b>
/// Cancellation is reported by an exception precisely so that the caller learns the work did
/// <i>not</i> finish. An empty catch discards that signal: execution continues past the <c>try</c>
/// as though the operation succeeded, and the caller sees a normal return. Downstream code then acts
/// on results that were never produced — a partially written file treated as complete, an empty
/// collection treated as "no matches".
/// </para>
/// <para>
/// <b>How this differs from CC019:</b> CC019 covers a <i>broad</i> catch — <c>catch</c> or
/// <c>catch (Exception)</c> — that happens to swallow cancellation among everything else. A clause
/// that names <c>OperationCanceledException</c> explicitly is outside its scope, yet it is the more
/// deliberate-looking version of the same defect.
/// </para>
/// <para>
/// <b>Scoped to the empty body deliberately.</b> Catching cancellation to stop quietly is a real
/// pattern at a boundary — a <c>BackgroundService</c> winding down, a request handler on client
/// disconnect — and such handlers log, set state, or break a loop. An <i>empty</i> body does none of
/// that: it is not handling the cancellation, it is discarding it. Any statement in the body, a
/// <c>when</c> filter, or a rethrow all mean the author considered the case, and the rule stays
/// quiet. Reported as <b>Info</b> because a deliberate silent stop remains a legitimate, if
/// unusual, choice.
/// </para>
/// <para>
/// <b>Analyzer-only:</b> the right resolution — rethrow, log, set a flag, or convert to a result —
/// depends entirely on what the caller needs to know.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// try
/// {
///     await SaveAsync(cancellationToken);
/// }
/// catch (OperationCanceledException)   // CC035: the caller cannot tell the save did not happen
/// {
/// }
/// </code>
/// </example>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class SilentlySwallowedCancellationAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// The diagnostic ID for this analyzer rule.
    /// </summary>
    public const string DiagnosticId = "CC035";

    private static readonly LocalizableString Title =
        "Cancellation is silently swallowed by an empty catch";
    private static readonly LocalizableString MessageFormat =
        "Empty 'catch ({0})' discards the cancellation; execution continues as though the work completed";
    private static readonly LocalizableString Description =
        "An empty catch of OperationCanceledException turns a cancellation into an apparent success, so the caller cannot tell the work did not finish.";
    private const string Category = "Usage";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Info,
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

        // Resolve the framework exception once per compilation and compare symbols, rather than
        // matching on name and namespace: source or a referenced assembly may declare its own
        // System.OperationCanceledException, which the catch would bind to instead.
        context.RegisterCompilationStartAction(start =>
        {
            var cancellationException = start.Compilation.GetTypeByMetadataName(
                "System.OperationCanceledException"
            );
            if (cancellationException is null)
                return;

            start.RegisterSyntaxNodeAction(
                nodeContext => AnalyzeCatchClause(nodeContext, cancellationException),
                SyntaxKind.CatchClause
            );
        });
    }

    private static void AnalyzeCatchClause(
        SyntaxNodeAnalysisContext context,
        INamedTypeSymbol cancellationException
    )
    {
        var catchClause = (CatchClauseSyntax)context.Node;

        // A filter means the author reasoned about which cancellations to handle.
        if (catchClause.Filter != null)
            return;

        // A broad catch is CC019's finding; this rule is about the explicitly named one.
        if (catchClause.Declaration?.Type is not { } typeSyntax)
            return;

        if (
            context.SemanticModel.GetTypeInfo(typeSyntax, context.CancellationToken).Type
                is not { } caughtType
            || !IsCancellationException(caughtType, cancellationException)
        )
            return;

        // Any statement at all means the author did something about it.
        if (catchClause.Block.Statements.Count > 0)
            return;

        // A comment is deliberation too. `catch (TaskCanceledException) { /* expected on shutdown */ }`
        // is the idiomatic way to wait until cancelled, and the note is what distinguishes a
        // considered discard from a silent one — which is what this rule is named for.
        if (ContainsComment(catchClause.Block))
            return;

        context.ReportDiagnostic(
            Diagnostic.Create(Rule, catchClause.CatchKeyword.GetLocation(), caughtType.Name)
        );
    }

    /// <summary>
    /// Returns <c>true</c> when the block carries a comment, in its braces or between them.
    /// </summary>
    private static bool ContainsComment(BlockSyntax block) =>
        block
            .DescendantTrivia(descendIntoTrivia: true)
            .Any(trivia =>
                trivia.IsKind(SyntaxKind.SingleLineCommentTrivia)
                || trivia.IsKind(SyntaxKind.MultiLineCommentTrivia)
                || trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia)
                || trivia.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia)
            );

    /// <summary>
    /// Returns <c>true</c> for <c>OperationCanceledException</c> and its cancellation-specific
    /// subclasses, such as <c>TaskCanceledException</c>.
    /// </summary>
    private static bool IsCancellationException(
        ITypeSymbol type,
        INamedTypeSymbol cancellationException
    )
    {
        for (var current = type; current != null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, cancellationException))
                return true;
        }

        return false;
    }
}
