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
/// Code fix provider that rewrites a blocking <c>client.Send(...)</c>
/// to <c>await client.SendMailAsync(..., [cancellationToken])</c>.
/// Not the event-based <c>SendAsync</c>.
/// </summary>
[
    ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(BlockingSmtpClientCodeFixProvider)),
    Shared
]
public class BlockingSmtpClientCodeFixProvider : CodeFixProvider
{
    private const string Title = "Use await SendMailAsync";

    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(BlockingSmtpClientAnalyzer.DiagnosticId);

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

        // "token-required" means an async counterpart exists but the analyzer's in-place rewrite
        // could not apply it — the statement hoist below can. "await-unsafe" and "self-async"
        // are final: no rewrite is offered.
        var hasNoFix = diagnostic.Properties.TryGetValue(
            BlockingSmtpClientAnalyzer.NoFixProperty,
            out var noFixReason
        );
        if (hasNoFix && noFixReason != "token-required")
            return;

        var invocation = root.FindToken(diagnostic.Location.SourceSpan.Start)
            .Parent?.AncestorsAndSelf()
            .OfType<InvocationExpressionSyntax>()
            .FirstOrDefault();

        if (invocation is null)
            return;

        var semanticModel = await context
            .Document.GetSemanticModelAsync(context.CancellationToken)
            .ConfigureAwait(false);
        if (semanticModel == null)
            return;

        var tokenName = diagnostic.Properties.TryGetValue(
            BlockingSmtpClientAnalyzer.TokenNameProperty,
            out var name
        )
            ? name
            : null;

        var tokenArgumentName = diagnostic.Properties.TryGetValue(
            BlockingSmtpClientAnalyzer.TokenArgumentNameProperty,
            out var argumentName
        )
            ? argumentName
            : null;

        // A whole null-conditional statement (`smtp?.Send(msg);`) hoists to
        // `if (smtp is not null) { await smtp.SendMailAsync(msg, ct); }` — an in-place rewrite
        // cannot be inserted on the spine of `?.`. The spine is detected syntactically.
        if (
            NullConditionalHoist.TryGetStatementReceiver(
                semanticModel,
                invocation,
                "Send",
                out var hoistStatement,
                out var conditionalAccess,
                out var splicedReceiver
            )
            && NullConditionalHoist.SupportsIsNotNullPattern(semanticModel)
            && !NullConditionalHoist.IsNullableStructOperation(
                semanticModel,
                conditionalAccess.Expression
            )
        )
        {
            var sendMethod = semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;

            var hoistToken =
                tokenName
                ?? CancellationTokenHelpers
                    .FindEnclosingCancellationToken(invocation, semanticModel)
                    ?.ExpressionText;

            InvocationExpressionSyntax? BuildCall(string? token) =>
                token == null
                    ? SyntaxFactory.InvocationExpression(
                        SyntaxFactory.MemberAccessExpression(
                            SyntaxKind.SimpleMemberAccessExpression,
                            splicedReceiver,
                            SyntaxFactory.IdentifierName("SendMailAsync")
                        ),
                        invocation.ArgumentList
                    )
                    : BuildRenamedInvocationWithToken(
                        invocation,
                        splicedReceiver,
                        "SendMailAsync",
                        TokenArgument(token, tokenArgumentName)
                    );

            InvocationExpressionSyntax? BuildNamedTokenCall()
            {
                if (hoistToken == null)
                    return null;
                return BuildRenamedInvocationWithToken(
                    invocation,
                    splicedReceiver,
                    "SendMailAsync",
                    TokenArgument(hoistToken, "cancellationToken")
                );
            }

            bool IsValid(InvocationExpressionSyntax call) =>
                SpeculativeRebindIsUsableCounterpart(
                    semanticModel,
                    invocation.SpanStart,
                    call,
                    sendMethod
                );

            // Prefer the cancellable form; fall back to the tokenless one (e.g. .NET Framework's
            // SendMailAsync has no CancellationToken overload). Both are validated by speculative
            // rebinding so hidden non-awaitable members withhold the rewrite.
            InvocationExpressionSyntax? sendCall = null;
            if (hoistToken != null)
            {
                var positional = BuildCall(hoistToken);
                if (IsValid(positional))
                {
                    sendCall = positional;
                }
                else
                {
                    // Reordered named arguments make a positional token unbindable; the
                    // framework's token parameter is named `cancellationToken`.
                    var named = BuildNamedTokenCall();
                    if (named != null && IsValid(named))
                        sendCall = named;
                }
            }

            if (sendCall == null)
            {
                var tokenless = BuildCall(null);
                if (tokenless != null && IsValid(tokenless))
                    sendCall = tokenless;
            }

            if (sendCall == null)
                return;

            context.RegisterCodeFix(
                CodeAction.Create(
                    title: Title,
                    createChangedDocument: c =>
                        NullConditionalHoist.ReplaceStatementWithIfNotNullAsync(
                            context.Document,
                            hoistStatement,
                            conditionalAccess,
                            sendCall,
                            c
                        ),
                    equivalenceKey: Title
                ),
                diagnostic
            );
            return;
        }

