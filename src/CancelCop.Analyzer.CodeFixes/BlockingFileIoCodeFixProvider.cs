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
/// Code fix provider that rewrites a blocking qualified or <c>using static</c> <c>System.IO</c> call
/// (including the <c>Stream</c> primitives) to its <c>await ...Async(..., token)</c> counterpart,
/// flowing the in-scope token when one is available.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(BlockingFileIoCodeFixProvider)), Shared]
public class BlockingFileIoCodeFixProvider : CodeFixProvider
{
    private const string Title = "Use the async I/O method";

    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(BlockingFileIoAnalyzer.DiagnosticId);

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

        // The diagnostic is correct but the analyzer determined no safe rewrite exists (e.g. the
        // call's named arguments do not line up with the async counterpart's parameter names).
        // Offering a fix here would emit code that does not compile.
        if (diagnostic.Properties.ContainsKey(BlockingFileIoAnalyzer.NoFixProperty))
            return;
        var invocation = root.FindToken(diagnostic.Location.SourceSpan.Start)
            .Parent?.AncestorsAndSelf()
            .OfType<InvocationExpressionSyntax>()
            .FirstOrDefault();
        var invokedName = invocation?.Expression switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name,
            IdentifierNameSyntax identifier => identifier,
            _ => null,
        };
        if (invocation == null || invokedName == null)
            return;

        var tokenName = diagnostic.Properties.TryGetValue(
            BlockingFileIoAnalyzer.TokenNameProperty,
            out var name
        )
            ? name
            : null;

        // The counterpart's own token parameter name; an override may rename it, so a named token
        // argument must not assume "cancellationToken".
        var tokenArgumentName = diagnostic.Properties.TryGetValue(
            BlockingFileIoAnalyzer.TokenArgumentNameProperty,
            out var argumentName
        )
            ? argumentName
            : "cancellationToken";

        context.RegisterCodeFix(
            CodeAction.Create(
                title: Title,
                createChangedDocument: c =>
                    ReplaceAsync(
                        context.Document,
                        invocation,
                        invokedName,
                        tokenName,
                        tokenArgumentName,
                        c
                    ),
                equivalenceKey: Title
            ),
            diagnostic
        );
    }

    private static async Task<Document> ReplaceAsync(
        Document document,
        InvocationExpressionSyntax invocation,
        SimpleNameSyntax invokedName,
        string? tokenName,
        string tokenArgumentName,
        CancellationToken cancellationToken
    )
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null)
            return document;

        // Append the in-scope token (when available) after the original arguments; the async
        // File.*Async overloads take the CancellationToken (named 'cancellationToken') as the last
        // parameter. AddTokenArgument keeps the call valid when the original used named arguments.
        var argumentList = invocation.ArgumentList.WithoutTrivia();
        if (tokenName != null)
        {
            argumentList = CancellationTokenFixHelpers.AddTokenArgument(
                argumentList,
                tokenName,
                tokenArgumentName
            );
        }

        var asyncName = invokedName.Identifier.Text + "Async";
        var asyncTarget = invocation.Expression is MemberAccessExpressionSyntax memberAccess
            ? (ExpressionSyntax)
                SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    memberAccess.Expression.WithoutTrivia(),
                    SyntaxFactory.IdentifierName(asyncName)
                )
            : SyntaxFactory.IdentifierName(asyncName);
        var asyncInvocation = SyntaxFactory.InvocationExpression(asyncTarget, argumentList);

        // When the blocking call is the receiver of a further access (e.g. File.ReadAllText(p).Trim()),
        // the await must be parenthesized so it binds before the trailing member/element access:
        // `(await File.ReadAllTextAsync(p)).Trim()`, not `await File.ReadAllTextAsync(p).Trim()`.
        ExpressionSyntax replacement = SyntaxFactory.AwaitExpression(asyncInvocation);
        if (NeedsParentheses(invocation))
            replacement = SyntaxFactory.ParenthesizedExpression(replacement);

        var newRoot = root.ReplaceNode(invocation, replacement.WithTriviaFrom(invocation));
        return document.WithSyntaxRoot(newRoot);
    }

    /// <summary>
    /// Returns <c>true</c> when the invocation is the receiver of a trailing access, so the inserted
    /// <c>await</c> would otherwise bind to the whole postfix chain instead of just the async call.
    /// </summary>
    private static bool NeedsParentheses(InvocationExpressionSyntax invocation) =>
        invocation.Parent switch
        {
            MemberAccessExpressionSyntax m => m.Expression == invocation,
            ElementAccessExpressionSyntax e => e.Expression == invocation,
            ConditionalAccessExpressionSyntax c => c.Expression == invocation,
            _ => false,
        };
}
