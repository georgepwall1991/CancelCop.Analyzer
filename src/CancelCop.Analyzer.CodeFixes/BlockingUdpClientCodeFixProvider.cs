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
/// Code fix provider that rewrites a discarded
/// <c>client.Receive(ref endpoint)</c> to
/// <c>await client.ReceiveAsync([cancellationToken])</c>.
/// The TAP does not take the <c>ref</c> endpoint and returns
/// <c>UdpReceiveResult</c>, so a value-use is left alone.
/// </summary>
[
    ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(BlockingUdpClientCodeFixProvider)),
    Shared
]
public class BlockingUdpClientCodeFixProvider : CodeFixProvider
{
    private const string Title = "Use await ReceiveAsync";

    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(BlockingUdpClientAnalyzer.DiagnosticId);

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

        if (diagnostic.Properties.ContainsKey(BlockingUdpClientAnalyzer.NoFixProperty))
            return;

        var invocation = root.FindToken(diagnostic.Location.SourceSpan.Start)
            .Parent?.AncestorsAndSelf()
            .OfType<InvocationExpressionSyntax>()
            .FirstOrDefault();

        if (invocation is null)
            return;

        var tokenName = diagnostic.Properties.TryGetValue(
            BlockingUdpClientAnalyzer.TokenNameProperty,
            out var name
        )
            ? name
            : null;

        var tokenArgumentName = diagnostic.Properties.TryGetValue(
            BlockingUdpClientAnalyzer.TokenArgumentNameProperty,
            out var argumentName
        )
            ? argumentName
            : null;

        var asyncInvocation = BlockingUdpClientAnalyzer.BuildReceiveAsyncInvocation(
            invocation,
            tokenName,
            tokenArgumentName
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

        if (
            invocation.Parent is not ExpressionStatementSyntax statement
            || !BlockingUdpClientAnalyzer.TryGetSimpleRefIdentifier(invocation, out var endpoint)
        )
            return document;

        var receivedName = UniqueReceivedName(statement, invocation);
        var received = SyntaxFactory.IdentifierName(receivedName);
        ExpressionSyntax awaitExpression = SyntaxFactory.AwaitExpression(asyncInvocation);
        if (CancellationTokenHelpers.AwaitNeedsParentheses(invocation))
            awaitExpression = SyntaxFactory.ParenthesizedExpression(awaitExpression);

        var receivedDecl = SyntaxFactory
            .LocalDeclarationStatement(
                SyntaxFactory.VariableDeclaration(
                    SyntaxFactory.IdentifierName("var"),
                    SyntaxFactory.SingletonSeparatedList(
                        SyntaxFactory
                            .VariableDeclarator(receivedName)
                            .WithInitializer(SyntaxFactory.EqualsValueClause(awaitExpression))
                    )
                )
            )
            .WithTriviaFrom(statement);

        var assignEndpoint = SyntaxFactory
            .ExpressionStatement(
                SyntaxFactory.AssignmentExpression(
                    SyntaxKind.SimpleAssignmentExpression,
                    SyntaxFactory.IdentifierName(endpoint.Identifier),
                    SyntaxFactory.MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        received,
                        SyntaxFactory.IdentifierName("RemoteEndPoint")
                    )
                )
            )
            .WithLeadingTrivia(statement.GetLeadingTrivia());

        if (statement.Parent is not BlockSyntax)
            return document;

        return document.WithSyntaxRoot(
            root.ReplaceNode(statement, new SyntaxNode[] { receivedDecl, assignEndpoint })
        );
    }

    private static string UniqueReceivedName(
        SyntaxNode statement,
        InvocationExpressionSyntax invocation
    )
    {
        var scope =
            statement
                .Ancestors()
                .FirstOrDefault(candidate =>
                    candidate
                        is BaseMethodDeclarationSyntax
                            or LocalFunctionStatementSyntax
                            or AnonymousFunctionExpressionSyntax
                )
            ?? statement.SyntaxTree.GetRoot();

        var used = new HashSet<string>(
            scope
                .DescendantNodesAndSelf()
                .SelectMany(node =>
                    node switch
                    {
                        ParameterSyntax parameter => new[] { parameter.Identifier.ValueText },
                        VariableDeclaratorSyntax declarator => new[]
                        {
                            declarator.Identifier.ValueText,
                        },
                        SingleVariableDesignationSyntax designation => new[]
                        {
                            designation.Identifier.ValueText,
                        },
                        IdentifierNameSyntax identifier => new[]
                        {
                            identifier.Identifier.ValueText,
                        },
                        _ => Array.Empty<string>(),
                    }
                )
        );

        var receiveIndex = scope
            .DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Where(candidate =>
                candidate.Expression
                    is IdentifierNameSyntax { Identifier.ValueText: "Receive" }
                        or MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Receive" }
            )
            .TakeWhile(candidate => candidate.SpanStart < invocation.SpanStart)
            .Count();

        for (var n = receiveIndex; ; n++)
        {
            var candidate = n == 0 ? "received" : "received" + n;
            if (!used.Contains(candidate))
                return candidate;
        }
    }
}
