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

        var node = root.FindToken(diagnosticSpan.Start).Parent;
        if (node == null)
            return;

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

        // A simple lambda has a single UNTYPED parameter (e.g. `async id => ...`). Adding a
        // typed CancellationToken would mix typed/untyped parameters (CS0748), and an untyped
        // token is not recognized as a CancellationToken (the fix would re-fire). Such a lambda
        // is not a bindable minimal-API handler anyway, so no safe fix can be offered — skip it.
    }

    private static async Task<Document> AddCancellationTokenToParenthesizedLambdaAsync(
        Document document,
        ParenthesizedLambdaExpressionSyntax lambda,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null)
            return document;

        // Avoid colliding with a parameter (CS0100) or a local declared in the lambda body (CS0136).
        var tokenName = CancellationTokenFixHelpers.GetUniqueTokenParameterName(lambda.ParameterList, lambda.Body);

        var tokenParameter = SyntaxFactory.Parameter(SyntaxFactory.Identifier(tokenName))
            .WithType(SyntaxFactory.ParseTypeName("CancellationToken"));

        var newParameterList = lambda.ParameterList.AddParameters(tokenParameter);
        var newLambda = lambda.WithParameterList(newParameterList);

        var newRoot = root.ReplaceNode(lambda, newLambda);

        if (newRoot is CompilationUnitSyntax compilationUnit)
            newRoot = CancellationTokenFixHelpers.AddSystemThreadingUsing(compilationUnit);

        return document.WithSyntaxRoot(newRoot);
    }
}
