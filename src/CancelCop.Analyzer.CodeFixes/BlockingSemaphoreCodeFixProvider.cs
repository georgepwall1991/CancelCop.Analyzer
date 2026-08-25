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

        var tokenName = diagnostic.Properties.TryGetValue(
            BlockingSemaphoreAnalyzer.TokenNameProperty,
            out var name
        )
            ? name
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
            var waitMethod = semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
            var rebound = semanticModel
                .GetSpeculativeSymbolInfo(
                    invocation.SpanStart,
                    waitAsync,
                    SpeculativeBindingOption.BindAsExpression
                )
                .Symbol as IMethodSymbol;
            if (
                rebound == null
                || rebound.Name != "WaitAsync"
                || rebound.ReturnType.Name != "Task"
                || rebound.ReturnType.ContainingNamespace?.ToDisplayString()
                    != "System.Threading.Tasks"
                || waitMethod == null
                || !rebound.ContainingType.Equals(waitMethod.OriginalDefinition.ContainingType)
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

        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            return;

        // Only withhold when this Wait (or a postfix chain on it) is the WhenNotNull
        // branch of `?.`. An argument nested inside an unrelated `holder?.Consume(...)`
        // is still a legal await.
        if (CancellationTokenHelpers.IsWhenNotNullOfConditionalAccess(invocation))
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
