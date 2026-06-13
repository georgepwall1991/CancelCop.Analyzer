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
/// Code fix provider that replaces a synchronous block on a task (<c>.Result</c>, <c>.Wait()</c>,
/// <c>.GetAwaiter().GetResult()</c>) with an <c>await</c> of the task.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(BlockingOnAsyncCodeFixProvider)), Shared]
public class BlockingOnAsyncCodeFixProvider : CodeFixProvider
{
    private const string Title = "Await the task";

    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(BlockingOnAsyncAnalyzer.DiagnosticId);

    public sealed override FixAllProvider GetFixAllProvider() =>
        WellKnownFixAllProviders.BatchFixer;

    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root == null)
            return;

        var diagnostic = context.Diagnostics.First();
        var name = root.FindToken(diagnostic.Location.SourceSpan.Start).Parent;
        if (name?.Parent is not MemberAccessExpressionSyntax memberAccess)
            return;

        if (!TryBuildRewrite(memberAccess, out var target, out var replacement))
            return;

        context.RegisterCodeFix(
            CodeAction.Create(
                title: Title,
                createChangedDocument: c => ReplaceAsync(context.Document, target!, replacement!, c),
                equivalenceKey: Title),
            diagnostic);
    }

    private static bool TryBuildRewrite(
        MemberAccessExpressionSyntax memberAccess,
        out SyntaxNode? target,
        out ExpressionSyntax? replacement)
    {
        target = null;
        replacement = null;

        switch (memberAccess.Name.Identifier.Text)
        {
            case "Result":
                target = memberAccess;
                replacement = ParenthesizedAwait(memberAccess.Expression);
                return true;

            case "Wait":
                if (memberAccess.Parent is not InvocationExpressionSyntax waitInvocation)
                    return false;
                target = waitInvocation;
                replacement = SyntaxFactory.AwaitExpression(memberAccess.Expression.WithoutTrivia());
                return true;

            case "GetResult":
                // memberAccess is `<X>.GetResult`; its parent is `<X>.GetResult()`, and
                // <X> is `<task>.GetAwaiter()`.
                if (memberAccess.Parent is not InvocationExpressionSyntax getResultInvocation ||
                    memberAccess.Expression is not InvocationExpressionSyntax getAwaiterInvocation ||
                    getAwaiterInvocation.Expression is not MemberAccessExpressionSyntax getAwaiterAccess)
                    return false;
                target = getResultInvocation;
                replacement = ParenthesizedAwait(getAwaiterAccess.Expression);
                return true;

            default:
                return false;
        }
    }

    private static ExpressionSyntax ParenthesizedAwait(ExpressionSyntax awaited) =>
        SyntaxFactory.ParenthesizedExpression(
            SyntaxFactory.AwaitExpression(awaited.WithoutTrivia()));

    private static async Task<Document> ReplaceAsync(
        Document document,
        SyntaxNode target,
        ExpressionSyntax replacement,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null)
            return document;

        var newRoot = root.ReplaceNode(target, replacement.WithTriviaFrom(target));
        return document.WithSyntaxRoot(newRoot);
    }
}
