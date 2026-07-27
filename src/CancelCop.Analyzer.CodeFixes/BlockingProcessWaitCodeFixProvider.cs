using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CancelCop.Analyzer;

/// <summary>
/// Code fix provider that rewrites a blocking <c>process.WaitForExit()</c> to
/// <c>await process.WaitForExitAsync(cancellationToken)</c>, flowing the in-scope token when one is
/// available.
/// </summary>
[
    ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(BlockingProcessWaitCodeFixProvider)),
    Shared
]
public class BlockingProcessWaitCodeFixProvider : CodeFixProvider
{
    private const string Title = "Use await WaitForExitAsync";

    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(BlockingProcessWaitAnalyzer.DiagnosticId);

    public sealed override FixAllProvider GetFixAllProvider() =>
        WellKnownFixAllProviders.BatchFixer;

    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context
            .Document.GetSyntaxRootAsync(context.CancellationToken)
            .ConfigureAwait(false);
        if (root == null)
            return;

        var diagnostic = context.Diagnostics.First();

        // The diagnostic stands but the analyzer determined no compilable rewrite exists here.
        if (diagnostic.Properties.ContainsKey(BlockingProcessWaitAnalyzer.NoFixProperty))
            return;

        var invocation = root.FindToken(diagnostic.Location.SourceSpan.Start)
            .Parent?.AncestorsAndSelf()
            .OfType<InvocationExpressionSyntax>()
            .FirstOrDefault();

        if (invocation is null)
            return;

        var tokenName = diagnostic.Properties.TryGetValue(
            BlockingProcessWaitAnalyzer.TokenNameProperty,
            out var name
        )
            ? name
            : null;

        // Null for a null-conditional call (`process?.WaitForExit()`), where preserving the null
        // semantics needs control flow rather than an expression rewrite — the same choice CC022,
        // CC026, and CC028 make. Checked here so no code action is offered at all, rather than one
        // that silently does nothing.
        var asyncInvocation = CancellationTokenHelpers.BuildRenamedInvocation(
            invocation,
            "WaitForExitAsync",
            tokenName
        );
        if (asyncInvocation is null)
            return;

        context.RegisterCodeFix(
            CodeAction.Create(
                title: Title,
                createChangedDocument: c =>
                    ReplaceAsync(context.Document, invocation, asyncInvocation, c),
                equivalenceKey: Title
            ),
            diagnostic
        );
    }

    private static async Task<Document> ReplaceAsync(
        Document document,
        InvocationExpressionSyntax invocation,
        InvocationExpressionSyntax asyncInvocation,
        CancellationToken cancellationToken
    )
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null)
            return document;

        // Built by the analyzer's helper so the emitted call is exactly the one it bound and
        // approved. It replaces the name on the existing member access rather than rebuilding the
        // expression, which keeps the receiver and any trivia around the dot intact — reconstructing
        // it would silently delete a comment such as `process /* started above */ .WaitForExit()`.
        var newRoot = root.ReplaceNode(
            invocation,
            SyntaxFactory.AwaitExpression(asyncInvocation).WithTriviaFrom(invocation)
        );
        return document.WithSyntaxRoot(newRoot);
    }
}
