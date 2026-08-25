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
/// Code fix provider that rewrites a blocking
/// <c>listener.AcceptTcpClient()</c> / <c>AcceptSocket()</c>
/// to <c>await listener.Accept*Async([cancellationToken])</c>.
/// </summary>
[
    ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(BlockingTcpListenerCodeFixProvider)),
    Shared
]
public class BlockingTcpListenerCodeFixProvider : CodeFixProvider
{
    private const string Title = "Use await Accept*Async";

    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(BlockingTcpListenerAnalyzer.DiagnosticId);

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
            BlockingTcpListenerAnalyzer.NoFixProperty,
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

        var asyncName = invocation.Expression switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.Text
                + "Async",
            // Direct spine (`listener?.AcceptTcpClient()`): a receiver-less member binding.
            MemberBindingExpressionSyntax memberBinding => memberBinding.Name.Identifier.Text
                + "Async",
            IdentifierNameSyntax identifier => identifier.Identifier.Text + "Async",
            _ => null,
        };
        if (asyncName is null)
            return;

        var tokenName = diagnostic.Properties.TryGetValue(
            BlockingTcpListenerAnalyzer.TokenNameProperty,
            out var name
        )
            ? name
            : null;

        var tokenArgumentName = diagnostic.Properties.TryGetValue(
            BlockingTcpListenerAnalyzer.TokenArgumentNameProperty,
            out var argumentName
        )
            ? argumentName
            : null;

        // A whole null-conditional statement (`listener?.AcceptTcpClient();`) hoists to
        // `if (listener is not null) { await listener.AcceptTcpClientAsync(ct); }` — an in-place
        // rewrite cannot be inserted on the spine of `?.`.
        InvocationExpressionSyntax? hoistedInvocation = null;
        ExpressionStatementSyntax? hoistStatement = null;
        ConditionalAccessExpressionSyntax? conditionalAccess = null;
        if (
            NullConditionalHoist.TryPrepareHoistedCall(
                semanticModel,
                invocation,
                asyncName.Substring(0, asyncName.Length - "Async".Length),
                asyncName,
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
                    hoistedInvocation.ArgumentList.AddArguments(TokenArgument(hoistToken, null))
                );
            }

            // Speculatively rebind: only the framework's awaitable Accept*Async overloads on
            // System.Net.Sockets.TcpListener qualify; hidden unrelated members withhold the fix.
            var acceptMethod = semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
            if (
                !RebindsToTcpListenerAcceptAsync(
                    semanticModel,
                    invocation.SpanStart,
                    hoistedInvocation,
                    acceptMethod
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

        var asyncInvocation2 = CancellationTokenHelpers.BuildRenamedInvocation(
            invocation,
            asyncName,
            tokenName,
            tokenArgumentName
        );
        if (asyncInvocation2 is null)
            return;

        context.RegisterCodeFix(
            CodeAction.Create(
                title: Title,
                createChangedDocument: c =>
                    ReplaceAsync(context.Document, invocation, asyncInvocation2, c),
                equivalenceKey: Title
            ),
            diagnostic
        );
    }

    /// <summary>
    /// Speculatively binds the generated call and requires it to land on the framework's
    /// Task-returning Accept*Async overloads declared by System.Net.Sockets.TcpListener. A
    /// subclass hiding the member with an unrelated one withholds the rewrite instead of
    /// producing non-compiling code.
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

    private static bool RebindsToTcpListenerAcceptAsync(
        SemanticModel semanticModel,
        int position,
        InvocationExpressionSyntax call,
        IMethodSymbol? acceptMethod
    )
    {
        var info = semanticModel.GetSpeculativeSymbolInfo(
            position,
            call,
            SpeculativeBindingOption.BindAsExpression
        );

        if (info.Symbol is not IMethodSymbol resolved)
            return false;

        return resolved.Name == "AcceptTcpClientAsync"
            || resolved.Name == "AcceptSocketAsync";
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
