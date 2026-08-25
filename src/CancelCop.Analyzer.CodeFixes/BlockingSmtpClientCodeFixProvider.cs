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
            var hoistToken =
                tokenName
                ?? CancellationTokenHelpers
                    .FindEnclosingCancellationToken(invocation, semanticModel)
                    ?.ExpressionText;

            InvocationExpressionSyntax? sendCall = null;
            if (hoistToken != null)
            {
                var candidate = BuildCall(hoistToken);
                if (IsValid(candidate))
                    sendCall = candidate;
            }

            if (sendCall == null)
            {
                var candidate = BuildCall(null);
                if (candidate != null && IsValid(candidate))
                    sendCall = candidate;
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
    /// Speculatively binds the generated call and requires it to resolve to a Task-returning
    /// <c>SendMailAsync</c> declared by the same type as the original <c>Send</c>, whose
    /// parameters mirror the original signature — so hiders and unrelated overloads withhold
    /// the rewrite instead of producing non-compiling or behavior-changing code.
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
        return rebound != null
            && rebound.Name == "SendMailAsync"
            && rebound.ReturnType.Name == "Task"
            && rebound.ReturnType.ContainingNamespace?.ToDisplayString()
                == "System.Threading.Tasks"
            && sendMethod != null
            && rebound.Parameters.Length >= 1
            && rebound.Parameters.Length <= sendMethod.Parameters.Length + 1
            && MatchesSendShape(rebound.Parameters, sendMethod.Parameters)
            && rebound.ContainingType.Equals(sendMethod.OriginalDefinition.ContainingType);
    }

    private static bool MatchesSendShape(
        ImmutableArray<IParameterSymbol> reboundParameters,
        ImmutableArray<IParameterSymbol> sendParameters
    )
    {
        // The trailing appended token is not part of the original shape.
        var comparable = reboundParameters.Length == sendParameters.Length + 1
            ? reboundParameters.RemoveAt(reboundParameters.Length - 1)
            : reboundParameters;
        for (var i = 0; i < comparable.Length; i++)
        {
            if (
                !SymbolEqualityComparer.Default.Equals(
                    comparable[i].Type,
                    sendParameters[i].Type
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
