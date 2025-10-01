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

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(MinimalApiCodeFixProvider)), Shared]
public class MinimalApiCodeFixProvider : CodeFixProvider
{
    private const string Title = "Add CancellationToken parameter";

    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create("CC005C");

    public sealed override FixAllProvider GetFixAllProvider() =>
        WellKnownFixAllProviders.BatchFixer;

    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root == null)
            return;

        var diagnostic = context.Diagnostics.First();
        var diagnosticSpan = diagnostic.Location.SourceSpan;

        // Find the lambda expression at the diagnostic location
        var node = root.FindToken(diagnosticSpan.Start).Parent;
        if (node == null)
            return;

        // Check for ParenthesizedLambdaExpressionSyntax
        var parenthesizedLambda = node.AncestorsAndSelf()
            .OfType<ParenthesizedLambdaExpressionSyntax>()
            .FirstOrDefault();

        if (parenthesizedLambda != null)
        {
            context.RegisterCodeFix(
                CodeAction.Create(
                    title: Title,
                    createChangedDocument: c => AddCancellationTokenToParenthesizedLambdaAsync(
                        context.Document, parenthesizedLambda, c),
                    equivalenceKey: Title),
                diagnostic);
            return;
        }

        // Check for SimpleLambdaExpressionSyntax
        var simpleLambda = node.AncestorsAndSelf()
            .OfType<SimpleLambdaExpressionSyntax>()
            .FirstOrDefault();

        if (simpleLambda != null)
        {
            context.RegisterCodeFix(
                CodeAction.Create(
                    title: Title,
                    createChangedDocument: c => AddCancellationTokenToSimpleLambdaAsync(
                        context.Document, simpleLambda, c),
                    equivalenceKey: Title),
                diagnostic);
        }
    }

    private static async Task<Document> AddCancellationTokenToParenthesizedLambdaAsync(
        Document document,
        ParenthesizedLambdaExpressionSyntax lambda,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null)
            return document;

        // Create the CancellationToken parameter
        var tokenParameter = SyntaxFactory.Parameter(
                SyntaxFactory.Identifier("cancellationToken"))
            .WithType(SyntaxFactory.ParseTypeName("CancellationToken"));

        // Add the parameter to the lambda
        var newParameterList = lambda.ParameterList.AddParameters(tokenParameter);
        var newLambda = lambda.WithParameterList(newParameterList);

        var newRoot = root.ReplaceNode(lambda, newLambda);

        return document.WithSyntaxRoot(newRoot);
    }

    private static async Task<Document> AddCancellationTokenToSimpleLambdaAsync(
        Document document,
        SimpleLambdaExpressionSyntax lambda,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null)
            return document;

        // Convert simple lambda to parenthesized lambda to add multiple parameters
        var tokenParameter = SyntaxFactory.Parameter(
                SyntaxFactory.Identifier("cancellationToken"))
            .WithType(SyntaxFactory.ParseTypeName("CancellationToken"));

        var parameters = SyntaxFactory.ParameterList(
            SyntaxFactory.SeparatedList(new[] { lambda.Parameter, tokenParameter }));

        var newLambda = SyntaxFactory.ParenthesizedLambdaExpression(
            lambda.AsyncKeyword,
            parameters,
            lambda.ArrowToken,
            lambda.Body);

        var newRoot = root.ReplaceNode(lambda, newLambda);

        return document.WithSyntaxRoot(newRoot);
    }
}
