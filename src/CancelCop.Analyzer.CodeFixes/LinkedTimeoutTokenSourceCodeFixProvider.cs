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

/// <summary>
/// Code fix that rewrites an unlinked timeout <c>CancellationTokenSource</c> to
/// <c>CreateLinkedTokenSource(token)</c> + <c>CancelAfter(delay)</c>.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(LinkedTimeoutTokenSourceCodeFixProvider)), Shared]
public class LinkedTimeoutTokenSourceCodeFixProvider : CodeFixProvider
{
    private const string Title = "Link timeout CTS to in-scope token";

    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(LinkedTimeoutTokenSourceAnalyzer.DiagnosticId);

    public sealed override FixAllProvider GetFixAllProvider() =>
        WellKnownFixAllProviders.BatchFixer;

    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root == null)
            return;

        var diagnostic = context.Diagnostics.First();
        var tokenName = diagnostic.Properties.TryGetValue(
                LinkedTimeoutTokenSourceAnalyzer.TokenNameProperty, out var name) && name != null
            ? name
            : "cancellationToken";

        // Path A: diagnostic on a timeout object creation (local initializer).
        var creation = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true)
            .AncestorsAndSelf()
            .OfType<BaseObjectCreationExpressionSyntax>()
            .FirstOrDefault();

        if (creation != null &&
            creation.Parent is EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax declarator } &&
            declarator.Parent is VariableDeclarationSyntax { Parent: LocalDeclarationStatementSyntax declaration } &&
            declaration.Declaration.Variables.Count == 1 &&
            creation.ArgumentList is { Arguments.Count: > 0 } argumentList)
        {
            var delayExpression = argumentList.Arguments[0].Expression;
            context.RegisterCodeFix(
                CodeAction.Create(
                    title: Title,
                    createChangedDocument: c => RewriteTimeoutCtorAsync(
                        context.Document, declaration, declarator, delayExpression, tokenName, c),
                    equivalenceKey: Title),
                diagnostic);
            return;
        }

        // Path B: diagnostic on CancelAfter — rewrite the parameterless local's initializer only.
        var memberAccess = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true)
            .AncestorsAndSelf()
            .OfType<MemberAccessExpressionSyntax>()
            .FirstOrDefault(m => m.Name.Identifier.Text == "CancelAfter");

        if (memberAccess?.Expression is not IdentifierNameSyntax receiverName)
            return;

        var semanticModel = await context.Document.GetSemanticModelAsync(context.CancellationToken)
            .ConfigureAwait(false);
        if (semanticModel == null)
            return;

        var local = semanticModel.GetSymbolInfo(receiverName, context.CancellationToken).Symbol as ILocalSymbol;
        if (local == null)
            return;

        foreach (var syntaxRef in local.DeclaringSyntaxReferences)
        {
            if (syntaxRef.GetSyntax(context.CancellationToken) is not VariableDeclaratorSyntax cancelDeclarator)
                continue;

            if (cancelDeclarator.Parent is not VariableDeclarationSyntax
                {
                    Parent: LocalDeclarationStatementSyntax cancelDeclaration
                })
            {
                continue;
            }

            if (cancelDeclaration.Declaration.Variables.Count != 1)
                continue;

            if (cancelDeclarator.Initializer?.Value is not BaseObjectCreationExpressionSyntax)
                continue;

            context.RegisterCodeFix(
                CodeAction.Create(
                    title: Title,
                    createChangedDocument: c => RewriteParameterlessInitializerAsync(
                        context.Document, cancelDeclarator, tokenName, c),
                    equivalenceKey: Title),
                diagnostic);
            return;
        }
    }

    private static async Task<Document> RewriteTimeoutCtorAsync(
        Document document,
        LocalDeclarationStatementSyntax declaration,
        VariableDeclaratorSyntax declarator,
        ExpressionSyntax delayExpression,
        string tokenName,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null)
            return document;

        var linkedCreation = CreateLinkedTokenSourceExpression(tokenName)
            .WithTriviaFrom(declarator.Initializer!.Value);

        var newDeclarator = declarator.WithInitializer(
            declarator.Initializer.WithValue(linkedCreation));
        var newDeclaration = declaration.ReplaceNode(declarator, newDeclarator);

        // Preserve the document's newline style by reusing a trailing end-of-line from the
        // rewritten declaration (tests and most sources use LF).
        var endOfLine = declaration.GetTrailingTrivia()
            .FirstOrDefault(t => t.IsKind(SyntaxKind.EndOfLineTrivia));
        var trailing = endOfLine != default
            ? SyntaxFactory.TriviaList(endOfLine)
            : SyntaxFactory.TriviaList(SyntaxFactory.LineFeed);

        var cancelAfter = CreateCancelAfterStatement(declarator.Identifier.Text, delayExpression)
            .WithLeadingTrivia(GetIndentTrivia(declaration))
            .WithTrailingTrivia(trailing)
            .WithAdditionalAnnotations(Formatter.Annotation);

        // Keep the declaration's trailing trivia (including its newline) and insert CancelAfter after.
        var statements = new SyntaxNode[] { newDeclaration, cancelAfter };

        if (declaration.Parent is BlockSyntax)
        {
            var newRoot = root.ReplaceNode(declaration, statements);
            return document.WithSyntaxRoot(newRoot);
        }

        // Fallback: only rewrite the initializer when the parent is not a block (rare).
        return document.WithSyntaxRoot(root.ReplaceNode(declaration, newDeclaration));
    }

    private static async Task<Document> RewriteParameterlessInitializerAsync(
        Document document,
        VariableDeclaratorSyntax declarator,
        string tokenName,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null || declarator.Initializer?.Value is not { } oldValue)
            return document;

        var linkedCreation = CreateLinkedTokenSourceExpression(tokenName)
            .WithTriviaFrom(oldValue);

        var newDeclarator = declarator.WithInitializer(
            declarator.Initializer.WithValue(linkedCreation));

        var newRoot = root.ReplaceNode(declarator, newDeclarator);
        return document.WithSyntaxRoot(newRoot);
    }

    private static InvocationExpressionSyntax CreateLinkedTokenSourceExpression(string tokenName) =>
        SyntaxFactory.InvocationExpression(
            SyntaxFactory.MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                SyntaxFactory.IdentifierName("CancellationTokenSource"),
                SyntaxFactory.IdentifierName("CreateLinkedTokenSource")),
            SyntaxFactory.ArgumentList(
                SyntaxFactory.SingletonSeparatedList(
                    SyntaxFactory.Argument(SyntaxFactory.IdentifierName(tokenName)))));

    private static ExpressionStatementSyntax CreateCancelAfterStatement(
        string localName,
        ExpressionSyntax delayExpression) =>
        SyntaxFactory.ExpressionStatement(
            SyntaxFactory.InvocationExpression(
                SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    SyntaxFactory.IdentifierName(localName),
                    SyntaxFactory.IdentifierName("CancelAfter")),
                SyntaxFactory.ArgumentList(
                    SyntaxFactory.SingletonSeparatedList(
                        SyntaxFactory.Argument(delayExpression.WithoutTrivia())))));

    private static SyntaxTriviaList GetIndentTrivia(LocalDeclarationStatementSyntax declaration)
    {
        // Reuse the declaration's leading whitespace so CancelAfter lines up with the local.
        var leading = declaration.GetLeadingTrivia();
        var whitespace = leading.Where(t => t.IsKind(SyntaxKind.WhitespaceTrivia)).LastOrDefault();
        return whitespace != default
            ? SyntaxFactory.TriviaList(whitespace)
            : SyntaxFactory.TriviaList(SyntaxFactory.Whitespace("        "));
    }
}
