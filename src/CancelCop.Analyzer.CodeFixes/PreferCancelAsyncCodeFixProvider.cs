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
/// Code fix provider that rewrites a synchronous <c>cts.Cancel()</c> to
/// <c>await cts.CancelAsync()</c>. A null-conditional <c>cts?.Cancel();</c> statement is hoisted
/// to <c>if (cts is not null) { await cts.CancelAsync(); }</c> (see
/// <see cref="NullConditionalHoist"/>); every rewrite speculatively rebinds the renamed call so
/// a hidden, non-awaitable <c>CancelAsync()</c> withholds the fix instead of breaking the build.
/// </summary>
[
    ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(PreferCancelAsyncCodeFixProvider)),
    Shared
]
public class PreferCancelAsyncCodeFixProvider : CodeFixProvider
{
    private const string Title = "Use await CancelAsync()";

    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(PreferCancelAsyncAnalyzer.DiagnosticId);

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
        var isConditionalAccess =
            diagnostic.Properties.TryGetValue(PreferCancelAsyncAnalyzer.NoFixProperty, out var reason)
            && reason == PreferCancelAsyncAnalyzer.ConditionalAccessReason;
        // The diagnostic stands but a plain in-place await insertion would not compile.
        if (!isConditionalAccess && diagnostic.Properties.ContainsKey(PreferCancelAsyncAnalyzer.NoFixProperty))
            return;

        var invocation = root.FindToken(diagnostic.Location.SourceSpan.Start)
            .Parent?.AncestorsAndSelf()
            .OfType<InvocationExpressionSyntax>()
            .FirstOrDefault();
        if (invocation == null)
            return;
        // On a direct `?.` spine the receiver is a member binding (`cts?.Cancel()`); chained
        // calls keep an ordinary member access whose leftmost node is the spliced operation
        // (`holder?.Cts.Cancel()`).
        if (invocation.Expression is not (MemberAccessExpressionSyntax or MemberBindingExpressionSyntax))
            return;
        // `holder?.Cts.Cancel()` cannot take an in-place rewrite (`holder? await…` does not
        // parse), but as a statement it can be hoisted to an `is not null` check.
        if (
            !isConditionalAccess
            && CancellationTokenHelpers.IsWhenNotNullOfConditionalAccess(invocation)
        )
            return;

        var cancelMethod = semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;

        if (isConditionalAccess)
        {
            // The hoisted call: rename in place, splice the operation back into the chain, and
            // verify it still binds to the framework's awaitable CancelAsync().
            if (
                NullConditionalHoist.TryGetStatement(
                    semanticModel,
                    invocation,
                    out var statement,
                    out var conditionalAccess
                )
                && NullConditionalHoist.SupportsIsNotNullPattern(semanticModel)
                && !NullConditionalHoist.IsNullableStructOperation(
                    semanticModel,
                    conditionalAccess.Expression
                )
                && TryBuildHoistedCall(
                    semanticModel,
                    conditionalAccess,
                    invocation,
                    cancelMethod,
                    out var hoistedCall
                )
            )
            {
                context.RegisterCodeFix(
                    CodeAction.Create(
                        title: Title,
                        createChangedDocument: c =>
                            NullConditionalHoist.ReplaceStatementWithIfNotNullAsync(
                                context.Document,
                                statement,
                                conditionalAccess,
                                hoistedCall,
                                c
                            ),
                        equivalenceKey: Title
                    ),
                    diagnostic
                );
            }
            return;
        }

