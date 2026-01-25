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

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(CancellationTokenNoneCodeFixProvider)), Shared]
public class CancellationTokenNoneCodeFixProvider : CodeFixProvider
{
    private const string Title = "Use available CancellationToken";

    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(CancellationTokenNoneAnalyzer.DiagnosticId);

    public sealed override FixAllProvider GetFixAllProvider() =>
        WellKnownFixAllProviders.BatchFixer;

    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root == null)
            return;

        var diagnostic = context.Diagnostics.First();
        var diagnosticSpan = diagnostic.Location.SourceSpan;

        // Get the token name from properties
        if (!diagnostic.Properties.TryGetValue("TokenName", out var tokenName) || string.IsNullOrEmpty(tokenName))
            return;

        var node = root.FindNode(diagnosticSpan);

        context.RegisterCodeFix(
            CodeAction.Create(
                title: $"Replace with '{tokenName}'",
                createChangedDocument: c => ReplaceWithTokenAsync(context.Document, node, tokenName!, c),
                equivalenceKey: Title),
            diagnostic);
    }

    private static async Task<Document> ReplaceWithTokenAsync(
        Document document,
        SyntaxNode nodeToReplace,
        string tokenName,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null)
            return document;

        var newNode = SyntaxFactory.IdentifierName(tokenName)
            .WithTriviaFrom(nodeToReplace);

        var newRoot = root.ReplaceNode(nodeToReplace, newNode);
        return document.WithSyntaxRoot(newRoot);
    }
}
