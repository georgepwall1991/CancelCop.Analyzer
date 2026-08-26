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
/// Code fix provider that rewrites a blocking <c>Socket</c> call with a compiling
/// TAP counterpart (<c>socket.Receive(buffer)</c> to
/// <c>await socket.ReceiveAsync(buffer, ct)</c>, and likewise for the other
/// members whose async form accepts the original arguments).
/// </summary>
/// <remarks>
/// Every candidate is validated by speculative binding before it is offered: the
/// rewritten call must resolve to an instance <c>&lt;name&gt;Async</c> on
/// <see cref="System.Net.Sockets.Socket"/> (override lineage walked) returning
/// <see cref="System.Threading.Tasks.Task"/>, with the original non-token arguments
/// preserved. Arities without such a counterpart (flag-bearing forms, endpoint
/// connects) are reported by the analyzer and stay unfixed here.
/// </remarks>
[ExportCodeFixProvider(
    LanguageNames.CSharp,
    Name = nameof(BlockingSocketIoCodeFixProvider)
), Shared]
public class BlockingSocketIoCodeFixProvider : CodeFixProvider
{
    private const string Title = "Use the async socket method";

    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(BlockingSocketIoAnalyzer.DiagnosticId);

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
            BlockingSocketIoAnalyzer.NoFixProperty,
            out var noFixReason
        );
        // "conditional-access" means only a statement hoist can apply; every other
        // reason ("await-unsafe") is final.
        if (hasNoFix && noFixReason != "conditional-access")
            return;

        var invocation = root.FindToken(diagnostic.Location.SourceSpan.Start)
            .Parent?.AncestorsAndSelf()
            .OfType<InvocationExpressionSyntax>()
            .FirstOrDefault();

        if (invocation is null)
            return;

        // An unqualified inherited call (`Receive(buffer)` in a Socket subclass)
        // reaches the fixer as a plain identifier — the renamed invocation stays
        // bare and resolves through the same lineage walk.
        var memberName = invocation.Expression switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.Text,
            MemberBindingExpressionSyntax memberBinding => memberBinding.Name.Identifier.Text,
            IdentifierNameSyntax identifier => identifier.Identifier.Text,
            _ => null,
        };
        if (memberName is null)
            return;

        var tokenName = diagnostic.Properties.TryGetValue(
            BlockingSocketIoAnalyzer.TokenNameProperty,
            out var name
        )
            ? name
            : null;

        // A whole null-conditional statement (`socket?.Receive(buffer);`) hoists to an
        // is-not-null guard — an in-place rewrite cannot sit on the spine of `?.`.
        if (
            NullConditionalHoist.TryPrepareHoistedCall(
                semanticModel,
                invocation,
                memberName,
                memberName + "Async",
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
            var candidates = new List<InvocationExpressionSyntax>();
            if (tokenName != null)
            {
                candidates.Add(hoistedInvocation.WithArgumentList(
                    hoistedInvocation.ArgumentList.AddArguments(
                        SyntaxFactory.Argument(
                            CancellationTokenHelpers.TokenExpression(tokenName))
                    )
                ));
            }
            candidates.Add(hoistedInvocation);

            var socketMethod = semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
            InvocationExpressionSyntax? boundCall = null;
            foreach (var candidate in candidates)
            {
                if (ResolvesToUsableCounterpart(
                        semanticModel,
                        invocation,
                        candidate,
                        socketMethod,
                        memberName + "Async"
                    ))
                {
                    boundCall = candidate;
                    break;
                }
            }

            if (boundCall is null)
                return;

            context.RegisterCodeFix(
                CodeAction.Create(
                    title: Title,
                    createChangedDocument: c =>
                        NullConditionalHoist.ReplaceStatementWithIfNotNullAsync(
                            context.Document,
                            hoistStatement,
                            conditionalAccess,
                            boundCall,
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

        // In-place rewrite: rename to the async member, appending the in-scope token
        // when one was recorded, then prove the rewritten call still binds.
        var asyncInvocation = CancellationTokenHelpers.BuildRenamedInvocation(
            invocation,
            memberName + "Async",
            tokenName,
            null
        );
        if (
            asyncInvocation is null
            || !ResolvesToUsableCounterpart(
                semanticModel,
                invocation,
                asyncInvocation,
                semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol,
                memberName + "Async"
            )
        )
            return;

        context.RegisterCodeFix(
            CodeAction.Create(
                title: Title,
                createChangedDocument: c =>
                    ReplaceWithAwaitAsync(
                        context.Document,
                        invocation,
                        asyncInvocation,
                        c
                    ),
                equivalenceKey: Title
            ),
            diagnostic
        );
    }

    private static bool ResolvesToUsableCounterpart(
        SemanticModel semanticModel,
        InvocationExpressionSyntax original,
        InvocationExpressionSyntax candidate,
        IMethodSymbol? originalMethod,
        string expectedName
    )
    {
        var bound = semanticModel
            .GetSpeculativeSymbolInfo(
                original.SpanStart,
                candidate,
                SpeculativeBindingOption.BindAsExpression
            )
            .Symbol as IMethodSymbol;
        if (
            bound == null
            || bound.IsStatic
            || bound.Name != expectedName
            || (bound.ReturnType.Name != "Task" && bound.ReturnType.Name != "ValueTask")
            || bound.ReturnType.ContainingNamespace?.ToDisplayString()
                != "System.Threading.Tasks"
            || originalMethod == null
        )
            return false;

        // Walk overrides so a legitimate Socket-subclass override keeps framework
        // lineage; an unrelated same-named member must declare on Socket itself.
        var definition = bound.OriginalDefinition;
        while (definition.OverriddenMethod != null)
            definition = definition.OverriddenMethod.OriginalDefinition;

        if (
            !SymbolEqualityComparer.Default.Equals(
                definition.ContainingType,
                originalMethod.OriginalDefinition.ContainingType
            )
        )
            return false;

        // Non-token parameters must mirror the original arguments.
        return bound.Parameters.Count(p =>
            !CancellationTokenHelpers.IsCancellationToken(p.Type)
        ) == original.ArgumentList.Arguments.Count;
    }

    private static async Task<Document> ReplaceWithAwaitAsync(
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
