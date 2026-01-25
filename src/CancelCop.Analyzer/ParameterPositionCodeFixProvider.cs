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

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ParameterPositionCodeFixProvider)), Shared]
public class ParameterPositionCodeFixProvider : CodeFixProvider
{
    private const string Title = "Move CancellationToken to last position";

    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(ParameterPositionAnalyzer.DiagnosticId);

    public sealed override FixAllProvider GetFixAllProvider() =>
        WellKnownFixAllProviders.BatchFixer;

    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root == null)
            return;

        var diagnostic = context.Diagnostics.First();
        var diagnosticSpan = diagnostic.Location.SourceSpan;

        var parameterSyntax = root.FindNode(diagnosticSpan) as ParameterSyntax;
        if (parameterSyntax == null)
            return;

        var methodDeclaration = parameterSyntax.AncestorsAndSelf()
            .OfType<MethodDeclarationSyntax>()
            .FirstOrDefault();

        if (methodDeclaration == null)
            return;

        context.RegisterCodeFix(
            CodeAction.Create(
                title: Title,
                createChangedDocument: c => MoveParameterToLastAsync(context.Document, methodDeclaration, parameterSyntax, c),
                equivalenceKey: Title),
            diagnostic);
    }

    private static async Task<Document> MoveParameterToLastAsync(
        Document document,
        MethodDeclarationSyntax methodDeclaration,
        ParameterSyntax cancellationTokenParameter,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null)
            return document;

        // Get existing parameters
        var parameters = methodDeclaration.ParameterList.Parameters.ToList();

        // Find and remove the CancellationToken parameter
        var ctIndex = parameters.IndexOf(cancellationTokenParameter);
        if (ctIndex == -1)
            return document;

        parameters.RemoveAt(ctIndex);

        // Preserve trivia from the removed parameter
        var ctParameterWithTrivia = cancellationTokenParameter;

        // If there are remaining parameters, fix up the commas/trivia
        if (parameters.Count > 0)
        {
            // Remove trailing comma from what's now the last parameter
            var lastParam = parameters[parameters.Count - 1];
            if (lastParam.GetTrailingTrivia().Any(t => t.IsKind(SyntaxKind.WhitespaceTrivia)))
            {
                // Keep proper spacing
            }

            // Ensure proper leading trivia on CancellationToken (space after comma)
            ctParameterWithTrivia = cancellationTokenParameter
                .WithLeadingTrivia(SyntaxFactory.Space);
        }

        // Add CancellationToken at the end
        parameters.Add(ctParameterWithTrivia);

        // Create the new parameter list with proper separators
        var separatedList = SyntaxFactory.SeparatedList(
            parameters,
            Enumerable.Repeat(
                SyntaxFactory.Token(SyntaxKind.CommaToken).WithTrailingTrivia(SyntaxFactory.Space),
                parameters.Count - 1));

        var newParameterList = methodDeclaration.ParameterList.WithParameters(separatedList);
        var newMethodDeclaration = methodDeclaration.WithParameterList(newParameterList);

        var newRoot = root.ReplaceNode(methodDeclaration, newMethodDeclaration);
        return document.WithSyntaxRoot(newRoot);
    }
}
