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
/// Code fix provider that rewrites a synchronous <c>cts.Cancel()</c> to
/// <c>await cts.CancelAsync()</c>.
/// </summary>
[
    ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(PreferCancelAsyncCodeFixProvider)),
    Shared
]
public class PreferCancelAsyncCodeFixProvider : CodeFixProvider
{
    private const string Title = "Use await CancelAsync()";

    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(PreferCancelAsyncAnalyzer.DiagnosticId);

    public sealed override FixAllProvider GetFixAllProvider() =>
        WellKnownFixAllProviders.BatchFixer;

    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context
            .Document.GetSyntaxRootAsync(context.CancellationToken)
            .ConfigureAwait(false);
        if (root == null)
            return;

        var semanticModel = await context
            .Document.GetSemanticModelAsync(context.CancellationToken)
            .ConfigureAwait(false);
        if (semanticModel == null)
            return;

        var diagnostic = context.Diagnostics.First();
        var isConditionalAccess =
            diagnostic.Properties.TryGetValue(PreferCancelAsyncAnalyzer.NoFixProperty, out var reason)
            && reason == PreferCancelAsyncAnalyzer.ConditionalAccessReason;
        // The diagnostic stands but a plain in-place await insertion would not compile.
        if (!isConditionalAccess && diagnostic.Properties.ContainsKey(PreferCancelAsyncAnalyzer.NoFixProperty))
            return;

        var invocation = root.FindToken(diagnostic.Location.SourceSpan.Start)
            .Parent?.AncestorsAndSelf()
            .OfType<InvocationExpressionSyntax>()
            .FirstOrDefault();
        // On a direct `?.` spine the receiver is a member binding (`cts?.Cancel()`); chained
        // calls keep an ordinary member access whose leftmost node is the spliced operation
        // (`holder?.Cts.Cancel()`).
        if (invocation.Expression is not (MemberAccessExpressionSyntax or MemberBindingExpressionSyntax))
            return;
        var memberAccess = invocation.Expression as MemberAccessExpressionSyntax;
        // `holder?.Cts.Cancel()` is an ordinary member access, but it is the WhenNotNull
        // of `?.`. Replacing it with `await .Cts.CancelAsync()` yields
        // `holder? await.Cts.CancelAsync()`, which does not parse. As a statement, though,
        // the whole null-conditional call hoists to an `is not null` check (below).
        if (
            !isConditionalAccess
            && CancellationTokenHelpers.IsWhenNotNullOfConditionalAccess(invocation)
        )
            return;

        if (isConditionalAccess)
        {
            if (
                TryGetHoistableNullConditionalStatement(
                    invocation,
                    semanticModel,
                    out var statement,
                    out var conditionalAccess
                )
                && TryBuildHoistedCall(
                    semanticModel,
                    conditionalAccess,
                    invocation,
                    out var hoistedCall
                )
            )
            {
                context.RegisterCodeFix(
                    CodeAction.Create(
                        title: Title,
                        createChangedDocument: c =>
                            HoistToIfNotNullAsync(
                                context.Document,
                                statement,
                                conditionalAccess,
                                hoistedCall,
                                c
                            ),
                        equivalenceKey: Title
                    ),
                    diagnostic
                );
            }
            return;
        }
 
