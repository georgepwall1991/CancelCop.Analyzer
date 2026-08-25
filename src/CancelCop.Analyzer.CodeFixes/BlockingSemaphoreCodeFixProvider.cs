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
/// Code fix provider that rewrites a synchronous <c>gate.Wait()</c> to
/// <c>await gate.WaitAsync(token)</c>, flowing the in-scope token when one is available.
/// </summary>
[
    ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(BlockingSemaphoreCodeFixProvider)),
    Shared
]
public class BlockingSemaphoreCodeFixProvider : CodeFixProvider
{
    private const string Title = "Use await WaitAsync()";

    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(BlockingSemaphoreAnalyzer.DiagnosticId);

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
        // The diagnostic stands but the analyzer determined that inserting an await here would not
        // compile, so no rewrite is offered.
        if (diagnostic.Properties.ContainsKey(BlockingSemaphoreAnalyzer.NoFixProperty))
            return;
        var invocation = root.FindToken(diagnostic.Location.SourceSpan.Start)
            .Parent?.AncestorsAndSelf()
            .OfType<InvocationExpressionSyntax>()
            .FirstOrDefault();
        if (invocation == null)
            return;

        var semanticModel = await context
            .Document.GetSemanticModelAsync(context.CancellationToken)
            .ConfigureAwait(false);
        if (semanticModel == null)
            return;

        var name = root.FindToken(diagnostic.Location.SourceSpan.Start).Parent;
        if (name == null)
            return;

        var waitMethod = semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;

        var tokenName = diagnostic.Properties.TryGetValue(
            BlockingSemaphoreAnalyzer.TokenNameProperty,
            out var tokenNameProperty
        )
            ? tokenNameProperty
            : null;

        // A whole null-conditional statement (`gate?.Wait();`) hoists to
        // `if (gate is not null) { await gate.WaitAsync(…); }` — an in-place await cannot be
        // inserted on the spine of `?.`.
        if (
            NullConditionalHoist.TryGetStatement(
                semanticModel,
                invocation,
                out var statement,
                out var conditionalAccess
            )
            && ReferenceEquals(invocation, conditionalAccess.WhenNotNull)
            && NullConditionalHoist.SupportsIsNotNullPattern(semanticModel)
            && !NullConditionalHoist.IsNullableStructOperation(
                semanticModel,
                conditionalAccess.Expression
            )
        )
        {
            // Resolve the awaited receiver. A chained spine arrives as a receiver-less member
            // binding (`.Gate.Wait()`), so the operation is spliced under it; a direct spine is
            // the bare `.Wait()` binding, whose receiver is the operation itself.
            ExpressionSyntax splicedReceiver;
            if (
                invocation.Expression is MemberAccessExpressionSyntax chainedAccess
                && NullConditionalHoist.TrySpliceOperation(
                    chainedAccess.Expression,
                    conditionalAccess.Expression.WithoutTrivia(),
                    out var spliced
                )
            )
            {
                splicedReceiver = spliced;
            }
            else if (
                invocation.Expression
                    is MemberBindingExpressionSyntax
                    {
                        Name.Identifier.Text: "Wait"
                    }
            )
            {
                splicedReceiver = conditionalAccess.Expression;
            }
            else
            {
                return;
            }

            var waitAsync = SyntaxFactory.InvocationExpression(
                SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    splicedReceiver,
                    SyntaxFactory.IdentifierName("WaitAsync")
                ),
                BuildArgumentList(invocation.ArgumentList, tokenName)
            );

            // Speculatively rebind the generated call: a SemaphoreSlim subclass may hide
            // WaitAsync with an unrelated member, and the rewrite must not invoke it.
            if (
                !SpeculativeRebindIsFrameworkWaitAsync(
                    semanticModel,
                    invocation.SpanStart,
                    waitAsync,
                    waitMethod
                )
            )
                return;


