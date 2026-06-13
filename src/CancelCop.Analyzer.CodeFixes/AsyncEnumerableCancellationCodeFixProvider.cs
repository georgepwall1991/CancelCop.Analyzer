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
/// Code fix provider that wraps the source of an <c>await foreach</c> in
/// <c>.WithCancellation(token)</c> so the in-scope token flows into the async enumeration.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(AsyncEnumerableCancellationCodeFixProvider)), Shared]
public class AsyncEnumerableCancellationCodeFixProvider : CodeFixProvider
{
    private const string Title = "Flow CancellationToken with .WithCancellation()";

    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(AsyncEnumerableCancellationAnalyzer.DiagnosticId);

    public sealed override FixAllProvider GetFixAllProvider() =>
        WellKnownFixAllProviders.BatchFixer;

    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root == null)
            return;

        var diagnostic = context.Diagnostics.First();
        var span = diagnostic.Location.SourceSpan;

        var source = root.FindNode(span, getInnermostNodeForTie: true) as ExpressionSyntax;
        if (source == null)
            return;

        var tokenName = diagnostic.Properties.TryGetValue(AsyncEnumerableCancellationAnalyzer.TokenNameProperty, out var name) && name != null
            ? name
            : "cancellationToken";

        context.RegisterCodeFix(
            CodeAction.Create(
                title: Title,
                createChangedDocument: c => WrapWithCancellationAsync(context.Document, source, tokenName, c),
                equivalenceKey: Title),
            diagnostic);
    }

    private static async Task<Document> WrapWithCancellationAsync(
        Document document,
        ExpressionSyntax source,
        string tokenName,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null)
            return document;

        // Build `<source>.WithCancellation(<tokenName>)`, preserving the source's leading/trailing trivia.
        var bareSource = source.WithoutTrivia();
        var withCancellation = SyntaxFactory.InvocationExpression(
                SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    bareSource,
                    SyntaxFactory.IdentifierName("WithCancellation")),
                SyntaxFactory.ArgumentList(
                    SyntaxFactory.SingletonSeparatedList(
                        SyntaxFactory.Argument(SyntaxFactory.IdentifierName(tokenName)))))
            .WithTriviaFrom(source);

        var newRoot = root.ReplaceNode(source, withCancellation);
        return document.WithSyntaxRoot(newRoot);
    }
}
