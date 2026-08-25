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
/// Code fix provider that rewrites a blocking <c>command.ExecuteReader(…)</c> to
/// <c>await command.ExecuteReaderAsync(…[, cancellationToken])</c>, flowing the
/// in-scope token when one is available and the rewritten call still binds.
/// </summary>
[
    ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(BlockingDbCommandCodeFixProvider)),
    Shared
]
public class BlockingDbCommandCodeFixProvider : CodeFixProvider
{
    private const string Title = "Use await ExecuteReaderAsync";

    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(BlockingDbCommandAnalyzer.DiagnosticId);

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
            BlockingDbCommandAnalyzer.NoFixProperty,
            out var noFixReason
        );
        // The analyzer's in-place rewrite could not apply; the statement hoist below can.
        // "await-unsafe" and "self-async" are final: no rewrite is offered.
        if (hasNoFix && noFixReason != "no-safe-rewrite")
            return;

        var invocation = root.FindToken(diagnostic.Location.SourceSpan.Start)
            .Parent?.AncestorsAndSelf()
            .OfType<InvocationExpressionSyntax>()
            .FirstOrDefault();

        if (invocation is null)
            return;

        var tokenName = diagnostic.Properties.TryGetValue(
            BlockingDbCommandAnalyzer.TokenNameProperty,
            out var name
        )
            ? name
            : null;

        var tokenArgumentName = diagnostic.Properties.TryGetValue(
            BlockingDbCommandAnalyzer.TokenArgumentNameProperty,
            out var argumentName
        )
            ? argumentName
            : null;

        // A whole null-conditional statement (`command?.ExecuteReader();`) hoists to
        // `if (command is not null) { await command.ExecuteReaderAsync(ct); }` — an in-place
        // rewrite cannot be inserted on the spine of `?.`.
        InvocationExpressionSyntax? hoistedInvocation = null;
        ExpressionStatementSyntax? hoistStatement = null;
        ConditionalAccessExpressionSyntax? conditionalAccess = null;
        if (
            NullConditionalHoist.TryPrepareHoistedCall(
                semanticModel,
                invocation,
                "ExecuteReader",
                "ExecuteReaderAsync",
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

            if (hoistToken != null)
            {
                hoistedInvocation = hoistedInvocation.WithArgumentList(
                    hoistedInvocation.ArgumentList.AddArguments(TokenArgument(hoistToken, tokenArgumentName))
                );
            }

            // Speculatively rebind: only framework DbCommand ExecuteReaderAsync overloads
            // qualify; hidden unrelated members withhold the fix.
            var readerMethod = semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
            if (
                !RebindsToDbCommandExecuteReaderAsync(
                    semanticModel,
                    invocation.SpanStart,
                    hoistedInvocation,
                    readerMethod
                )
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

        if (hasNoFix)
            return;

        var asyncInvocation = CancellationTokenHelpers.BuildRenamedInvocation(
            invocation,
            "ExecuteReaderAsync",
            tokenName,
            tokenArgumentName
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
    /// Speculatively binds the generated call and requires it to resolve to a Task-returning
    /// <c>ExecuteReaderAsync</c> declared by System.Data.Common.DbCommand (through overrides or
    /// new-hiders matching the framework shape). Unrelated members withhold the rewrite.
    /// </summary>
    private static ArgumentSyntax TokenArgument(string tokenName, string? tokenArgumentName)
    {
        var tokenArgument = SyntaxFactory.Argument(
            CancellationTokenHelpers.TokenExpression(tokenName)
        );
        if (tokenArgumentName != null)
        {
            tokenArgument = tokenArgument.WithNameColon(
                SyntaxFactory.NameColon(SyntaxFactory.IdentifierName(tokenArgumentName))
            );
        }

        return tokenArgument;
    }

    private static bool RebindsToDbCommandExecuteReaderAsync(
        SemanticModel semanticModel,
        int position,
        InvocationExpressionSyntax call,
        IMethodSymbol? readerMethod
    )
    {
        var rebound = semanticModel
            .GetSpeculativeSymbolInfo(position, call, SpeculativeBindingOption.BindAsExpression)
            .Symbol as IMethodSymbol;

        if (
            rebound == null
            || rebound.Name != "ExecuteReaderAsync"
            || rebound.ReturnType.Name != "Task"
            || rebound.ReturnType.ContainingNamespace?.ToDisplayString()
                != "System.Threading.Tasks"
            || readerMethod == null
            || !rebound.ContainingType.Equals(readerMethod.OriginalDefinition.ContainingType)
        )
            return false;

        return true;
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