        // Direct call: an ordinary member access renames in place. Guard against a subclass
        // hiding CancelAsync with a non-awaitable member before offering the rewrite.
        if (
            invocation.Expression is MemberAccessExpressionSyntax memberAccess
            && SpeculativeRebindIsFrameworkCancelAsync(
                semanticModel,
                invocation.SpanStart,
                SyntaxFactory.InvocationExpression(
                    SyntaxFactory.MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        memberAccess.Expression.WithoutTrivia(),
                        SyntaxFactory.Token(SyntaxKind.DotToken),
                        SyntaxFactory.IdentifierName("CancelAsync")
                    ),
                    invocation.ArgumentList
                ),
                cancelMethod
            )
        )
        {
            context.RegisterCodeFix(
                CodeAction.Create(
                    title: Title,
                    createChangedDocument: c =>
                        ReplaceAsync(context.Document, invocation, memberAccess, c),
                    equivalenceKey: Title
                ),
                diagnostic
            );
        }
    }

    /// <summary>
    /// Builds the awaited <c>CancelAsync()</c> call for the hoisted rewrite, or returns false when
    /// the shape must stay unfixed. Renames Cancel → CancelAsync in place and splices the
    /// operation back into the chain: the leading access on a `?.` spine is a member binding with
    /// no receiver (`holder?.Cts.Cancel()` → `.Cts.Cancel()`), so it becomes an ordinary member
    /// access over the operation. A nested `?.` surviving the splice would change behavior
    /// (`await x?.M()` throws instead of silently skipping), so those are rejected here — before
    /// a code action is ever registered.
    /// </summary>
    private static bool TryBuildHoistedCall(
        SemanticModel semanticModel,
        ConditionalAccessExpressionSyntax conditionalAccess,
        InvocationExpressionSyntax invocation,
        IMethodSymbol? cancelMethod,
        out ExpressionSyntax hoistedCall
    )
    {
        var nameNode = invocation.Expression switch
        {
            MemberBindingExpressionSyntax binding => (SyntaxNode)binding.Name,
            MemberAccessExpressionSyntax member => member.Name,
            _ => null,
        };
        if (nameNode == null)
            return Withhold(out hoistedCall);

        var renamed = conditionalAccess.WhenNotNull.ReplaceNode(
            nameNode,
            SyntaxFactory.IdentifierName("CancelAsync").WithTriviaFrom(nameNode)
        );
        if (renamed == null)
            return Withhold(out hoistedCall);

        // The splice only knows receiver-less member bindings; anything else leftmost on the
        // spine (an element binding, a null-forgiving operator) would produce invalid syntax.
        if (
            !NullConditionalHoist.TrySpliceOperation(
                renamed,
                conditionalAccess.Expression.WithoutTrivia(),
                out var spliced
            )
            || NullConditionalHoist.ContainsNullConditionalAccess(spliced)
        )
            return Withhold(out hoistedCall);

        if (!SpeculativeRebindIsFrameworkCancelAsync(semanticModel, conditionalAccess.SpanStart, spliced, cancelMethod))
            return Withhold(out hoistedCall);

        hoistedCall = spliced;
        return true;
    }

    /// <summary>
    /// Speculatively binds the rewritten call and requires it to resolve to a parameterless,
    /// Task-returning <c>CancelAsync()</c> declared by the same type as the original
    /// <c>Cancel()</c> — so a subclass hiding <c>CancelAsync</c> with an unrelated member
    /// withholds the rewrite instead of producing non-compiling code.
    /// </summary>
    private static bool SpeculativeRebindIsFrameworkCancelAsync(
        SemanticModel semanticModel,
        int position,
        ExpressionSyntax call,
        IMethodSymbol? cancelMethod
    )
    {
        var rebound = semanticModel
            .GetSpeculativeSymbolInfo(position, call, SpeculativeBindingOption.BindAsExpression)
            .Symbol as IMethodSymbol;
        return rebound != null
            && rebound.Name == "CancelAsync"
            && rebound.Parameters.Length == 0
            && rebound.ReturnType.Name == "Task"
            && rebound.ReturnType.ContainingNamespace?.ToDisplayString()
                == "System.Threading.Tasks"
            && cancelMethod != null
            && rebound.ContainingType.Equals(cancelMethod.OriginalDefinition.ContainingType);
    }

    private static bool Withhold(out ExpressionSyntax hoistedCall)
    {
        hoistedCall = null!;
        return false;
    }

    private static async Task<Document> ReplaceAsync(
        Document document,
        InvocationExpressionSyntax invocation,
        MemberAccessExpressionSyntax memberAccess,
        CancellationToken cancellationToken
    )
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null)
            return document;

        // await <receiver>.CancelAsync()
        var cancelAsync = SyntaxFactory.InvocationExpression(
            SyntaxFactory.MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                memberAccess.Expression.WithoutTrivia(),
                SyntaxFactory.IdentifierName("CancelAsync")
            )
        );

        var awaitExpression = SyntaxFactory.AwaitExpression(cancelAsync).WithTriviaFrom(invocation);

        var newRoot = root.ReplaceNode(invocation, awaitExpression);
        return document.WithSyntaxRoot(newRoot);
    }
}
