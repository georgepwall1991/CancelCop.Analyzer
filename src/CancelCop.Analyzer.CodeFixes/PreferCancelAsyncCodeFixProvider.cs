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
/// Code fix provider that rewrites a synchronous <c>cts.Cancel()</c> to
/// <c>await cts.CancelAsync()</c>.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(PreferCancelAsyncCodeFixProvider)), Shared]
public class PreferCancelAsyncCodeFixProvider : CodeFixProvider
{
    private const string Title = "Use await CancelAsync()";

    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(PreferCancelAsyncAnalyzer.DiagnosticId);

    public sealed override FixAllProvider GetFixAllProvider() =>
        WellKnownFixAllProviders.BatchFixer;

    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root == null)
            return;

        var diagnostic = context.Diagnostics.First();
        var invocation = root.FindToken(diagnostic.Location.SourceSpan.Start)
            .Parent?.AncestorsAndSelf().OfType<InvocationExpressionSyntax>().FirstOrDefault();
        if (invocation?.Expression is not MemberAccessExpressionSyntax memberAccess)
            return;

        context.RegisterCodeFix(
            CodeAction.Create(
                title: Title,
                createChangedDocument: c => ReplaceAsync(context.Document, invocation, memberAccess, c),
                equivalenceKey: Title),
            diagnostic);
    }

    private static async Task<Document> ReplaceAsync(
        Document document,
        InvocationExpressionSyntax invocation,
        MemberAccessExpressionSyntax memberAccess,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null)
            return document;

        // await <receiver>.CancelAsync()
        var cancelAsync = SyntaxFactory.InvocationExpression(
            SyntaxFactory.MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                memberAccess.Expression.WithoutTrivia(),
                SyntaxFactory.IdentifierName("CancelAsync")));

        var awaitExpression = SyntaxFactory.AwaitExpression(cancelAsync).WithTriviaFrom(invocation);

        var newRoot = root.ReplaceNode(invocation, awaitExpression);
        return document.WithSyntaxRoot(newRoot);
    }
}
