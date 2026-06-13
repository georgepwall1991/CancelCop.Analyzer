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
/// Code fix provider that converts an undisposed <c>CancellationTokenSource</c> local declaration
/// into a <c>using</c> declaration.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(UndisposedTokenSourceCodeFixProvider)), Shared]
public class UndisposedTokenSourceCodeFixProvider : CodeFixProvider
{
    private const string Title = "Add 'using' declaration";

    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(UndisposedTokenSourceAnalyzer.DiagnosticId);

    public sealed override FixAllProvider GetFixAllProvider() =>
        WellKnownFixAllProviders.BatchFixer;

    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root == null)
            return;

        var diagnostic = context.Diagnostics.First();
        var declaration = root.FindToken(diagnostic.Location.SourceSpan.Start)
            .Parent?.AncestorsAndSelf().OfType<LocalDeclarationStatementSyntax>().FirstOrDefault();
        if (declaration == null)
            return;

        context.RegisterCodeFix(
            CodeAction.Create(
                title: Title,
                createChangedDocument: c => AddUsingDeclarationAsync(context.Document, declaration, c),
                equivalenceKey: Title),
            diagnostic);
    }

    private static async Task<Document> AddUsingDeclarationAsync(
        Document document,
        LocalDeclarationStatementSyntax declaration,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null)
            return document;

        // Move the statement's leading trivia (indentation) onto the new 'using' keyword so the
        // 'var'/type token no longer carries it (otherwise it renders as `using   var`).
        var leading = declaration.GetLeadingTrivia();
        var usingKeyword = SyntaxFactory.Token(SyntaxKind.UsingKeyword)
            .WithLeadingTrivia(leading)
            .WithTrailingTrivia(SyntaxFactory.Space);

        var newDeclaration = declaration
            .WithLeadingTrivia(SyntaxFactory.TriviaList())
            .WithUsingKeyword(usingKeyword);

        var newRoot = root.ReplaceNode(declaration, newDeclaration);
        return document.WithSyntaxRoot(newRoot);
    }
}
