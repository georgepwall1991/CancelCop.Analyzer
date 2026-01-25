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

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ConfigureAwaitCodeFixProvider)), Shared]
public class ConfigureAwaitCodeFixProvider : CodeFixProvider
{
    private const string TitleFalse = "Add ConfigureAwait(false)";
    private const string TitleTrue = "Add ConfigureAwait(true)";

    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(ConfigureAwaitAnalyzer.DiagnosticId);

    public sealed override FixAllProvider GetFixAllProvider() =>
        WellKnownFixAllProviders.BatchFixer;

    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root == null)
            return;

        var diagnostic = context.Diagnostics.First();
        var diagnosticSpan = diagnostic.Location.SourceSpan;

        var awaitExpression = root.FindToken(diagnosticSpan.Start)
            .Parent?
            .AncestorsAndSelf()
            .OfType<AwaitExpressionSyntax>()
            .FirstOrDefault();

        if (awaitExpression == null)
            return;

        // Offer ConfigureAwait(false) - most common for library code
        context.RegisterCodeFix(
            CodeAction.Create(
                title: TitleFalse,
                createChangedDocument: c => AddConfigureAwaitAsync(context.Document, awaitExpression, false, c),
                equivalenceKey: TitleFalse),
            diagnostic);

        // Also offer ConfigureAwait(true) for completeness
        context.RegisterCodeFix(
            CodeAction.Create(
                title: TitleTrue,
                createChangedDocument: c => AddConfigureAwaitAsync(context.Document, awaitExpression, true, c),
                equivalenceKey: TitleTrue),
            diagnostic);
    }

    private static async Task<Document> AddConfigureAwaitAsync(
        Document document,
        AwaitExpressionSyntax awaitExpression,
        bool continueOnCapturedContext,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null)
            return document;

        // Create .ConfigureAwait(false/true) invocation
        var configureAwaitInvocation = SyntaxFactory.InvocationExpression(
            SyntaxFactory.MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                awaitExpression.Expression,
                SyntaxFactory.IdentifierName("ConfigureAwait")),
            SyntaxFactory.ArgumentList(
                SyntaxFactory.SingletonSeparatedList(
                    SyntaxFactory.Argument(
                        SyntaxFactory.LiteralExpression(
                            continueOnCapturedContext
                                ? SyntaxKind.TrueLiteralExpression
                                : SyntaxKind.FalseLiteralExpression)))));

        // Create new await expression with ConfigureAwait
        var newAwaitExpression = awaitExpression.WithExpression(configureAwaitInvocation);

        var newRoot = root.ReplaceNode(awaitExpression, newAwaitExpression);
        return document.WithSyntaxRoot(newRoot);
    }
}
