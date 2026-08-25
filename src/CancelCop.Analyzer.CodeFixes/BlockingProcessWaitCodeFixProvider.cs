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
/// Code fix provider that rewrites a blocking <c>process.WaitForExit()</c> to
/// <c>await process.WaitForExitAsync(cancellationToken)</c>, flowing the in-scope token when one is
/// available.
/// </summary>
[
    ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(BlockingProcessWaitCodeFixProvider)),
    Shared
]
public class BlockingProcessWaitCodeFixProvider : CodeFixProvider
{
    private const string Title = "Use await WaitForExitAsync";

    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(BlockingProcessWaitAnalyzer.DiagnosticId);

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

        // The diagnostic stands but the analyzer determined no compilable rewrite exists here.
        if (diagnostic.Properties.ContainsKey(BlockingProcessWaitAnalyzer.NoFixProperty))
            return;

        var invocation = root.FindToken(diagnostic.Location.SourceSpan.Start)
            .Parent?.AncestorsAndSelf()
            .OfType<InvocationExpressionSyntax>()
            .FirstOrDefault();

        if (invocation is null)
            return;

        var tokenName = diagnostic.Properties.TryGetValue(
            BlockingProcessWaitAnalyzer.TokenNameProperty,
            out var name
        )
            ? name
            : null;

        // A whole null-conditional statement (`process?.WaitForExit();`) hoists to
        // `if (process is not null) { await process.WaitForExitAsync(ct); }` — an in-place
        // rewrite cannot be inserted on the spine of `?.`. The spine is detected syntactically.
        if (
            NullConditionalHoist.TryGetStatement(
                semanticModel,
                invocation,
                out var hoistStatement,
                out var conditionalAccess
            )
            && ReferenceEquals(invocation, conditionalAccess.WhenNotNull)
            && NullConditionalHoist.SupportsIsNotNullPattern(semanticModel)
            && TryBuildHoistedInvocation(
                semanticModel,
                conditionalAccess,
                invocation,
                tokenName,
                out var hoistedInvocation
            )
        )
        {
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

        var asyncInvocation = CancellationTokenHelpers.BuildRenamedInvocation(
            invocation,
            "WaitForExitAsync",
            tokenName
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

    /// <summary>
    /// Builds the awaited <c>WaitForExitAsync</c> call for a null-conditional statement.
    /// A chained spine arrives as a receiver-less member binding (`.Process.WaitForExit()`), so
    /// the operation is spliced under it; a direct spine (`process?.WaitForExit()`) awaits the
    /// operation itself. Only the framework's Task-returning `WaitForExitAsync` on
    /// System.Diagnostics.Process qualifies — verified by speculative rebinding, so a subclass
    /// hiding the member withholds the rewrite instead of producing non-compiling code.
    /// </summary>
    private static bool TryBuildHoistedInvocation(
        SemanticModel semanticModel,
        ConditionalAccessExpressionSyntax conditionalAccess,
        InvocationExpressionSyntax invocation,
        string? tokenName,
        out InvocationExpressionSyntax? asyncInvocation
    )
    {
        ExpressionSyntax splicedReceiver;
        SimpleNameSyntax newName;
        switch (invocation.Expression)
        {
            case MemberBindingExpressionSyntax
                {
                    Name.Identifier.Text: "WaitForExit"
                } directBinding:
                // Direct spine (`process?.WaitForExit()`): the awaited receiver is the
                // spine operation itself.
                splicedReceiver = conditionalAccess.Expression;
                newName = SyntaxFactory.IdentifierName("WaitForExitAsync").WithTriviaFrom(
                    directBinding.Name
                );
                break;
            case MemberAccessExpressionSyntax chainedAccess
                when NullConditionalHoist.TrySpliceOperation(
                    chainedAccess.Expression,
                    conditionalAccess.Expression.WithoutTrivia(),
                    out var spliced
                )
                && !NullConditionalHoist.ContainsNullConditionalAccess(spliced):
                // Chained spine (`holder?.Process.WaitForExit()`): splice the operation under
                // the receiver-less chain.
                splicedReceiver = spliced;
                newName = SyntaxFactory.IdentifierName("WaitForExitAsync").WithTriviaFrom(
                    chainedAccess.Name
                );
                break;
            default:
                asyncInvocation = null;
                return false;
        }

        asyncInvocation = SyntaxFactory.InvocationExpression(
            SyntaxFactory.MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                splicedReceiver,
                newName
            ),
            invocation.ArgumentList
        );

        if (tokenName != null)
        {
            asyncInvocation = asyncInvocation.WithArgumentList(
                asyncInvocation.ArgumentList.AddArguments(
                    SyntaxFactory.Argument(
                        CancellationTokenHelpers.TokenExpression(tokenName)
                    )
                )
            );
        }

        // Speculatively bind the exact emitted invocation: a subclass hiding WaitForExitAsync
        // with a non-awaitable member must withhold the rewrite instead of breaking the build.
        var waitMethod = semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
        var rebound = semanticModel
            .GetSpeculativeSymbolInfo(
                invocation.SpanStart,
                asyncInvocation,
                SpeculativeBindingOption.BindAsExpression
            )
            .Symbol as IMethodSymbol;
        if (
            rebound == null
            || rebound.Name != "WaitForExitAsync"
            || rebound.ReturnType.Name != "Task"
            || rebound.ReturnType.ContainingNamespace?.ToDisplayString()
                != "System.Threading.Tasks"
            || waitMethod == null
            || !rebound.ContainingType.Equals(waitMethod.OriginalDefinition.ContainingType)
        )
        {
            asyncInvocation = null;
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

        // Built by the analyzer's helper so the emitted call is exactly the one it bound and
        // approved. It replaces the name on the existing member access rather than rebuilding the
        // expression, which keeps the receiver and any trivia around the dot intact — reconstructing
        // it would silently delete a comment such as `process /* started above */ .WaitForExit()`.
        var newRoot = root.ReplaceNode(
            invocation,
            SyntaxFactory.AwaitExpression(asyncInvocation).WithTriviaFrom(invocation)
        );
        return document.WithSyntaxRoot(newRoot);
    }
}
