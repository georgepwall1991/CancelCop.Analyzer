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
/// Code fix provider that rewrites a blocking <c>File.&lt;name&gt;(...)</c> call to
/// <c>await File.&lt;name&gt;Async(..., token)</c>, flowing the in-scope token when one is available.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(BlockingFileIoCodeFixProvider)), Shared]
public class BlockingFileIoCodeFixProvider : CodeFixProvider
{
    private const string Title = "Use the async File method";

    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(BlockingFileIoAnalyzer.DiagnosticId);

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

        var tokenName = diagnostic.Properties.TryGetValue(BlockingFileIoAnalyzer.TokenNameProperty, out var name)
            ? name
            : null;

        context.RegisterCodeFix(
            CodeAction.Create(
                title: Title,
                createChangedDocument: c => ReplaceAsync(context.Document, invocation, memberAccess, tokenName, c),
                equivalenceKey: Title),
            diagnostic);
    }

    private static async Task<Document> ReplaceAsync(
        Document document,
        InvocationExpressionSyntax invocation,
        MemberAccessExpressionSyntax memberAccess,
        string? tokenName,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null)
            return document;

        // Append the in-scope token (when available) after the original arguments; the async
        // File.*Async overloads take the CancellationToken as the last parameter.
        var argumentList = invocation.ArgumentList.WithoutTrivia();
        if (tokenName != null)
        {
            argumentList = argumentList.AddArguments(
                SyntaxFactory.Argument(SyntaxFactory.IdentifierName(tokenName)));
        }

        var asyncName = memberAccess.Name.Identifier.Text + "Async";
        var asyncInvocation = SyntaxFactory.InvocationExpression(
            SyntaxFactory.MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                memberAccess.Expression.WithoutTrivia(),
                SyntaxFactory.IdentifierName(asyncName)),
            argumentList);

        var awaitExpression = SyntaxFactory.AwaitExpression(asyncInvocation).WithTriviaFrom(invocation);

        var newRoot = root.ReplaceNode(invocation, awaitExpression);
        return document.WithSyntaxRoot(newRoot);
    }
}
