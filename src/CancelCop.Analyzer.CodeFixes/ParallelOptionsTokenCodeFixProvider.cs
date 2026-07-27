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
using Microsoft.CodeAnalysis.Formatting;

namespace CancelCop.Analyzer;

/// <summary>
/// Code fix provider that adds <c>CancellationToken = token</c> to a <c>ParallelOptions</c> object
/// initializer, creating the initializer when there is none.
/// </summary>
[
    ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ParallelOptionsTokenCodeFixProvider)),
    Shared
]
public class ParallelOptionsTokenCodeFixProvider : CodeFixProvider
{
    private const string Title = "Set CancellationToken on ParallelOptions";

    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(ParallelOptionsTokenAnalyzer.DiagnosticId);

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

        var creation = root.FindToken(diagnostic.Location.SourceSpan.Start)
            .Parent?.AncestorsAndSelf()
            .OfType<BaseObjectCreationExpressionSyntax>()
            .FirstOrDefault();
        if (
            creation == null
            || !diagnostic.Properties.TryGetValue(
                ParallelOptionsTokenAnalyzer.TokenNameProperty,
                out var tokenName
            )
            || tokenName is null
        )
            return;

        context.RegisterCodeFix(
            CodeAction.Create(
                title: Title,
                createChangedDocument: c => AddTokenAsync(context.Document, creation, tokenName, c),
                equivalenceKey: Title
            ),
            diagnostic
        );
    }

    private static async Task<Document> AddTokenAsync(
        Document document,
        BaseObjectCreationExpressionSyntax creation,
        string tokenName,
        CancellationToken cancellationToken
    )
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null)
            return document;

        var assignment = SyntaxFactory.AssignmentExpression(
            SyntaxKind.SimpleAssignmentExpression,
            SyntaxFactory.IdentifierName("CancellationToken"),
            CancellationTokenHelpers.IdentifierNameFor(tokenName)
        );

        // Append to the existing initializer, or create one. Appending keeps whatever the author
        // already set (MaxDegreeOfParallelism, TaskScheduler) rather than replacing it.
        var initializer = creation.Initializer is { } existing
            ? existing.WithExpressions(existing.Expressions.Add(assignment))
            : SyntaxFactory.InitializerExpression(
                SyntaxKind.ObjectInitializerExpression,
                SyntaxFactory.SingletonSeparatedList<ExpressionSyntax>(assignment)
            );

        var updated = creation
            .WithInitializer(initializer)
            .WithAdditionalAnnotations(Formatter.Annotation);

        // `new ParallelOptions()` with an added initializer keeps its empty argument list, which is
        // legal but noisy; drop it so the result reads as `new ParallelOptions { … }`.
        if (
            updated is ObjectCreationExpressionSyntax
            {
                ArgumentList.Arguments.Count: 0
            } explicitCreation
        )
        {
            updated = explicitCreation.WithArgumentList(null);
        }

        var newRoot = root.ReplaceNode(creation, updated);

        if (newRoot is CompilationUnitSyntax compilationUnit)
        {
            newRoot = CancellationTokenFixHelpers.AddSystemThreadingUsing(compilationUnit);
        }

        return document.WithSyntaxRoot(newRoot);
    }
}
