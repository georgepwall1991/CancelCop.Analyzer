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

        // Only a direct `receiver.WaitForExit()` is rewritten. A null-conditional call
        // (`process?.WaitForExit()`) would need control flow to preserve its null semantics, so it is
        // reported without a fix — the same choice CC022, CC026, and CC028 make.
        if (invocation?.Expression is not MemberAccessExpressionSyntax memberAccess)
            return;

        var tokenName = diagnostic.Properties.TryGetValue(
            BlockingProcessWaitAnalyzer.TokenNameProperty,
            out var name
        )
            ? name
            : null;

        context.RegisterCodeFix(
            CodeAction.Create(
                title: Title,
                createChangedDocument: c =>
                    ReplaceAsync(context.Document, invocation, memberAccess, tokenName, c),
                equivalenceKey: Title
            ),
            diagnostic
        );
    }

    private static async Task<Document> ReplaceAsync(
        Document document,
        InvocationExpressionSyntax invocation,
        MemberAccessExpressionSyntax memberAccess,
        string? tokenName,
        CancellationToken cancellationToken
    )
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null)
            return document;

        // WaitForExitAsync's token parameter has no default, but passing none is still valid via the
        // (CancellationToken cancellationToken = default) signature shipped since .NET 5; when a
        // token is in scope, flow it so the wait is actually cancellable.
        var argumentList = SyntaxFactory.ArgumentList();
        if (tokenName != null)
        {
            argumentList = argumentList.AddArguments(
                SyntaxFactory.Argument(SyntaxFactory.IdentifierName(tokenName))
            );
        }

        var asyncInvocation = SyntaxFactory.InvocationExpression(
            SyntaxFactory.MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                memberAccess.Expression.WithoutTrivia(),
                SyntaxFactory.IdentifierName("WaitForExitAsync")
            ),
            argumentList
        );

        var newRoot = root.ReplaceNode(
            invocation,
            SyntaxFactory.AwaitExpression(asyncInvocation).WithTriviaFrom(invocation)
        );
        return document.WithSyntaxRoot(newRoot);
    }
}