        // No spine (or the hoist was withheld): any analyzer NoFix reason is final here.
        if (hasNoFix)
            return;

        var asyncInvocation = CancellationTokenHelpers.BuildRenamedInvocation(
            invocation,
            "SendMailAsync",
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

    /// <summary>
    /// Builds the renamed call over an explicit (spliced) receiver, appending a pre-built token
    /// argument when supplied.
    /// </summary>
    private static InvocationExpressionSyntax BuildRenamedInvocationWithToken(
        InvocationExpressionSyntax invocation,
        ExpressionSyntax splicedReceiver,
        string newName,
        ArgumentSyntax tokenArgument
    )
    {
        var call = SyntaxFactory.InvocationExpression(
            SyntaxFactory.MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                splicedReceiver,
                SyntaxFactory.IdentifierName(newName)
            ),
            invocation.ArgumentList
        );

        return call.WithArgumentList(
            call.ArgumentList.AddArguments(tokenArgument)
        );
    }

    /// <summary>
    /// Speculatively binds the generated call and validates it with the same shape rules as the
    /// analyzer: a Task-returning parameterless-static-free <c>SendMailAsync</c> whose non-token
    /// parameters mirror the original <c>Send</c> signature — so hidden members and unrelated
    /// overloads withhold the rewrite instead of producing non-compiling or behavior-changing
    /// code, while inherited framework counterparts on subclasses still qualify.
    /// </summary>
    private static bool SpeculativeRebindIsUsableCounterpart(
        SemanticModel semanticModel,
        int position,
        InvocationExpressionSyntax call,
        IMethodSymbol? sendMethod
    )
    {
        var rebound = semanticModel
            .GetSpeculativeSymbolInfo(position, call, SpeculativeBindingOption.BindAsExpression)
            .Symbol as IMethodSymbol;
        if (
            rebound == null
            || rebound.IsStatic
            || rebound.Name != "SendMailAsync"
            || rebound.ReturnType.Name != "Task"
            || rebound.ReturnType.ContainingNamespace?.ToDisplayString()
                != "System.Threading.Tasks"
            || sendMethod == null
        )
            return false;

        // Non-token parameters must mirror the original Send signature exactly.
        var tapArgs = rebound
            .Parameters.Where(p => !CancellationTokenHelpers.IsCancellationToken(p.Type))
            .ToArray();
        if (tapArgs.Length != sendMethod.Parameters.Length)
            return false;
        for (var i = 0; i < tapArgs.Length; i++)
        {
            if (tapArgs[i].RefKind != sendMethod.Parameters[i].RefKind)
                return false;
            if (
                !SymbolEqualityComparer.Default.Equals(
                    tapArgs[i].Type,
                    sendMethod.Parameters[i].Type
                )
            )
                return false;
        }

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
