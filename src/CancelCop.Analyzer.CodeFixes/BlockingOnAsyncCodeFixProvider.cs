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
/// Code fix provider that replaces a synchronous block on a task (<c>.Result</c>, <c>.Wait()</c>,
/// <c>.GetAwaiter().GetResult()</c>) with an <c>await</c> of the task.
/// </summary>
[
    ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(BlockingOnAsyncCodeFixProvider)),
    Shared
]
public class BlockingOnAsyncCodeFixProvider : CodeFixProvider
{
    private const string Title = "Await the task";

    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(BlockingOnAsyncAnalyzer.DiagnosticId);

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
            BlockingOnAsyncAnalyzer.NoFixProperty,
            out var noFixReason
        );

        var name = root.FindToken(diagnostic.Location.SourceSpan.Start).Parent;
        if (name == null)
            return;

        // Await insertion is unsafe here (a lock body, an exception filter, …): nothing can be
        // offered, hoisted or in place.
        if (hasNoFix && noFixReason != BlockingOnAsyncAnalyzer.ConditionalAccessReason)
            return;

        // A `?.` spine is detected syntactically from the diagnosed name — the analyzer marks
        // the common shapes, but deep chains like `task?.GetAwaiter().GetResult()` are only
        // discoverable by walking the tree.
        if (
            NullConditionalHoist.TryGetStatement(
                semanticModel,
                name,
                out var statement,
                out var conditionalAccess
            )
            && TryGetTaskExpression(name, conditionalAccess, out var taskExpression)
            && NullConditionalHoist.SupportsIsNotNullPattern(semanticModel)
            && !NullConditionalHoist.IsNullableStructOperation(
                semanticModel,
                conditionalAccess.Expression
            )
        )
        {
            // The task expression arrives attached to the tree; the spliced variant is a new,
            // detached node. The original is kept for the speculative type comparison.
            ExpressionSyntax hoistedTask;
            if (ReferenceEquals(taskExpression, conditionalAccess.Expression))
            {
                // Direct spine (`task?.Wait()`): the awaited task is the operation itself.
                hoistedTask = taskExpression;
            }
            else if (
                NullConditionalHoist.TrySpliceOperation(
                    taskExpression,
                    conditionalAccess.Expression.WithoutTrivia(),
                    out var spliced
                )
                && !NullConditionalHoist.ContainsNullConditionalAccess(spliced)
            )
            {
                hoistedTask = spliced;
            }
            else
            {
                return;
            }

            if (!SpeculativeRebindIsSameTask(semanticModel, name.SpanStart, taskExpression, hoistedTask))
                return;

            context.RegisterCodeFix(
                CodeAction.Create(
                    title: Title,
                    createChangedDocument: c =>
                        NullConditionalHoist.ReplaceStatementWithIfNotNullAsync(
                            context.Document,
                            statement,
                            conditionalAccess,
                            hoistedTask,
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

        if (name.Parent is not MemberAccessExpressionSyntax memberAccess)
            return;
        if (!TryBuildRewrite(memberAccess, out var target, out var replacement))
            return;

        // `host?.Work.Result` is an ordinary member access, but it is the WhenNotNull
        // of `?.`. Replacing it with `(await .Work)` yields `host?(await .Work)`.
        if (target is not null && CancellationTokenHelpers.IsWhenNotNullOfConditionalAccess(target))
            return;

        // `host?.Work.Result` is an ordinary member access, but it is the WhenNotNull
        // of `?.`. Replacing it with `(await .Work)` yields `host?(await .Work)`.
        if (target is not null && CancellationTokenHelpers.IsWhenNotNullOfConditionalAccess(target))
            return;

        context.RegisterCodeFix(
            CodeAction.Create(
                title: Title,
                createChangedDocument: c =>
                    ReplaceAsync(context.Document, target!, replacement!, c),
                equivalenceKey: Title
            ),
            diagnostic
        );
    }

    /// <summary>
    /// Resolves the awaited-from task expression behind the diagnosed blocking member
    /// (<c>.Result</c>, parameterless <c>.Wait()</c>, or <c>.GetAwaiter().GetResult()</c>).
    /// The blocking operation must be the terminal expression of the conditional statement —
    /// `holder?.Work.GetAwaiter().GetResult().Dispose();` does real work after the block, which
    /// the rewrite would drop. A `.Result` or `.Wait()` sitting directly on the spine arrives as
    /// a receiver-less member binding; the awaited task is the spine operation itself.
    /// </summary>
    private static bool TryGetTaskExpression(
        SyntaxNode name,
        ConditionalAccessExpressionSyntax conditionalAccess,
        out ExpressionSyntax task
    )
    {
        var terminal = conditionalAccess.WhenNotNull;

        if (name.Parent is MemberAccessExpressionSyntax access && access.Name == name)
        {
            switch (access.Name.Identifier.Text)
            {
                case "Result" when ReferenceEquals(access, terminal):
                    task = access.Expression;
                    return true;
                case "Wait"
                    when access.Parent is InvocationExpressionSyntax waitInvocation
                        && waitInvocation.ArgumentList.Arguments.Count == 0
                        && ReferenceEquals(waitInvocation, terminal):
                    // Only the parameterless Wait() maps cleanly to `await task`; timeout and
                    // token overloads change semantics and stay without a fix.
                    task = access.Expression;
                    return true;
                case "GetResult"
                    when access.Expression
                            is InvocationExpressionSyntax
                            {
                                ArgumentList.Arguments.Count: 0,
                                Expression: MemberAccessExpressionSyntax getAwaiterAccess
                            }
                        && getAwaiterAccess.Name.Identifier.Text == "GetAwaiter"
                        && access.Parent is InvocationExpressionSyntax getResultInvocation
                        && getResultInvocation.ArgumentList.Arguments.Count == 0
                        && ReferenceEquals(getResultInvocation, terminal):
                    // `<task>.GetAwaiter().GetResult()` awaits <task>.
                    task = getAwaiterAccess.Expression;
                    return true;
                case "GetResult"
                    when access.Expression
                            is InvocationExpressionSyntax
                            {
                                ArgumentList.Arguments.Count: 0,
                                Expression: MemberBindingExpressionSyntax
                                {
                                    Name.Identifier.Text: "GetAwaiter"
                                }
                            }
                        && access.Parent is InvocationExpressionSyntax directGetResultInvocation
                        && directGetResultInvocation.ArgumentList.Arguments.Count == 0
                        && ReferenceEquals(directGetResultInvocation, terminal):
                    // Direct spine (`task?.GetAwaiter().GetResult()`): the awaited task is the
                    // spine operation itself.
                    task = conditionalAccess.Expression;
                    return true;
            }
        }
        else if (name.Parent is MemberBindingExpressionSyntax binding)
        {
            // Direct spine: `.Result` and parameterless `.Wait()` arrive as receiver-less
            // member bindings; the awaited task is the spine operation itself.
            switch (binding.Name.Identifier.Text)
            {
                case "Result"
                    when ReferenceEquals(binding.Parent, terminal):
                    task = conditionalAccess.Expression;
                    return true;
                case "Wait"
                    when binding.Parent is InvocationExpressionSyntax waitInvocation
                        && waitInvocation.ArgumentList.Arguments.Count == 0
                        && ReferenceEquals(waitInvocation, terminal):
                    task = conditionalAccess.Expression;
                    return true;
                case "GetAwaiter"
                    when binding.Parent is MemberAccessExpressionSyntax
                    {
                        Name.Identifier.Text: "GetResult",
                        Parent: InvocationExpressionSyntax getResultInvocation
                    } getResultAccess
                        && getResultInvocation.ArgumentList.Arguments.Count == 0
                        && ReferenceEquals(getResultInvocation, terminal):
                    task = conditionalAccess.Expression;
                    return true;
            }
        }

        task = null!;
        return false;
    }

    /// <summary>
    /// Speculatively binds the spliced task expression and requires it to resolve to the same
    /// task type as the original expression — so a subclass hiding an intermediate member with
    /// something other than the task withholds the rewrite instead of awaiting the wrong thing.
    /// </summary>
    private static bool SpeculativeRebindIsSameTask(
        SemanticModel semanticModel,
        int position,
        ExpressionSyntax originalTask,
        ExpressionSyntax splicedTask
    )
    {
        var originalType = semanticModel.GetTypeInfo(originalTask).Type;
        var reboundType = semanticModel
            .GetSpeculativeTypeInfo(
                position,
                splicedTask,
                SpeculativeBindingOption.BindAsExpression
            )
            .Type;
        return originalType != null
            && reboundType != null
            && SymbolEqualityComparer.Default.Equals(originalType, reboundType);
    }

    private static bool TryBuildRewrite(
        MemberAccessExpressionSyntax memberAccess,
        out SyntaxNode? target,
        out ExpressionSyntax? replacement
    )
    {
        target = null;
        replacement = null;

        switch (memberAccess.Name.Identifier.Text)
        {
            case "Result":
                target = memberAccess;
                replacement = ParenthesizedAwait(memberAccess.Expression);
                return true;

            case "Wait":
                // Only the parameterless Wait() maps cleanly to `await task`; Wait(timeout) /
                // Wait(token) change semantics, so they report without a fix.
                if (
                    memberAccess.Parent is not InvocationExpressionSyntax waitInvocation
                    || waitInvocation.ArgumentList.Arguments.Count != 0
                )
                    return false;
                target = waitInvocation;
                replacement = SyntaxFactory.AwaitExpression(
                    memberAccess.Expression.WithoutTrivia()
                );
                return true;

            case "GetResult":
                // memberAccess is `<X>.GetResult`; its parent is `<X>.GetResult()`, and
                // <X> is `<task>.GetAwaiter()`.
                if (
                    memberAccess.Parent is not InvocationExpressionSyntax getResultInvocation
                    || memberAccess.Expression
                        is not InvocationExpressionSyntax getAwaiterInvocation
                    || getAwaiterInvocation.Expression
                        is not MemberAccessExpressionSyntax getAwaiterAccess
                )
                    return false;
                target = getResultInvocation;
                replacement = ParenthesizedAwait(getAwaiterAccess.Expression);
                return true;

            default:
                return false;
        }
    }

    private static ExpressionSyntax ParenthesizedAwait(ExpressionSyntax awaited) =>
        SyntaxFactory.ParenthesizedExpression(
            SyntaxFactory.AwaitExpression(awaited.WithoutTrivia())
        );

    private static async Task<Document> ReplaceAsync(
        Document document,
        SyntaxNode target,
        ExpressionSyntax replacement,
        CancellationToken cancellationToken
    )
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null)
            return document;

        var newRoot = root.ReplaceNode(target, replacement.WithTriviaFrom(target));
        return document.WithSyntaxRoot(newRoot);
    }
}
