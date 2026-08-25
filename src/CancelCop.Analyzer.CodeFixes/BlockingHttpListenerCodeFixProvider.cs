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
/// Code fix provider that rewrites a blocking <c>listener.GetContext()</c>
/// to <c>await listener.GetContextAsync()</c>. The framework TAP is
/// tokenless, so the fixer never invents a token argument.
/// </summary>
[
    ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(BlockingHttpListenerCodeFixProvider)),
    Shared
]
public class BlockingHttpListenerCodeFixProvider : CodeFixProvider
{
    private const string Title = "Use await GetContextAsync";

    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(BlockingHttpListenerAnalyzer.DiagnosticId);

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
            BlockingHttpListenerAnalyzer.NoFixProperty,
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

        // A whole null-conditional statement (`listener?.GetContext();`) hoists to
        // `if (listener is not null) { await listener.GetContextAsync(); }` — an in-place
        // rewrite cannot be inserted on the spine of `?.`. GetContextAsync is tokenless, so the
        // hoist never invents a token argument.
        if (
            NullConditionalHoist.TryPrepareHoistedCall(
                semanticModel,
                invocation,
                "GetContext",
                "GetContextAsync",
                out var hoistStatement,
                out var conditionalAccess,
                out var hoistedInvocation
            )
            && NullConditionalHoist.SupportsIsNotNullPattern(semanticModel)
            && !NullConditionalHoist.IsNullableStructOperation(
                semanticModel,
                conditionalAccess.Expression
            )
        )
        {
            // Speculatively rebind: only the framework's awaitable GetContextAsync on
            // System.Net.HttpListener qualifies; hidden unrelated members withhold the fix.
            var contextMethod = semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
            var rebound = semanticModel
                .GetSpeculativeSymbolInfo(
                    invocation.SpanStart,
                    hoistedInvocation,
                    SpeculativeBindingOption.BindAsExpression
                )
                .Symbol as IMethodSymbol;
            if (
                rebound == null
                || rebound.Name != "GetContextAsync"
                || rebound.ReturnType.Name != "Task"
                || rebound.ReturnType.ContainingNamespace?.ToDisplayString()
                    != "System.Threading.Tasks"
                || contextMethod == null
                || !rebound.ContainingType.Equals(contextMethod.OriginalDefinition.ContainingType)
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
            "GetContextAsync",
            tokenName: null
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
