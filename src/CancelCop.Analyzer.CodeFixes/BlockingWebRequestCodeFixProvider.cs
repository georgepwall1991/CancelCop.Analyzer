using System.Collections.Generic;
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
/// <c>request.GetResponse(...)</c>
/// to <c>await request.GetResponseAsync(...)</c>.
/// </summary>
/// <remarks>
/// The only <c>GetResponseAsync</c> on the framework type is parameterless —
/// no arity accepts a <c>CancellationToken</c>, so the rewrite is always
/// tokenless even when a token is in scope; every candidate is still
/// revalidated by speculative binding.
/// </remarks>
[
    ExportCodeFixProvider(
        LanguageNames.CSharp,
        Name = nameof(BlockingWebRequestCodeFixProvider)
    ),
    Shared
]
public class BlockingWebRequestCodeFixProvider : CodeFixProvider
{
    private const string Title = "Use await GetResponseAsync";

    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(BlockingWebRequestAnalyzer.DiagnosticId);

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
            BlockingWebRequestAnalyzer.NoFixProperty,
            out var noFixReason
        );
        // The analyzer's in-place rewrite could not apply; the statement hoist below can.
        // "await-unsafe" and "self-async" are final: no rewrite is offered.
        if (hasNoFix && noFixReason != "conditional-access")
            return;

        var invocation = root.FindToken(diagnostic.Location.SourceSpan.Start)
            .Parent?.AncestorsAndSelf()
            .OfType<InvocationExpressionSyntax>()
            .FirstOrDefault();

        if (invocation is null)
            return;

        var tokenName = diagnostic.Properties.TryGetValue(
            BlockingWebRequestAnalyzer.TokenNameProperty,
            out var name
        )
            ? name
            : null;

        var tokenArgumentName = diagnostic.Properties.TryGetValue(
            BlockingWebRequestAnalyzer.TokenArgumentNameProperty,
            out var argumentName
        )
            ? argumentName
            : null;

        // A whole null-conditional statement
        // (`request?.GetResponse();`) hoists to
        // `if (request is not null) { await request.GetResponseAsync(); }`.
        InvocationExpressionSyntax? hoistedInvocation = null;
        ExpressionStatementSyntax? hoistStatement = null;
        ConditionalAccessExpressionSyntax? conditionalAccess = null;
        if (
            NullConditionalHoist.TryPrepareHoistedCall(
                semanticModel,
                invocation,
                "GetResponse",
                "GetResponseAsync",
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

            var candidates = new List<InvocationExpressionSyntax>();
            if (hoistToken != null)
            {
                candidates.Add(hoistedInvocation.WithArgumentList(
                    hoistedInvocation.ArgumentList.AddArguments(
                        TokenArgument(hoistToken, tokenArgumentName)
                    )
                ));
            }
            candidates.Add(hoistedInvocation);

            var requestMethod = semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
            // The hoisted candidate must resolve to the framework's
            // GetResponseAsync on System.Net.WebRequest — a same-named
            // `new` hider on a derived type must not pass just because it shares the
            // receiver type with the original sync call.
            var frameworkRequestType = semanticModel.Compilation.GetTypeByMetadataName(
                "System.Net.WebRequest"
            );
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
                    || reboundCandidate.Name != "GetResponseAsync"
                    || (reboundCandidate.ReturnType.Name != "Task"
                        && reboundCandidate.ReturnType.Name != "ValueTask")
                    || reboundCandidate.ReturnType.ContainingNamespace?.ToDisplayString()
                        != "System.Threading.Tasks"
                    || requestMethod == null
                    || frameworkRequestType == null
                    || !ResolvesOnFrameworkRequest(reboundCandidate, frameworkRequestType)
                    // Non-token parameters must mirror the original
                    // GetResponse arguments.
                    || reboundCandidate.Parameters.Count(
                        p => !CancellationTokenHelpers.IsCancellationToken(p.Type)
                    ) != invocation.ArgumentList.Arguments.Count
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
            "GetResponseAsync",
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

    private static bool ResolvesOnFrameworkRequest(
        IMethodSymbol bound,
        INamedTypeSymbol frameworkRequestType
    )
    {
        // Walk overrides so a legitimate override of the framework TAP member keeps
        // its framework lineage; a same-named `new` hider has no override chain and
        // must declare on WebRequest itself to pass.
        var definition = bound.OriginalDefinition;
        while (definition.OverriddenMethod != null)
            definition = definition.OverriddenMethod.OriginalDefinition;

        return SymbolEqualityComparer.Default.Equals(
            definition.ContainingType,
            frameworkRequestType
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
