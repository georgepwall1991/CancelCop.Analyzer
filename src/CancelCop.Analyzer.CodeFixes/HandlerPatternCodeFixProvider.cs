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

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(HandlerPatternCodeFixProvider)), Shared]
public class HandlerPatternCodeFixProvider : CodeFixProvider
{
    private const string Title = "Add CancellationToken parameter";

    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create("CC005A", "CC005B", "CC018");

    public sealed override FixAllProvider GetFixAllProvider() =>
        WellKnownFixAllProviders.BatchFixer;

    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root == null)
            return;

        var diagnostic = context.Diagnostics.First();
        var diagnosticSpan = diagnostic.Location.SourceSpan;

        var methodDeclaration = root.FindToken(diagnosticSpan.Start)
            .Parent?
            .AncestorsAndSelf()
            .OfType<MethodDeclarationSyntax>()
            .First();

        if (methodDeclaration == null)
            return;

        context.RegisterCodeFix(
            CodeAction.Create(
                title: Title,
                createChangedDocument: c => AddCancellationTokenParameterAsync(
                    context.Document, methodDeclaration, c),
                equivalenceKey: Title),
            diagnostic);
    }

    private static async Task<Document> AddCancellationTokenParameterAsync(
        Document document,
        MethodDeclarationSyntax methodDeclaration,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null)
            return document;

        // Choose a non-colliding name (CS0100), also avoiding locals in the body (CS0136).
        var tokenName = CancellationTokenFixHelpers.GetUniqueTokenParameterName(
            methodDeclaration.ParameterList,
            methodDeclaration.Body ?? (SyntaxNode?)methodDeclaration.ExpressionBody);

        var tokenParameter = SyntaxFactory.Parameter(
                SyntaxFactory.Identifier(tokenName))
            .WithType(SyntaxFactory.ParseTypeName("CancellationToken"));

        // A required token after an optional parameter is CS1737 — give it a default in that case.
        if (CancellationTokenFixHelpers.RequiresDefaultToAppend(methodDeclaration.ParameterList))
            tokenParameter = tokenParameter.WithDefault(CancellationTokenFixHelpers.DefaultValueClause());

        // Insert before any trailing 'params' parameter (CS0231 guard); otherwise append.
        var newParameterList = CancellationTokenFixHelpers.InsertTokenParameter(
            methodDeclaration.ParameterList, tokenParameter);
        var newMethodDeclaration = methodDeclaration.WithParameterList(newParameterList);

        var newRoot = root.ReplaceNode(methodDeclaration, newMethodDeclaration);

        // Add 'using System.Threading;' when missing (CS0246 guard).
        if (newRoot is CompilationUnitSyntax compilationUnit)
            newRoot = CancellationTokenFixHelpers.AddSystemThreadingUsing(compilationUnit);

        return document.WithSyntaxRoot(newRoot);
    }
}
