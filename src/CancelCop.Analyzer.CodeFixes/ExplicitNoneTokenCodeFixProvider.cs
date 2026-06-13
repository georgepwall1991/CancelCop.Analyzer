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
/// Code fix provider that replaces an explicit <c>CancellationToken.None</c> / <c>default</c>
/// argument with the in-scope token.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ExplicitNoneTokenCodeFixProvider)), Shared]
public class ExplicitNoneTokenCodeFixProvider : CodeFixProvider
{
    private const string Title = "Pass the in-scope CancellationToken";

    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(ExplicitNoneTokenAnalyzer.DiagnosticId);

    public sealed override FixAllProvider GetFixAllProvider() =>
        WellKnownFixAllProviders.BatchFixer;

    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root == null)
            return;

        var diagnostic = context.Diagnostics.First();
        var expression = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true) as ExpressionSyntax;
        if (expression == null)
            return;

        var tokenName = diagnostic.Properties.TryGetValue(ExplicitNoneTokenAnalyzer.TokenNameProperty, out var name) && name != null
            ? name
            : "cancellationToken";

        context.RegisterCodeFix(
            CodeAction.Create(
                title: Title,
                createChangedDocument: c => ReplaceWithTokenAsync(context.Document, expression, tokenName, c),
                equivalenceKey: Title),
            diagnostic);
    }

    private static async Task<Document> ReplaceWithTokenAsync(
        Document document,
        ExpressionSyntax expression,
        string tokenName,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null)
            return document;

        var replacement = SyntaxFactory.IdentifierName(tokenName).WithTriviaFrom(expression);
        var newRoot = root.ReplaceNode(expression, replacement);
        return document.WithSyntaxRoot(newRoot);
    }
}
