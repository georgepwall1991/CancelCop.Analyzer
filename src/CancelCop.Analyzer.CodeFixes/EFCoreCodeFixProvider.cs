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

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(EFCoreCodeFixProvider)), Shared]
public class EFCoreCodeFixProvider : CodeFixProvider
{
    private const string Title = "Pass CancellationToken parameter";

    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(EFCoreAnalyzer.DiagnosticId);

    public sealed override FixAllProvider GetFixAllProvider() =>
        WellKnownFixAllProviders.BatchFixer;

    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root == null)
            return;

        var diagnostic = context.Diagnostics.First();
        var diagnosticSpan = diagnostic.Location.SourceSpan;

        var invocation = root.FindToken(diagnosticSpan.Start)
            .Parent?
            .AncestorsAndSelf()
            .OfType<InvocationExpressionSyntax>()
            .First();

        if (invocation == null)
            return;

        // Extract the token parameter name from diagnostic message
        var tokenParameterName = diagnostic.Properties.TryGetValue("TokenParameterName", out var name) && name != null
            ? name
            : "cancellationToken";

        // The target overload's token parameter name, used when a named argument is required.
        diagnostic.Properties.TryGetValue("TokenArgumentName", out var tokenArgumentName);

        context.RegisterCodeFix(
            CodeAction.Create(
                title: Title,
                createChangedDocument: c => AddCancellationTokenArgumentAsync(
                    context.Document, invocation, tokenParameterName, tokenArgumentName, c),
                equivalenceKey: Title),
            diagnostic);
    }

    private static async Task<Document> AddCancellationTokenArgumentAsync(
        Document document,
        InvocationExpressionSyntax invocation,
        string tokenParameterName,
        string? tokenArgumentName,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null)
            return document;

        // Append the token, switching to a named argument when the call already uses one.
        var newArgumentList = CancellationTokenFixHelpers.AddTokenArgument(
            invocation.ArgumentList, tokenParameterName, tokenArgumentName);
        var newInvocation = invocation.WithArgumentList(newArgumentList);

        var newRoot = root.ReplaceNode(invocation, newInvocation);

        return document.WithSyntaxRoot(newRoot);
    }
}
