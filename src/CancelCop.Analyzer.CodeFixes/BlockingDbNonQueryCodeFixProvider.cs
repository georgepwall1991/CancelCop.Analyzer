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

            // Speculatively rebind: only a Task<int>-returning ExecuteNonQueryAsync whose
            // override chain reaches the framework's DbCommand.ExecuteNonQueryAsync qualifies.
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
            )
                return;

            if (
                rebound.ReturnType is not INamedTypeSymbol namedResult
                || namedResult.TypeArguments.Length != 1
                || namedResult.TypeArguments[0].SpecialType != SpecialType.System_Int32
            )
                return;

            if (!ReachesDbCommandExecuteNonQueryAsync(rebound))
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

    /// <summary>
    /// Walks the override chain and requires it to reach the framework's ExecuteNonQueryAsync on
    /// System.Data.Common.DbCommand — so provider overrides qualify while unrelated `new`
    /// hiders on derived classes do not.
    /// </summary>
    private static bool ReachesDbCommandExecuteNonQueryAsync(IMethodSymbol? method)
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
