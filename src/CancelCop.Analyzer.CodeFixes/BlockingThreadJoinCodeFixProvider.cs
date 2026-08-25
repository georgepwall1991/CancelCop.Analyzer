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
/// Code fix provider that would rewrite a blocking
/// <c>thread.Join(...)</c>
/// to <c>await thread.JoinAsync(...)</c>.
/// </summary>
/// <remarks>
/// <para>
/// CC053 is analyzer-only by design: <see cref="System.Threading.Thread"/>
/// declares no TAP <c>JoinAsync</c> on any shipped .NET (verified against the
/// net9/net10 reference packs), so every candidate rewrite fails its
/// speculative rebind and no fix is ever offered. The provider is kept so
/// that if the framework grows a <c>JoinAsync</c>, rewrites light up without
/// an analyzer change.
/// </para>
/// </remarks>
[
    ExportCodeFixProvider(
        LanguageNames.CSharp,
        Name = nameof(BlockingThreadJoinCodeFixProvider)
    ),
    Shared
]
public class BlockingThreadJoinCodeFixProvider : CodeFixProvider
{
    private const string Title = "Use await JoinAsync";

    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(BlockingThreadJoinAnalyzer.DiagnosticId);

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
            BlockingThreadJoinAnalyzer.NoFixProperty,
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
            BlockingThreadJoinAnalyzer.TokenNameProperty,
            out var name
        )
            ? name
            : null;

        var tokenArgumentName = diagnostic.Properties.TryGetValue(
            BlockingThreadJoinAnalyzer.TokenArgumentNameProperty,
            out var argumentName
        )
            ? argumentName
            : null;

        // A whole null-conditional statement
        // (`thread?.Join();`) hoists to
        // `if (thread is not null) { await thread.JoinAsync(); }`.
        InvocationExpressionSyntax? hoistedInvocation = null;
        ExpressionStatementSyntax? hoistStatement = null;
        ConditionalAccessExpressionSyntax? conditionalAccess = null;
        if (
            NullConditionalHoist.TryPrepareHoistedCall(
                semanticModel,
                invocation,
                "Join",
                "JoinAsync",
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

            var threadMethod = semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
            // The hoisted candidate must resolve to the framework's
            // JoinAsync on System.Threading.Thread — a same-named
            // `new` hider on a derived type must not pass just because it shares the
            // receiver type with the original sync call.
            var frameworkThreadType = semanticModel.Compilation.GetTypeByMetadataName(
                "System.Threading.Thread"
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
                    || reboundCandidate.Name != "JoinAsync"
                    || (reboundCandidate.ReturnType.Name != "Task"
                        && reboundCandidate.ReturnType.Name != "ValueTask")
                    || reboundCandidate.ReturnType.ContainingNamespace?.ToDisplayString()
                        != "System.Threading.Tasks"
                    || threadMethod == null
                    || frameworkThreadType == null
                    || !ResolvesOnFrameworkThread(reboundCandidate, frameworkThreadType)
                    // Non-token parameters must mirror the original
                    // Join arguments.
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
            "JoinAsync",
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

    private static bool ResolvesOnFrameworkThread(
        IMethodSymbol bound,
        INamedTypeSymbol frameworkThreadType
    )
    {
        // Walk overrides so a legitimate override of the framework TAP member keeps
        // its framework lineage; a same-named `new` hider has no override chain and
        // must declare on Thread itself to pass.
        var definition = bound.OriginalDefinition;
        while (definition.OverriddenMethod != null)
            definition = definition.OverriddenMethod.OriginalDefinition;

        return SymbolEqualityComparer.Default.Equals(
            definition.ContainingType,
            frameworkThreadType
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
