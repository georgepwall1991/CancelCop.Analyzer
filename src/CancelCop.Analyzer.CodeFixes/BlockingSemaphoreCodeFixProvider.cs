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
/// Code fix provider that rewrites a synchronous <c>gate.Wait()</c> to
/// <c>await gate.WaitAsync(token)</c>, flowing the in-scope token when one is available.
/// </summary>
[
    ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(BlockingSemaphoreCodeFixProvider)),
    Shared
]
public class BlockingSemaphoreCodeFixProvider : CodeFixProvider
{
    private const string Title = "Use await WaitAsync()";

    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(BlockingSemaphoreAnalyzer.DiagnosticId);

    public sealed override FixAllProvider GetFixAllProvider() =>
        WellKnownFixAllProviders.BatchFixer;

    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context
            .Document.GetSyntaxRootAsync(context.CancellationToken)
            .ConfigureAwait(false);
        if (root == null)
            return;

        var diagnostic = context.Diagnostics.First();
        // The diagnostic stands but the analyzer determined that inserting an await here would not
        // compile, so no rewrite is offered.
        if (diagnostic.Properties.ContainsKey(BlockingSemaphoreAnalyzer.NoFixProperty))
            return;
        var invocation = root.FindToken(diagnostic.Location.SourceSpan.Start)
            .Parent?.AncestorsAndSelf()
            .OfType<InvocationExpressionSyntax>()
            .FirstOrDefault();
        if (invocation?.Expression is not MemberAccessExpressionSyntax memberAccess)
            return;

        // Only withhold when this Wait (or a postfix chain on it) is the WhenNotNull
        // branch of `?.`. An argument nested inside an unrelated `holder?.Consume(...)`
        // is still a legal await.
        if (IsWhenNotNullOfConditionalAccess(invocation))
            return;

        var tokenName = diagnostic.Properties.TryGetValue(
            BlockingSemaphoreAnalyzer.TokenNameProperty,
            out var name
        )
            ? name
            : null;

        context.RegisterCodeFix(
            CodeAction.Create(
                title: Title,
                createChangedDocument: c =>
                    ReplaceAsync(context.Document, invocation, memberAccess, tokenName, c),
                equivalenceKey: Title
            ),
            diagnostic
        );
    }

    private static async Task<Document> ReplaceAsync(
        Document document,
        InvocationExpressionSyntax invocation,
        MemberAccessExpressionSyntax memberAccess,
        string? tokenName,
        CancellationToken cancellationToken
    )
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null)
            return document;

        // Carry the original Wait arguments (timeout and/or token) through to WaitAsync; only when
        // Wait() was parameterless do we add the in-scope token (if any).
        ArgumentListSyntax argumentList;
        if (invocation.ArgumentList.Arguments.Count > 0)
            argumentList = invocation.ArgumentList.WithoutTrivia();
        else if (tokenName != null)
            argumentList = SyntaxFactory.ArgumentList(
                SyntaxFactory.SingletonSeparatedList(
                    SyntaxFactory.Argument(CancellationTokenHelpers.TokenExpression(tokenName))
                )
            );
        else
            argumentList = SyntaxFactory.ArgumentList();

        var waitAsync = SyntaxFactory.InvocationExpression(
            SyntaxFactory.MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                memberAccess.Expression.WithoutTrivia(),
                SyntaxFactory.IdentifierName("WaitAsync")
            ),
            argumentList
        );

        ExpressionSyntax replacement = SyntaxFactory.AwaitExpression(waitAsync);
        if (CancellationTokenHelpers.AwaitNeedsParentheses(invocation))
            replacement = SyntaxFactory.ParenthesizedExpression(replacement);

        var newRoot = root.ReplaceNode(invocation, replacement.WithTriviaFrom(invocation));
        return document.WithSyntaxRoot(newRoot);
    }

    /// <summary>
    /// True when <paramref name="invocation"/> sits on the left spine of a
    /// conditional-access <c>WhenNotNull</c>, so wrapping it in <c>await</c> would
    /// produce <c>holder?await .Gate...</c>. An argument nested inside that
    /// branch is not on the spine and still rewrites.
    /// </summary>
    private static bool IsWhenNotNullOfConditionalAccess(InvocationExpressionSyntax invocation)
    {
        SyntaxNode current = invocation;
        while (current.Parent != null)
        {
            switch (current.Parent)
            {
                case ConditionalAccessExpressionSyntax conditional
                    when conditional.WhenNotNull == current:
                    return true;
                case MemberAccessExpressionSyntax member when member.Expression == current:
                case InvocationExpressionSyntax call when call.Expression == current:
                case ElementAccessExpressionSyntax element when element.Expression == current:
                case ConditionalAccessExpressionSyntax nested when nested.Expression == current:
                case PostfixUnaryExpressionSyntax postfix when postfix.Operand == current:
                case PrefixUnaryExpressionSyntax prefix when prefix.Operand == current:
                case CastExpressionSyntax cast when cast.Expression == current:
                case ParenthesizedExpressionSyntax paren when paren.Expression == current:
                case BinaryExpressionSyntax binary when binary.Left == current:
                case AssignmentExpressionSyntax assignment when assignment.Left == current:
                case ConditionalExpressionSyntax ternary when ternary.Condition == current:
                case IsPatternExpressionSyntax isPattern when isPattern.Expression == current:
                    current = current.Parent;
                    continue;
                default:
                    return false;
            }
        }

        return false;
    }
}
