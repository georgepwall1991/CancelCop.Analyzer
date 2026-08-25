using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CancelCop.Analyzer;

/// <summary>
/// Shared machinery for code fixers that rewrite a null-conditional blocking statement
/// (<c>x?.Work.Result;</c>, <c>cts?.Cancel();</c>) into an explicit null check
/// (<c>if (x is not null) { await x.Work; }</c>). An in-place <c>await</c> cannot be inserted on
/// the spine of a <c>?.</c>, but as a whole statement the access can be hoisted out of it.
/// The rewrite is semantics-preserving only under the eligibility rules enforced here:
/// the operation must re-evaluate identically (a local, parameter, or <c>this</c>), no nested
/// <c>?.</c> may survive the splice, and the statement must not sit where a new if-statement
/// would capture an enclosing <c>else</c>.
/// </summary>
internal static class NullConditionalHoist
{
    /// <summary>
    /// Walks up from <paramref name="node"/> to its enclosing expression statement. Returns true
    /// when that statement's expression is exactly a null-conditional access whose operation is
    /// safe to re-evaluate and whose surroundings survive the rewrite.
    /// </summary>
    public static bool TryGetStatement(
        SemanticModel semanticModel,
        SyntaxNode node,
        out ExpressionStatementSyntax statement,
        out ConditionalAccessExpressionSyntax conditionalAccess
    )
    {
        for (var current = node.Parent; current != null; current = current.Parent)
        {
            if (current is not ConditionalAccessExpressionSyntax candidate)
                continue;
            if (
                candidate.Parent is ExpressionStatementSyntax enclosing
                && enclosing.Expression == candidate
                && IsReEvaluableOperation(candidate.Expression, semanticModel)
                && !IntroducesDanglingElse(enclosing)
            )
            {
                statement = enclosing;
                conditionalAccess = candidate;
                return true;
            }
        }

        statement = null!;
        conditionalAccess = null!;
        return false;
    }

    /// <summary>
    /// The hoist evaluates the operation twice (once in the condition, once in the hoisted call),
    /// so only symbols whose repeated read cannot run code or change identity qualify: a local, a
    /// parameter, or <c>this</c>. A bare identifier can still bind to a property inside its own
    /// class, so the check is semantic, not syntactic.
    /// </summary>
    public static bool IsReEvaluableOperation(
        ExpressionSyntax expression,
        SemanticModel semanticModel
    )
    {
        var unwrapped = expression;
        while (unwrapped is ParenthesizedExpressionSyntax parenthesized)
            unwrapped = parenthesized.Expression;

        return unwrapped switch
        {
            ThisExpressionSyntax => true,
            IdentifierNameSyntax identifier => semanticModel.GetSymbolInfo(identifier).Symbol
                is IParameterSymbol
                or ILocalSymbol,
            _ => false,
        };
    }

    /// <summary>
    /// True when replacing this statement with an if-statement would let an enclosing <c>else</c>
    /// re-bind to the new check: the statement sits (directly or through other unbraced embedded
    /// bodies — <c>while</c>, <c>for</c>, <c>using</c>, <c>lock</c>, …) as the unbraced body of an
    /// <c>if</c> that has an <c>else</c>. A braced body scopes else-binding again, so the walk
    /// stops there.
    /// </summary>
    public static bool IntroducesDanglingElse(ExpressionStatementSyntax statement)
    {
        for (var node = (SyntaxNode)statement; node.Parent != null; node = node.Parent)
        {
            if (node is BlockSyntax)
                return false;
            if (
                node.Parent is IfStatementSyntax parentIf
                && parentIf.Statement == node
                && parentIf.Else != null
            )
                return true;
        }

        return false;
    }

    /// <summary>
    /// Rebuilds the leading receiver-less member binding of a <c>?. </c> spine as an ordinary
    /// member access over the hoisted operation, walking the leftmost chain
    /// (<c>.Cts.Cancel()</c> over operation <c>x</c> becomes <c>x.Cts.Cancel()</c>). Returns
    /// false when the leftmost node is not a member binding — an element binding or a
    /// null-forgiving operator there would produce uncompilable syntax.
    /// </summary>
    public static bool TrySpliceOperation(
        ExpressionSyntax expression,
        ExpressionSyntax operation,
        out ExpressionSyntax result
    )
    {
        switch (expression)
        {
            case MemberBindingExpressionSyntax binding:
                result = SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    operation,
                    SyntaxFactory.Token(SyntaxKind.DotToken),
                    binding.Name
                );
                return true;
            case MemberAccessExpressionSyntax member:
                if (TrySpliceOperation(member.Expression, operation, out var innerMember))
                {
                    result = member.WithExpression(innerMember);
                    return true;
                }
                break;
            case InvocationExpressionSyntax invocation:
                if (TrySpliceOperation(invocation.Expression, operation, out var innerCall))
                {
                    result = invocation.WithExpression(innerCall);
                    return true;
                }
                break;
            case ParenthesizedExpressionSyntax parenthesized:
                if (TrySpliceOperation(parenthesized.Expression, operation, out var innerParen))
                {
                    result = parenthesized.WithExpression(innerParen);
                    return true;
                }
                break;
        }

