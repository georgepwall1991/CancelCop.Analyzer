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
        var method = root.FindToken(diagnostic.Location.SourceSpan.Start)
            .Parent?.AncestorsAndSelf().OfType<MethodDeclarationSyntax>().FirstOrDefault();
        if (method?.ReturnType == null)
            return;

        context.RegisterCodeFix(
            CodeAction.Create(
                title: Title,
                createChangedDocument: c => ChangeReturnTypeAsync(context.Document, method, c),
                equivalenceKey: Title),
            diagnostic);
    }

    private static async Task<Document> ChangeReturnTypeAsync(
        Document document,
        MethodDeclarationSyntax method,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null)
            return document;

        // Carry the void keyword's trivia (the space before the method name) onto Task.
        var taskType = SyntaxFactory.IdentifierName("Task").WithTriviaFrom(method.ReturnType);
        var newMethod = method.WithReturnType(taskType);

        var newRoot = root.ReplaceNode(method, newMethod);

        if (newRoot is CompilationUnitSyntax compilationUnit)
            newRoot = CancellationTokenFixHelpers.AddUsing(compilationUnit, TasksNamespace);

        return document.WithSyntaxRoot(newRoot);
    }
}
