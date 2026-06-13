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
/// Code fix provider that turns a <c>using</c> over an <c>IAsyncDisposable</c> into an
/// <c>await using</c>.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(AwaitUsingCodeFixProvider)), Shared]
public class AwaitUsingCodeFixProvider : CodeFixProvider
{
    private const string Title = "Use 'await using'";

    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(AwaitUsingAnalyzer.DiagnosticId);

    public sealed override FixAllProvider GetFixAllProvider() =>
        WellKnownFixAllProviders.BatchFixer;

    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root == null)
            return;

        var diagnostic = context.Diagnostics.First();
        var node = root.FindToken(diagnostic.Location.SourceSpan.Start).Parent;
        if (node is not UsingStatementSyntax and not LocalDeclarationStatementSyntax)
            node = node?.AncestorsAndSelf().FirstOrDefault(n => n is UsingStatementSyntax or LocalDeclarationStatementSyntax);
        if (node == null)
            return;

        context.RegisterCodeFix(
            CodeAction.Create(
                title: Title,
                createChangedDocument: c => AddAwaitAsync(context.Document, node, c),
                equivalenceKey: Title),
            diagnostic);
    }

    private static async Task<Document> AddAwaitAsync(
        Document document,
        SyntaxNode node,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null)
            return document;

        // Move the statement's leading trivia (indentation) onto the new 'await' keyword so the
        // 'using' keyword no longer carries it.
        var awaitToken = SyntaxFactory.Token(SyntaxKind.AwaitKeyword)
            .WithLeadingTrivia(node.GetLeadingTrivia())
            .WithTrailingTrivia(SyntaxFactory.Space);

        var stripped = node.WithLeadingTrivia(SyntaxFactory.TriviaList());

        SyntaxNode newNode = stripped switch
        {
            UsingStatementSyntax usingStatement => usingStatement.WithAwaitKeyword(awaitToken),
            LocalDeclarationStatementSyntax declaration => declaration.WithAwaitKeyword(awaitToken),
            _ => node,
        };

        var newRoot = root.ReplaceNode(node, newNode);
        return document.WithSyntaxRoot(newRoot);
    }
}
