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
/// Code fix provider that replaces a blocking <c>Thread.Sleep(delay)</c> with
/// <c>await Task.Delay(delay, token)</c>, flowing the in-scope token when one is available.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(BlockingSleepCodeFixProvider)), Shared]
public class BlockingSleepCodeFixProvider : CodeFixProvider
{
    private const string Title = "Replace with await Task.Delay";
    private const string TasksNamespace = "System.Threading.Tasks";

    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(BlockingSleepAnalyzer.DiagnosticId);

    public sealed override FixAllProvider GetFixAllProvider() =>
        WellKnownFixAllProviders.BatchFixer;

    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root == null)
            return;

        var diagnostic = context.Diagnostics.First();
        // The diagnostic stands but the analyzer determined that inserting an await here would not
        // compile, so no rewrite is offered.
        if (diagnostic.Properties.ContainsKey(BlockingSleepAnalyzer.NoFixProperty))
            return;
        var invocation = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true)
            ?.AncestorsAndSelf().OfType<InvocationExpressionSyntax>().FirstOrDefault();
        if (invocation == null)
            return;

        var tokenName = diagnostic.Properties.TryGetValue(BlockingSleepAnalyzer.TokenNameProperty, out var name)
            ? name
            : null;

        context.RegisterCodeFix(
            CodeAction.Create(
                title: Title,
                createChangedDocument: c => ReplaceWithTaskDelayAsync(context.Document, invocation, tokenName, c),
                equivalenceKey: Title),
            diagnostic);
    }

    private static async Task<Document> ReplaceWithTaskDelayAsync(
        Document document,
        InvocationExpressionSyntax sleepInvocation,
        string? tokenName,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null)
            return document;

        // Parameter names are NOT portable across the rewrite: Thread.Sleep spells its first
        // parameter `millisecondsTimeout`, while Task.Delay uses `millisecondsDelay`/`delay`.
        // Copying a named argument verbatim emits CS1739, so the rewrite binds positionally —
        // the argument list maps one-to-one onto every Delay overload the rule can reach.
        var arguments = sleepInvocation.ArgumentList.Arguments;
        if (arguments.Any(a => a.NameColon != null))
        {
            arguments = SyntaxFactory.SeparatedList(
                arguments.Select(a => a.NameColon == null ? a : a.WithNameColon(null)));
        }

        if (tokenName != null)
        {
            arguments = arguments.Add(
                SyntaxFactory.Argument(CancellationTokenHelpers.TokenExpression(tokenName)));
        }

        var taskDelay = SyntaxFactory.InvocationExpression(
            SyntaxFactory.MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                SyntaxFactory.IdentifierName("Task"),
                SyntaxFactory.IdentifierName("Delay")),
            SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(arguments)));

        var awaitExpression = SyntaxFactory.AwaitExpression(taskDelay)
            .WithTriviaFrom(sleepInvocation);

        var newRoot = root.ReplaceNode(sleepInvocation, awaitExpression);

        if (newRoot is CompilationUnitSyntax compilationUnit)
            newRoot = CancellationTokenFixHelpers.AddUsing(compilationUnit, TasksNamespace);

        return document.WithSyntaxRoot(newRoot);
    }
}