        result = null!;
        return false;
    }

    public static bool ContainsNullConditionalAccess(SyntaxNode node) =>
        node is ConditionalAccessExpressionSyntax
        || node.DescendantNodes().Any(n => n is ConditionalAccessExpressionSyntax);

    /// <summary>
    /// The generated <c>is not null</c> check is a C# 9 pattern; older language versions would
    /// receive a non-compiling fix and stay without a rewrite instead.
    /// </summary>
    public static bool SupportsIsNotNullPattern(SemanticModel semanticModel) =>
        semanticModel.Compilation is CSharpCompilation compilation
        && compilation.LanguageVersion >= LanguageVersion.CSharp9;

    /// <summary>
    /// A nullable-struct operation (<c>nullableStruct?.Field.Task</c>) would not compile after
    /// the hoist — outside <c>?.</c>, the compiler inserts no <c>.Value</c>.
    /// </summary>
    public static bool IsNullableStructOperation(
        SemanticModel semanticModel,
        ExpressionSyntax operation
    ) =>
        semanticModel
            .GetTypeInfo(operation)
            .Type?
            .OriginalDefinition
            .SpecialType == SpecialType.System_Nullable_T;

    /// <summary>
    /// Replaces <paramref name="statement"/> with
    /// <c>if (op is not null) { await awaitedExpression; }</c>, preserving the statement's
    /// surrounding trivia and matching the file's indentation and newline style.
    /// </summary>
    public static async Task<Document> ReplaceStatementWithIfNotNullAsync(
        Document document,
        ExpressionStatementSyntax statement,
        ConditionalAccessExpressionSyntax conditionalAccess,
        ExpressionSyntax awaitedExpression,
        CancellationToken cancellationToken
    )
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null)
            return document;

        var leading = statement.GetLeadingTrivia();
        var endOfLine = leading.FirstOrDefault(t => t.IsKind(SyntaxKind.EndOfLineTrivia));
        var newLine = endOfLine != default ? endOfLine.ToFullString() : "\n";
        var indentationTrivia = leading.LastOrDefault(t => t.IsKind(SyntaxKind.WhitespaceTrivia));
        var indentation = indentationTrivia != default ? indentationTrivia.ToString() : "";

        var awaitedStatement = SyntaxFactory.ExpressionStatement(
            SyntaxFactory.AwaitExpression(awaitedExpression.WithoutTrivia())
        );
        awaitedStatement = awaitedStatement.WithLeadingTrivia(
            SyntaxFactory.TriviaList(
                SyntaxFactory.EndOfLine(newLine),
                SyntaxFactory.Whitespace(indentation + "    ")
            )
        );

        var block = SyntaxFactory.Block(SyntaxFactory.SingletonList<StatementSyntax>(awaitedStatement));
        block = block
            .WithOpenBraceToken(block.OpenBraceToken.WithLeadingTrivia(SyntaxFactory.Space))
            .WithCloseBraceToken(
                block.CloseBraceToken.WithLeadingTrivia(
                    SyntaxFactory.TriviaList(
                        SyntaxFactory.EndOfLine(newLine),
                        SyntaxFactory.Whitespace(indentation)
                    )
                )
            );

        // `x is not null` — safe even for types with user-defined equality operators.
        var condition = SyntaxFactory.IsPatternExpression(
            conditionalAccess.Expression.WithoutTrivia(),
            SyntaxFactory.UnaryPattern(
                SyntaxFactory.Token(SyntaxKind.NotKeyword),
                SyntaxFactory.ConstantPattern(
                    SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression)
                )
            )
        );

        var ifStatement = SyntaxFactory.IfStatement(condition, block)
            .WithLeadingTrivia(statement.GetLeadingTrivia())
            .WithTrailingTrivia(statement.GetTrailingTrivia());

        var newRoot = root.ReplaceNode(statement, ifStatement);
        return document.WithSyntaxRoot(newRoot);
    }
}
