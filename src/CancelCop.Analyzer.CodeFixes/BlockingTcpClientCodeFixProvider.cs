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
/// Code fix provider that rewrites a blocking <c>client.Connect(...)</c>
/// to <c>await client.ConnectAsync(..., [cancellationToken])</c>.
/// </summary>
[
    ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(BlockingTcpClientCodeFixProvider)),
    Shared
]
public class BlockingTcpClientCodeFixProvider : CodeFixProvider
{
    private const string Title = "Use await ConnectAsync";

    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(BlockingTcpClientAnalyzer.DiagnosticId);

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
            BlockingTcpClientAnalyzer.NoFixProperty,
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
            BlockingTcpClientAnalyzer.TokenNameProperty,
            out var name
        )
            ? name
            : null;

        var tokenArgumentName = diagnostic.Properties.TryGetValue(
            BlockingTcpClientAnalyzer.TokenArgumentNameProperty,
            out var argumentName
        )
            ? argumentName
            : null;

        // A whole null-conditional statement (`client?.Connect(host);`) hoists to
        // `if (client is not null) { await client.ConnectAsync(host, ct); }` — an in-place
        // rewrite cannot be inserted on the spine of `?.`.
        if (
            NullConditionalHoist.TryPrepareHoistedCall(
                semanticModel,
                invocation,
                "Connect",
                "ConnectAsync",
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
            // The analyzer drops the in-scope token when its in-place rewrite could not apply;
            // the hoist can, so re-resolve the token here.
            var hoistToken =
                tokenName
                ?? CancellationTokenHelpers
                    .FindEnclosingCancellationToken(invocation, semanticModel)
                    ?.ExpressionText;

            // Prefer the cancellable form; older targets exposing only a tokenless ConnectAsync
            // fall back to it. Both are validated by speculative rebinding.
            InvocationExpressionSyntax? tokenCall = null;
            if (hoistToken != null)
            {
                tokenCall = hoistedInvocation.WithArgumentList(
                    hoistedInvocation.ArgumentList.AddArguments(TokenArgument(hoistToken, null))
                );
            }

            // Speculatively rebind: only the framework's awaitable ConnectAsync overloads on
            // System.Net.Sockets.TcpClient qualify; hidden unrelated members withhold the fix.
            var connectMethod = semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
            var boundCall =
                tokenCall != null
                && RebindsToTcpClientConnectAsync(
                    semanticModel,
                    invocation.SpanStart,
                    tokenCall,
                    connectMethod
                )
                    ? tokenCall
                    : RebindsToTcpClientConnectAsync(
                            semanticModel,
                            invocation.SpanStart,
                            hoistedInvocation,
                            connectMethod
                        )
                        ? hoistedInvocation
                        : null;

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
            "ConnectAsync",
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
    /// Speculatively binds the generated call and requires it to land on the framework's
    /// Task/ValueTask-returning ConnectAsync overloads declared by System.Net.Sockets.TcpClient
    /// with the same parameter count as the original Connect arguments. A subclass hiding
    /// ConnectAsync with an unrelated member withholds the rewrite instead of producing
    /// non-compiling code.
    /// </summary>
    private static bool RebindsToTcpClientConnectAsync(
        SemanticModel semanticModel,
        int position,
        InvocationExpressionSyntax call,
        IMethodSymbol? connectMethod
    )
    {
        var info = semanticModel.GetSpeculativeSymbolInfo(
            position,
            call,
            SpeculativeBindingOption.BindAsExpression
        );

        // Candidates do not prove the call binds: an invalid argument (e.g. a kept `hostname:`
        // name) must withhold the rewrite, so only an exactly-resolved symbol qualifies.
        if (info.Symbol is not IMethodSymbol resolved)
            return false;

        // ConnectAsync overloads may return Task or ValueTask.
        return resolved.Name == "ConnectAsync"
            && !resolved.IsStatic
            && (resolved.ReturnType.Name == "Task" || resolved.ReturnType.Name == "ValueTask")
            && resolved.ReturnType.ContainingNamespace?.ToDisplayString()
                == "System.Threading.Tasks"
            && resolved.ContainingType?.ToDisplayString() == "System.Net.Sockets.TcpClient"
            && resolved.Parameters.Length == call.ArgumentList.Arguments.Count
            && connectMethod != null
            && resolved.Parameters.Take(connectMethod.Parameters.Length)
                .Select((p, i) => (p, i))
                .All(x =>
                    x.p.Type.Equals(connectMethod.Parameters[x.i].Type)
                    || string.Equals(
                        x.p.Name,
                        connectMethod.Parameters[x.i].Name,
                        StringComparison.Ordinal
                    ));
    }

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
