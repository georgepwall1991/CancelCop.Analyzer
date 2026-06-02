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
/// Code fix provider that adds ThrowIfCancellationRequested() call to loops.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(LoopCancellationCodeFixProvider)), Shared]
public class LoopCancellationCodeFixProvider : CodeFixProvider
{
    private const string Title = "Add cancellation check to loop";

    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(LoopCancellationAnalyzer.DiagnosticId);

    public sealed override FixAllProvider GetFixAllProvider() =>
        WellKnownFixAllProviders.BatchFixer;

    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root == null)
            return;

        var diagnostic = context.Diagnostics.First();
        var diagnosticSpan = diagnostic.Location.SourceSpan;

        // Find the loop keyword token
        var token = root.FindToken(diagnosticSpan.Start);
        var loopNode = token.Parent;

        if (loopNode == null)
            return;

        // Get the token name from diagnostic properties
        var tokenName = diagnostic.Properties.TryGetValue(LoopCancellationAnalyzer.TokenNameProperty, out var name) && name != null
            ? name
            : "cancellationToken";

        context.RegisterCodeFix(
            CodeAction.Create(
                title: Title,
                createChangedDocument: c => AddCancellationCheckAsync(
                    context.Document, loopNode, tokenName, c),
                equivalenceKey: Title),
            diagnostic);
    }

    private static async Task<Document> AddCancellationCheckAsync(
        Document document,
        SyntaxNode loopNode,
        string tokenName,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null)
            return document;

        // Create the ThrowIfCancellationRequested statement
        var cancellationCheckStatement = CreateCancellationCheckStatement(tokenName);

        // Get the loop body and insert the cancellation check as the first statement
        SyntaxNode newLoopNode = loopNode switch
        {
            ForStatementSyntax forStatement => UpdateForStatement(forStatement, cancellationCheckStatement),
            ForEachStatementSyntax foreachStatement => UpdateForEachStatement(foreachStatement, cancellationCheckStatement),
            WhileStatementSyntax whileStatement => UpdateWhileStatement(whileStatement, cancellationCheckStatement),
            DoStatementSyntax doStatement => UpdateDoStatement(doStatement, cancellationCheckStatement),
            _ => loopNode
        };

        var newRoot = root.ReplaceNode(loopNode, newLoopNode);
        return document.WithSyntaxRoot(newRoot);
    }

    private static ExpressionStatementSyntax CreateCancellationCheckStatement(string tokenName)
    {
        // Create: tokenName.ThrowIfCancellationRequested();
        return SyntaxFactory.ExpressionStatement(
            SyntaxFactory.InvocationExpression(
                SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    SyntaxFactory.IdentifierName(tokenName),
                    SyntaxFactory.IdentifierName("ThrowIfCancellationRequested"))));
    }

    private static ForStatementSyntax UpdateForStatement(ForStatementSyntax forStatement, StatementSyntax cancellationCheck)
    {
        var newBody = InsertStatementIntoBody(forStatement.Statement, cancellationCheck);
        return forStatement.WithStatement(newBody);
    }

    private static ForEachStatementSyntax UpdateForEachStatement(ForEachStatementSyntax foreachStatement, StatementSyntax cancellationCheck)
    {
        var newBody = InsertStatementIntoBody(foreachStatement.Statement, cancellationCheck);
        return foreachStatement.WithStatement(newBody);
    }

    private static WhileStatementSyntax UpdateWhileStatement(WhileStatementSyntax whileStatement, StatementSyntax cancellationCheck)
    {
        var newBody = InsertStatementIntoBody(whileStatement.Statement, cancellationCheck);
        return whileStatement.WithStatement(newBody);
    }

    private static DoStatementSyntax UpdateDoStatement(DoStatementSyntax doStatement, StatementSyntax cancellationCheck)
    {
        var newBody = InsertStatementIntoBody(doStatement.Statement, cancellationCheck);
        return doStatement.WithStatement(newBody);
    }

    private static StatementSyntax InsertStatementIntoBody(StatementSyntax body, StatementSyntax statementToInsert)
    {
        if (body is BlockSyntax block)
        {
            // Insert as first statement in the block
            var newStatements = block.Statements.Insert(0, statementToInsert);
            return block.WithStatements(newStatements);
        }
        else
        {
            // Single statement body - convert to block with cancellation check first
            return SyntaxFactory.Block(statementToInsert, body);
        }
    }
}
