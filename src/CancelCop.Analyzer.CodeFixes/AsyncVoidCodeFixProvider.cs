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
/// Code fix provider that changes an <c>async void</c> method's return type to <c>Task</c>.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(AsyncVoidCodeFixProvider)), Shared]
public class AsyncVoidCodeFixProvider : CodeFixProvider
{
    private const string Title = "Return Task instead of void";
    private const string TasksNamespace = "System.Threading.Tasks";

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
        var node = root.FindToken(diagnostic.Location.SourceSpan.Start)
            .Parent?.AncestorsAndSelf()
            .FirstOrDefault(n => n is MethodDeclarationSyntax or LocalFunctionStatementSyntax);
        if (node == null)
            return;

        context.RegisterCodeFix(
            CodeAction.Create(
                title: Title,
                createChangedDocument: c => ChangeReturnTypeAsync(context.Document, node, c),
                equivalenceKey: Title),
            diagnostic);
    }

    private static async Task<Document> ChangeReturnTypeAsync(
        Document document,
        SyntaxNode node,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null)
            return document;

        // Carry the void keyword's trivia (the space before the name) onto Task.
        var returnType = node switch
        {
            MethodDeclarationSyntax m => m.ReturnType,
            LocalFunctionStatementSyntax l => l.ReturnType,
            _ => null,
        };
        if (returnType == null)
            return document;

        var taskType = SyntaxFactory.IdentifierName("Task").WithTriviaFrom(returnType);
        SyntaxNode newNode = node switch
        {
            MethodDeclarationSyntax m => m.WithReturnType(taskType),
            LocalFunctionStatementSyntax l => l.WithReturnType(taskType),
            _ => node,
        };

        var newRoot = root.ReplaceNode(node, newNode);

        if (newRoot is CompilationUnitSyntax compilationUnit)
            newRoot = CancellationTokenFixHelpers.AddUsing(compilationUnit, TasksNamespace);

        return document.WithSyntaxRoot(newRoot);
    }
}
