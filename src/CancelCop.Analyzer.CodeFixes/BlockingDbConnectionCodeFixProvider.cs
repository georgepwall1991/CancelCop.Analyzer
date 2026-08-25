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
/// Code fix provider that rewrites a blocking <c>connection.Open()</c> to
/// <c>await connection.OpenAsync(cancellationToken)</c>, flowing the in-scope
/// token when one is available and the rewritten call still binds.
/// </summary>
[
    ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(BlockingDbConnectionCodeFixProvider)),
    Shared
]
public class BlockingDbConnectionCodeFixProvider : CodeFixProvider
{
    private const string Title = "Use await OpenAsync";

    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(BlockingDbConnectionAnalyzer.DiagnosticId);

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

        if (diagnostic.Properties.ContainsKey(BlockingDbConnectionAnalyzer.NoFixProperty))
            return;

        var invocation = root.FindToken(diagnostic.Location.SourceSpan.Start)
            .Parent?.AncestorsAndSelf()
            .OfType<InvocationExpressionSyntax>()
            .FirstOrDefault();

        if (invocation is null)
            return;

        var tokenName = diagnostic.Properties.TryGetValue(
            BlockingDbConnectionAnalyzer.TokenNameProperty,
            out var name
        )
            ? name
            : null;

        // A whole null-conditional statement (`connection?.Open();`) hoists to
        // `if (connection is not null) { await connection.OpenAsync(ct); }` — an in-place
        // rewrite cannot be inserted on the spine of `?.`.
        if (
            NullConditionalHoist.TryPrepareHoistedCall(
                semanticModel,
                invocation,
                "Open",
                "OpenAsync",
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
            if (tokenName != null)
            {
                hoistedInvocation = hoistedInvocation.WithArgumentList(
                    hoistedInvocation.ArgumentList.AddArguments(
                        SyntaxFactory.Argument(
                            CancellationTokenHelpers.TokenExpression(tokenName)
                        )
                    )
                );
            }

            var openMethod = semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
            var rebound = semanticModel
                .GetSpeculativeSymbolInfo(
                    invocation.SpanStart,
                    hoistedInvocation,
                    SpeculativeBindingOption.BindAsExpression
                )
                .Symbol as IMethodSymbol;
            if (
                rebound == null
                || rebound.Name != "OpenAsync"
                || rebound.ReturnType.Name != "Task"
                || rebound.ReturnType.ContainingNamespace?.ToDisplayString()
                    != "System.Threading.Tasks"
                || openMethod == null
                || !rebound.ContainingType.Equals(openMethod.OriginalDefinition.ContainingType)
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

        var asyncInvocation = CancellationTokenHelpers.BuildRenamedInvocation(
            invocation,
            "OpenAsync",
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

        var newRoot = root.ReplaceNode(
            invocation,
            SyntaxFactory.AwaitExpression(asyncInvocation).WithTriviaFrom(invocation)
        );
        return document.WithSyntaxRoot(newRoot);
    }
}
