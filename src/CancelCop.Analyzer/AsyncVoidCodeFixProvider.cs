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

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(AsyncVoidCodeFixProvider)), Shared]
public class AsyncVoidCodeFixProvider : CodeFixProvider
{
    private const string Title = "Change to async Task";

    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(AsyncVoidAnalyzer.DiagnosticId);

    public sealed override FixAllProvider GetFixAllProvider() =>
        WellKnownFixAllProviders.BatchFixer;

    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root == null)
            return;

        var diagnostic = context.Diagnostics.First();
        var diagnosticSpan = diagnostic.Location.SourceSpan;
        var token = root.FindToken(diagnosticSpan.Start);

        // Check for method declaration
        var methodDeclaration = token.Parent?.AncestorsAndSelf().OfType<MethodDeclarationSyntax>().FirstOrDefault();
        if (methodDeclaration != null)
        {
            context.RegisterCodeFix(
                CodeAction.Create(
                    title: Title,
                    createChangedDocument: c => ChangeMethodToTaskAsync(context.Document, methodDeclaration, c),
                    equivalenceKey: Title),
                diagnostic);
            return;
        }

        // Check for local function
        var localFunction = token.Parent?.AncestorsAndSelf().OfType<LocalFunctionStatementSyntax>().FirstOrDefault();
        if (localFunction != null)
        {
            context.RegisterCodeFix(
                CodeAction.Create(
                    title: Title,
                    createChangedDocument: c => ChangeLocalFunctionToTaskAsync(context.Document, localFunction, c),
                    equivalenceKey: Title),
                diagnostic);
        }
    }

    private static async Task<Document> ChangeMethodToTaskAsync(
        Document document,
        MethodDeclarationSyntax methodDeclaration,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null)
            return document;

        // Create Task return type
        var taskType = SyntaxFactory.ParseTypeName("Task")
            .WithTrailingTrivia(methodDeclaration.ReturnType.GetTrailingTrivia());

        var newMethodDeclaration = methodDeclaration.WithReturnType(taskType);
        var newRoot = root.ReplaceNode(methodDeclaration, newMethodDeclaration);

        // Add using directive if needed
        newRoot = EnsureUsingDirective(newRoot, "System.Threading.Tasks");

        return document.WithSyntaxRoot(newRoot);
    }

    private static async Task<Document> ChangeLocalFunctionToTaskAsync(
        Document document,
        LocalFunctionStatementSyntax localFunction,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null)
            return document;

        // Create Task return type
        var taskType = SyntaxFactory.ParseTypeName("Task")
            .WithTrailingTrivia(localFunction.ReturnType.GetTrailingTrivia());

        var newLocalFunction = localFunction.WithReturnType(taskType);
        var newRoot = root.ReplaceNode(localFunction, newLocalFunction);

        // Add using directive if needed
        newRoot = EnsureUsingDirective(newRoot, "System.Threading.Tasks");

        return document.WithSyntaxRoot(newRoot);
    }

    private static SyntaxNode EnsureUsingDirective(SyntaxNode root, string namespaceName)
    {
        if (root is not CompilationUnitSyntax compilationUnit)
            return root;

        var hasUsing = compilationUnit.Usings
            .Any(u => u.Name?.ToString() == namespaceName);

        if (hasUsing)
            return root;

        var existingTrivia = compilationUnit.Usings.LastOrDefault()?.GetTrailingTrivia()
            ?? SyntaxFactory.TriviaList(SyntaxFactory.LineFeed);

        var newUsing = SyntaxFactory.UsingDirective(
            SyntaxFactory.ParseName(namespaceName))
            .WithTrailingTrivia(existingTrivia);

        // Find the correct position to insert (alphabetically)
        var usings = compilationUnit.Usings.ToList();
        var insertIndex = 0;
        for (int i = 0; i < usings.Count; i++)
        {
            if (string.CompareOrdinal(namespaceName, usings[i].Name?.ToString()) < 0)
            {
                insertIndex = i;
                break;
            }
            insertIndex = i + 1;
        }

        usings.Insert(insertIndex, newUsing);
        return compilationUnit.WithUsings(SyntaxFactory.List(usings));
    }
}