         context.RegisterCodeFix(
             CodeAction.Create(
                 title: Title,
                 createChangedDocument: c =>
                     ReplaceAsync(context.Document, invocation, memberAccess, c),
                 equivalenceKey: Title
            ),
            diagnostic
        );
     }

    /// <summary>
    /// Walks up from the invocation to its enclosing expression statement. Returns true when that
    /// statement's expression is exactly a null-conditional access whose operation is safe to
    /// re-evaluate and whose surroundings survive the rewrite — which makes the
    /// <c>x?.M()</c> → <c>if (x is not null) { … x.M() … }</c> hoist semantics-preserving.
    /// </summary>
    private static bool TryGetHoistableNullConditionalStatement(
        SyntaxNode node,
        SemanticModel semanticModel,
        out ExpressionStatementSyntax statement,
        out ConditionalAccessExpressionSyntax? conditionalAccess
    )
    {
        for (var current = node.Parent; current != null; current = current.Parent)
        {
            if (current is not ConditionalAccessExpressionSyntax candidate)
                continue;
            if (
                candidate.Parent is ExpressionStatementSyntax enclosing
                && enclosing.Expression == candidate
                && IsSimpleNullCheckReceiver(candidate.Expression, semanticModel)

                // `if (flag) cts?.Cancel(); else …` — replacing the unbraced body with another
                // if-statement would re-bind the outer `else` to the new check.
                && !(
                    enclosing.Parent is IfStatementSyntax outerIf
                    && outerIf.Statement == enclosing
                    && outerIf.Else != null
                )
            )
            {
                statement = enclosing;
                conditionalAccess = candidate;
                return true;
            }
        }

        statement = null!;
        conditionalAccess = null;
        return false;
    }

    /// <summary>
    /// The hoist evaluates the receiver twice (once in the condition, once in the awaited call),
    /// so only symbols whose repeated read cannot run code or change identity qualify: a local,
    /// a parameter, or <c>this</c>. A bare identifier can still bind to a property inside its
    /// own class, so the check is semantic, not syntactic.
    /// </summary>
    private static bool IsSimpleNullCheckReceiver(
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
    /// Builds the awaited call for the hoisted rewrite, or returns false when the shape must
    /// stay unfixed. Renames Cancel → CancelAsync in place and splices the operation back into
    /// the chain: the leading access on a `?.` spine is a member binding with no receiver
    /// (`holder?.Cts.Cancel()` -> `.Cts.Cancel()`), so it becomes an ordinary member access over
    /// the operation. A nested `?.` surviving the splice would change behavior (`await x?.M()`
    /// throws instead of silently skipping), so those are rejected here — before a code action
    /// is ever registered.
    /// </summary>
    private static bool TryBuildHoistedCall(
        SemanticModel semanticModel,
        ConditionalAccessExpressionSyntax conditionalAccess,
        InvocationExpressionSyntax invocation,
        out ExpressionSyntax hoistedCall
    )
    {
        // A nullable-struct receiver (`nullableStruct?.Field.Cancel()`) would not compile after
        // the hoist — outside `?.`, the compiler inserts no `.Value`.
        if (
            semanticModel
                .GetTypeInfo(conditionalAccess.Expression)
                .Type?
                .OriginalDefinition
                .SpecialType == SpecialType.System_Nullable_T
        )
        {
            hoistedCall = null!;
            return false;
        }

        var nameNode = invocation.Expression switch
        {
            MemberBindingExpressionSyntax binding => (SyntaxNode)binding.Name,
            MemberAccessExpressionSyntax member => member.Name,
            _ => null,
        };
        if (nameNode == null)
        {
            hoistedCall = null!;
            return false;
        }

        var renamed = conditionalAccess.WhenNotNull.ReplaceNode(
            nameNode,
            SyntaxFactory.IdentifierName("CancelAsync").WithTriviaFrom(nameNode)
        );
        if (renamed == null)
        {
            hoistedCall = null!;
            return false;
        }

        // The splice only knows receiver-less member bindings; anything else leftmost on the
        // spine (an element binding, a null-forgiving operator) would produce invalid syntax.
        if (
            !TrySpliceOperation(
                renamed,
                conditionalAccess.Expression.WithoutTrivia(),
                out var spliced
            )
        )
        {
            hoistedCall = null!;
            return false;
        }

        if (ContainsNullConditionalAccess(spliced))
        {
            hoistedCall = null!;
            return false;
        }

        hoistedCall = spliced;
        return true;
    }

    private static async Task<Document> HoistToIfNotNullAsync(
        Document document,
        ExpressionStatementSyntax statement,
        ConditionalAccessExpressionSyntax conditionalAccess,
        ExpressionSyntax hoistedCall,
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
            SyntaxFactory.AwaitExpression(hoistedCall.WithoutTrivia())
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

    /// <summary>
    /// Rebuilds the leading receiver-less member binding of a <c>?. </c> spine as an ordinary
    /// member access over the hoisted operation, walking the leftmost chain
    /// (<c>.Cts.Cancel()</c> over operation <c>x</c> becomes <c>x.Cts.Cancel()</c>). Returns
    /// false when the leftmost node is not a member binding — an element binding or a
    /// null-forgiving operator there would produce uncompilable syntax.
    /// </summary>
    private static bool TrySpliceOperation(
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
                if (
                    TrySpliceOperation(parenthesized.Expression, operation, out var innerParen)
                )
                {
                    result = parenthesized.WithExpression(innerParen);
                    return true;
                }
                break;
        }

        result = null!;
        return false;
    }

    private static bool ContainsNullConditionalAccess(SyntaxNode node) =>
        node is ConditionalAccessExpressionSyntax
        || node.DescendantNodes().Any(n => n is ConditionalAccessExpressionSyntax);

    private static async Task<Document> ReplaceAsync(
        Document document,
        InvocationExpressionSyntax invocation,
        MemberAccessExpressionSyntax memberAccess,
        CancellationToken cancellationToken
    )
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null)
            return document;

        // await <receiver>.CancelAsync()
        var cancelAsync = SyntaxFactory.InvocationExpression(
            SyntaxFactory.MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                memberAccess.Expression.WithoutTrivia(),
                SyntaxFactory.IdentifierName("CancelAsync")
            )
        );

        var awaitExpression = SyntaxFactory.AwaitExpression(cancelAsync).WithTriviaFrom(invocation);

        var newRoot = root.ReplaceNode(invocation, awaitExpression);
        return document.WithSyntaxRoot(newRoot);
    }
}