            context.RegisterCodeFix(
                CodeAction.Create(
                    title: Title,
                    createChangedDocument: c =>
                        NullConditionalHoist.ReplaceStatementWithIfNotNullAsync(
                            context.Document,
                            statement,
                            conditionalAccess,
                            waitAsync,
                            c
                        ),
                    equivalenceKey: Title
                ),
                diagnostic
            );
            return;
        }

        if (name.Parent is not MemberAccessExpressionSyntax memberAccess)
            return;


        // Only withhold when this Wait (or a postfix chain on it) is the WhenNotNull
        // branch of `?.`. An argument nested inside an unrelated `holder?.Consume(...)`
        // is still a legal await.
        if (CancellationTokenHelpers.IsWhenNotNullOfConditionalAccess(invocation))
            return;

        // The same hidden-member hazard applies to the in-place rewrite: only register when
        // the renamed call speculatively rebinds to the framework's awaitable WaitAsync().
        var candidateCall = SyntaxFactory.InvocationExpression(
            SyntaxFactory.MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                memberAccess.Expression.WithoutTrivia(),
                SyntaxFactory.IdentifierName("WaitAsync")
            ),
            BuildArgumentList(invocation.ArgumentList, tokenName)
        );
        if (
            !SpeculativeRebindIsFrameworkWaitAsync(
                semanticModel,
                invocation.SpanStart,
                candidateCall,
                waitMethod
            )
        )
            return;

        context.RegisterCodeFix(
            CodeAction.Create(
                title: Title,
                createChangedDocument: c =>
                    ReplaceAsync(context.Document, invocation, memberAccess, tokenName, c),
                equivalenceKey: Title
            ),
            diagnostic
        );
    }

    /// <summary>
    /// Speculatively binds the rewritten call and requires it to resolve to a
    /// Task-returning <c>WaitAsync</c> declared by the same type as the original
    /// <c>Wait()</c> — so a subclass hiding <c>WaitAsync</c> with an unrelated member
    /// withholds the rewrite instead of producing non-compiling code.
    /// </summary>
    private static bool SpeculativeRebindIsFrameworkWaitAsync(
        SemanticModel semanticModel,
        int position,
        InvocationExpressionSyntax call,
        IMethodSymbol? waitMethod
    )
    {
        var rebound = semanticModel
            .GetSpeculativeSymbolInfo(position, call, SpeculativeBindingOption.BindAsExpression)
            .Symbol as IMethodSymbol;
        return rebound != null
            && rebound.Name == "WaitAsync"
            && rebound.ReturnType.Name == "Task"
            && rebound.ReturnType.ContainingNamespace?.ToDisplayString()
                == "System.Threading.Tasks"
            && waitMethod != null
            && rebound.ContainingType.Equals(waitMethod.OriginalDefinition.ContainingType);
    }

    /// <summary>
    /// Carries the original Wait arguments (timeout and/or token) through to WaitAsync; only
    /// when Wait() was parameterless is the in-scope token (if any) added.
    /// </summary>
    private static ArgumentListSyntax BuildArgumentList(
        ArgumentListSyntax original,
        string? tokenName
    )
    {
        if (original.Arguments.Count > 0)
            return original.WithoutTrivia();
        if (tokenName != null)
            return SyntaxFactory.ArgumentList(
                SyntaxFactory.SingletonSeparatedList(
                    SyntaxFactory.Argument(CancellationTokenHelpers.TokenExpression(tokenName))
                )
            );
        return SyntaxFactory.ArgumentList();
    }

    private static async Task<Document> ReplaceAsync(
        Document document,
        InvocationExpressionSyntax invocation,
        MemberAccessExpressionSyntax memberAccess,
        string? tokenName,
        CancellationToken cancellationToken
    )
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null)
            return document;

        var waitAsync = SyntaxFactory.InvocationExpression(
            SyntaxFactory.MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                memberAccess.Expression.WithoutTrivia(),
                SyntaxFactory.IdentifierName("WaitAsync")
            ),
            BuildArgumentList(invocation.ArgumentList, tokenName)
        );

        ExpressionSyntax replacement = SyntaxFactory.AwaitExpression(waitAsync);

        if (CancellationTokenHelpers.AwaitNeedsParentheses(invocation))
            replacement = SyntaxFactory.ParenthesizedExpression(replacement);

        var newRoot = root.ReplaceNode(invocation, replacement.WithTriviaFrom(invocation));
        return document.WithSyntaxRoot(newRoot);
    }
}
