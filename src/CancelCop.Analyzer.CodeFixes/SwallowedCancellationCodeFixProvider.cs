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
/// Code fix provider that rethrows <c>OperationCanceledException</c> from a broad catch, so
/// cancellation propagates instead of being swallowed.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(SwallowedCancellationCodeFixProvider)), Shared]
public class SwallowedCancellationCodeFixProvider : CodeFixProvider
{
    private const string Title = "Rethrow OperationCanceledException";
    private const string SystemNamespace = "System";

    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(SwallowedCancellationAnalyzer.DiagnosticId);

    public sealed override FixAllProvider GetFixAllProvider() =>
        WellKnownFixAllProviders.BatchFixer;

    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root == null)
            return;

        var diagnostic = context.Diagnostics.First();
        var catchClause = root.FindToken(diagnostic.Location.SourceSpan.Start)
            .Parent?.AncestorsAndSelf().OfType<CatchClauseSyntax>().FirstOrDefault();

        // A typed catch is required: the rethrow guard tests a named exception. A bare catch-all has
        // no exception variable to test, so no fix is offered there.
        if (catchClause?.Declaration?.Type == null || catchClause.Block == null)
            return;

        context.RegisterCodeFix(
            CodeAction.Create(
                title: Title,
                createChangedDocument: c => AddRethrowGuardAsync(context.Document, catchClause, c),
                equivalenceKey: Title),
            diagnostic);
    }

    private static async Task<Document> AddRethrowGuardAsync(
        Document document,
        CatchClauseSyntax catchClause,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null)
            return document;

        var declaration = catchClause.Declaration!;

        // Ensure the caught exception has a name to test in the rethrow guard.
        var identifier = declaration.Identifier.IsKind(SyntaxKind.None)
            ? SyntaxFactory.Identifier("ex")
            : declaration.Identifier;
        var newDeclaration = declaration.WithIdentifier(identifier);

        // if (ex is OperationCanceledException) throw;
        // Built as a type-checking `is` BinaryExpression (the shape the parser produces for a bare
        // type), so the fixed tree round-trips against the expected source.
        var guard = SyntaxFactory.IfStatement(
            SyntaxFactory.BinaryExpression(
                SyntaxKind.IsExpression,
                SyntaxFactory.IdentifierName(identifier),
                SyntaxFactory.IdentifierName("OperationCanceledException")),
            SyntaxFactory.ThrowStatement());

        var newBlock = catchClause.Block.WithStatements(
            catchClause.Block.Statements.Insert(0, guard));

        var newCatch = catchClause
            .WithDeclaration(newDeclaration)
            .WithBlock(newBlock);

        var newRoot = root.ReplaceNode(catchClause, newCatch);

        if (newRoot is CompilationUnitSyntax compilationUnit)
            newRoot = CancellationTokenFixHelpers.AddUsing(compilationUnit, SystemNamespace);

        return document.WithSyntaxRoot(newRoot);
    }
}
