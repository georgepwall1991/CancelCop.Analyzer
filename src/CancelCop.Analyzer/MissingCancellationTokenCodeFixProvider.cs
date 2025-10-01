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
using Microsoft.CodeAnalysis.Formatting;

namespace CancelCop.Analyzer;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(MissingCancellationTokenCodeFixProvider)), Shared]
public class MissingCancellationTokenCodeFixProvider : CodeFixProvider
{
    private const string Title = "Add CancellationToken parameter";

    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(MissingCancellationTokenAnalyzer.DiagnosticId);

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
                createChangedDocument: c => AddCancellationTokenParameterAsync(context.Document, methodDeclaration, c),
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

        // Create the CancellationToken parameter with default value
        var cancellationTokenParameter = SyntaxFactory.Parameter(
                SyntaxFactory.Identifier("cancellationToken"))
            .WithType(SyntaxFactory.ParseTypeName("CancellationToken"))
            .WithDefault(SyntaxFactory.EqualsValueClause(
                SyntaxFactory.LiteralExpression(SyntaxKind.DefaultLiteralExpression,
                    SyntaxFactory.Token(SyntaxKind.DefaultKeyword))));

        // Add the parameter to the method
        var newParameterList = methodDeclaration.ParameterList.AddParameters(cancellationTokenParameter);
        var newMethodDeclaration = methodDeclaration.WithParameterList(newParameterList)
            .WithAdditionalAnnotations(Formatter.Annotation);

        var newRoot = root.ReplaceNode(methodDeclaration, newMethodDeclaration);

        // Add using directive if needed, maintaining alphabetical order
        var compilationUnit = newRoot as CompilationUnitSyntax;
        if (compilationUnit != null)
        {
            var hasSystemThreadingUsing = compilationUnit.Usings
                .Any(u => u.Name?.ToString() == "System.Threading");

            if (!hasSystemThreadingUsing)
            {
                // Get the existing trailing trivia from the last using (if any) to preserve line endings
                var existingTrivia = compilationUnit.Usings.LastOrDefault()?.GetTrailingTrivia()
                    ?? SyntaxFactory.TriviaList(SyntaxFactory.LineFeed);

                var systemThreadingUsing = SyntaxFactory.UsingDirective(
                    SyntaxFactory.ParseName("System.Threading"))
                    .WithTrailingTrivia(existingTrivia);

                // Find the correct position to insert (alphabetically)
                var usings = compilationUnit.Usings.ToList();
                var insertIndex = 0;
                for (int i = 0; i < usings.Count; i++)
                {
                    if (string.CompareOrdinal("System.Threading", usings[i].Name?.ToString()) < 0)
                    {
                        insertIndex = i;
                        break;
                    }
                    insertIndex = i + 1;
                }

                usings.Insert(insertIndex, systemThreadingUsing);
                newRoot = compilationUnit.WithUsings(SyntaxFactory.List(usings));
            }
        }

        return document.WithSyntaxRoot(newRoot);
    }
}
