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
/// Code fix provider that rewrites a blocking <c>command.ExecuteScalar()</c>
/// to <c>await command.ExecuteScalarAsync([cancellationToken])</c>.
/// </summary>
[
    ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(BlockingDbScalarCodeFixProvider)),
    Shared
]
public class BlockingDbScalarCodeFixProvider : CodeFixProvider
{
    private const string Title = "Use await ExecuteScalarAsync";

    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(BlockingDbScalarAnalyzer.DiagnosticId);

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

        var hasNoFix = diagnostic.Properties.TryGetValue(
            BlockingDbScalarAnalyzer.NoFixProperty,
            out var noFixReason
        );
        // The analyzer's in-place rewrite could not apply; the statement hoist below can.
        // "await-unsafe" and "self-async" are final: no rewrite is offered.
        if (hasNoFix && noFixReason != "token-required")
            return;

        var invocation = root.FindToken(diagnostic.Location.SourceSpan.Start)
            .Parent?.AncestorsAndSelf()
            .OfType<InvocationExpressionSyntax>()
            .FirstOrDefault();

        if (invocation is null)
            return;

        var tokenName = diagnostic.Properties.TryGetValue(
            BlockingDbScalarAnalyzer.TokenNameProperty,
            out var name
        )
            ? name
            : null;

        // A whole null-conditional statement (`command?.ExecuteScalar();`) hoists to
        // `if (command is not null) { await command.ExecuteScalarAsync(ct); }`.
        InvocationExpressionSyntax? hoistedInvocation = null;
        ExpressionStatementSyntax? hoistStatement = null;
        ConditionalAccessExpressionSyntax? conditionalAccess = null;
        if (
            NullConditionalHoist.TryPrepareHoistedCall(
                semanticModel,
                invocation,
                "ExecuteScalar",
                "ExecuteScalarAsync",
                out hoistStatement,
                out conditionalAccess,
                out hoistedInvocation
            )
            && NullConditionalHoist.SupportsIsNotNullPattern(semanticModel)
            && !NullConditionalHoist.IsNullableStructOperation(
                semanticModel,
                conditionalAccess.Expression
            )
        )
        {
            // The analyzer drops the in-scope token when its in-place rewrite could not apply;
            // the hoist can, so re-resolve the token here.
            var hoistToken =
                tokenName
                ?? CancellationTokenHelpers
                    .FindEnclosingCancellationToken(invocation, semanticModel)
                    ?.ExpressionText;

            // Candidate forms in priority order: cancellable (in-scope token re-resolved),
            // then tokenless for targets whose ExecuteScalarAsync lacks a ct overload.
            var candidates = new List<InvocationExpressionSyntax>();
            if (hoistToken != null)
            {
                candidates.Add(hoistedInvocation.WithArgumentList(
                    hoistedInvocation.ArgumentList.AddArguments(
                        SyntaxFactory.Argument(
                            CancellationTokenHelpers.TokenExpression(hoistToken)
                        )
                    )
                ));
            }
            candidates.Add(hoistedInvocation);

            InvocationExpressionSyntax? boundCall = null;
            foreach (var candidate in candidates)
            {
                var reboundCandidate = semanticModel
                    .GetSpeculativeSymbolInfo(
                        invocation.SpanStart,
                        candidate,
                        SpeculativeBindingOption.BindAsExpression
                    )
                    .Symbol as IMethodSymbol;
                if (
                    reboundCandidate == null
                    || reboundCandidate.IsStatic
                    || reboundCandidate.Name != "ExecuteScalarAsync"
                    || reboundCandidate.ReturnType.Name != "Task"
                    || reboundCandidate.ReturnType.ContainingNamespace?.ToDisplayString()
                        != "System.Threading.Tasks"
                    || !ReachesDbCommandExecuteScalarAsync(reboundCandidate)
                )
                    continue;

                boundCall = candidate;
                break;
            }

            if (boundCall is null)
                return;

            hoistedInvocation = boundCall;

            context.RegisterCodeFix(
                CodeAction.Create(
                    title: Title,
                    createChangedDocument: c =>
                        NullConditionalHoist.ReplaceStatementWithIfNotNullAsync(
                            context.Document,
                            hoistStatement,
                            conditionalAccess,
                            hoistedInvocation,
                            c
                        ),
                    equivalenceKey: Title
                ),
                diagnostic
            );
            return;
        }

        if (hasNoFix)
            return;

        var asyncInvocation = CancellationTokenHelpers.BuildRenamedInvocation(
            invocation,
            "ExecuteScalarAsync",
            tokenName
        );
        if (asyncInvocation is null)
            return;

        context.RegisterCodeFix(
            CodeAction.Create(
                title: Title,
                createChangedDocument: c =>
                    ReplaceAsync(context.Document, invocation, asyncInvocation, c),
                equivalenceKey: Title
            ),
            diagnostic
        );
    }

    /// <summary>
    /// Walks the override chain and requires it to reach the framework's ExecuteScalarAsync on
    /// System.Data.Common.DbCommand — provider overrides qualify while unrelated `new` hiders
    /// on derived classes do not.
    /// </summary>
    private static bool ReachesDbCommandExecuteScalarAsync(IMethodSymbol? method)
    {
        for (
            var current = method?.OriginalDefinition;
            current != null;
            current = current.OverriddenMethod
        )
        {
            if (current.ContainingType?.ToDisplayString() == "System.Data.Common.DbCommand")
                return true;
        }

        return false;
    }

    private static async Task<Document> ReplaceAsync(
        Document document,
        InvocationExpressionSyntax invocation,
        InvocationExpressionSyntax asyncInvocation,
        CancellationToken cancellationToken
    )
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null)
            return document;

        ExpressionSyntax replacement = SyntaxFactory.AwaitExpression(asyncInvocation);
        if (CancellationTokenHelpers.AwaitNeedsParentheses(invocation))
            replacement = SyntaxFactory.ParenthesizedExpression(replacement);

        var newRoot = root.ReplaceNode(invocation, replacement.WithTriviaFrom(invocation));
        return document.WithSyntaxRoot(newRoot);
    }
}
