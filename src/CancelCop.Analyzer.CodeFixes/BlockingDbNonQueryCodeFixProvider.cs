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
/// Code fix provider that rewrites a blocking <c>command.ExecuteNonQuery()</c>
/// to <c>await command.ExecuteNonQueryAsync([cancellationToken])</c>.
/// </summary>
[
    ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(BlockingDbNonQueryCodeFixProvider)),
    Shared
]
public class BlockingDbNonQueryCodeFixProvider : CodeFixProvider
{
    private const string Title = "Use await ExecuteNonQueryAsync";

    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(BlockingDbNonQueryAnalyzer.DiagnosticId);

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

        if (diagnostic.Properties.ContainsKey(BlockingDbNonQueryAnalyzer.NoFixProperty))
            return;

        var invocation = root.FindToken(diagnostic.Location.SourceSpan.Start)
            .Parent?.AncestorsAndSelf()
            .OfType<InvocationExpressionSyntax>()
            .FirstOrDefault();

        if (invocation is null)
            return;

        var tokenName = diagnostic.Properties.TryGetValue(
            BlockingDbNonQueryAnalyzer.TokenNameProperty,
            out var name
        )
            ? name
            : null;

        // A whole null-conditional statement (`command?.ExecuteNonQuery();`) hoists to
        // `if (command is not null) { await command.ExecuteNonQueryAsync(ct); }` — an in-place
        // rewrite cannot be inserted on the spine of `?.`.
        InvocationExpressionSyntax? hoistedInvocation = null;
        ExpressionStatementSyntax? hoistStatement = null;
        ConditionalAccessExpressionSyntax? conditionalAccess = null;
        if (
            NullConditionalHoist.TryPrepareHoistedCall(
                semanticModel,
                invocation,
                "ExecuteNonQuery",
                "ExecuteNonQueryAsync",
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
            var hoistToken =
                tokenName
                ?? CancellationTokenHelpers
                    .FindEnclosingCancellationToken(invocation, semanticModel)
                    ?.ExpressionText;

            if (hoistToken != null)
            {
                hoistedInvocation = hoistedInvocation.WithArgumentList(
                    hoistedInvocation.ArgumentList.AddArguments(
                        SyntaxFactory.Argument(
                            CancellationTokenHelpers.TokenExpression(hoistToken)
                        )
                    )
                );
            }

            // Speculatively rebind: only framework DbCommand ExecuteNonQueryAsync overloads
            // qualify; hidden unrelated members withhold the fix.
            var nonQueryMethod = semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
            var rebound = semanticModel
                .GetSpeculativeSymbolInfo(
                    invocation.SpanStart,
                    hoistedInvocation,
                    SpeculativeBindingOption.BindAsExpression
                )
                .Symbol as IMethodSymbol;
            if (
                rebound == null
                || rebound.IsStatic
                || rebound.Name != "ExecuteNonQueryAsync"
                || rebound.ReturnType.Name != "Task"
                || rebound.ReturnType.ContainingNamespace?.ToDisplayString()
                    != "System.Threading.Tasks"
                || nonQueryMethod == null
                || !rebound.ContainingType.Equals(nonQueryMethod.OriginalDefinition.ContainingType)
            )
                return;

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

        var asyncInvocation = CancellationTokenHelpers.BuildRenamedInvocation(
            invocation,
            "ExecuteNonQueryAsync",
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
