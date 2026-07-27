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
/// Code fix provider that adds
/// <c>[EnumeratorCancellation] CancellationToken cancellationToken = default</c> to an async
/// iterator that has no token.
/// </summary>
/// <remarks>
/// The attribute is added at the same time deliberately. A bare token parameter on an iterator is
/// silently ignored by <c>.WithCancellation(token)</c> — which is CC011's whole point — so a fix
/// that added only the parameter would trade this diagnostic for that one and leave the stream just
/// as uncancellable.
/// </remarks>
[
    ExportCodeFixProvider(
        LanguageNames.CSharp,
        Name = nameof(AsyncStreamMissingTokenCodeFixProvider)
    ),
    Shared
]
public class AsyncStreamMissingTokenCodeFixProvider : CodeFixProvider
{
    private const string Title = "Add [EnumeratorCancellation] CancellationToken parameter";
    private const string CompilerServicesNamespace = "System.Runtime.CompilerServices";

    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(AsyncStreamMissingTokenAnalyzer.DiagnosticId);

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

        var declaration = root.FindToken(diagnostic.Location.SourceSpan.Start)
            .Parent?.AncestorsAndSelf()
            .OfType<MethodDeclarationSyntax>()
            .FirstOrDefault();
        if (declaration == null)
            return;

        context.RegisterCodeFix(
            CodeAction.Create(
                title: Title,
                createChangedDocument: c =>
                    AddTokenParameterAsync(context.Document, declaration, c),
                equivalenceKey: Title
            ),
            diagnostic
        );
    }

    private static async Task<Document> AddTokenParameterAsync(
        Document document,
        MethodDeclarationSyntax declaration,
        CancellationToken cancellationToken
    )
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null)
            return document;

        // Avoid colliding with an existing parameter (CS0100) or a local in the body (CS0136).
        var tokenName = CancellationTokenFixHelpers.GetUniqueTokenParameterName(
            declaration.ParameterList,
            declaration.Body ?? (SyntaxNode?)declaration.ExpressionBody
        );

        var parameter = SyntaxFactory
            .Parameter(SyntaxFactory.Identifier(tokenName))
            .WithType(SyntaxFactory.ParseTypeName("CancellationToken"))
            .WithAttributeLists(
                SyntaxFactory.SingletonList(
                    SyntaxFactory.AttributeList(
                        SyntaxFactory.SingletonSeparatedList(
                            SyntaxFactory.Attribute(
                                SyntaxFactory.IdentifierName("EnumeratorCancellation")
                            )
                        )
                    )
                )
            )
            .WithDefault(
                SyntaxFactory.EqualsValueClause(
                    SyntaxFactory.LiteralExpression(
                        SyntaxKind.DefaultLiteralExpression,
                        SyntaxFactory.Token(SyntaxKind.DefaultKeyword)
                    )
                )
            );

        // Before any trailing `params` parameter, which must remain last (CS0231).
        var newParameterList = CancellationTokenFixHelpers.InsertTokenParameter(
            declaration.ParameterList,
            parameter
        );

        var newRoot = root.ReplaceNode(
            declaration,
            declaration
                .WithParameterList(newParameterList)
                .WithAdditionalAnnotations(Formatter.Annotation)
        );

        if (newRoot is CompilationUnitSyntax compilationUnit)
        {
            newRoot = CancellationTokenFixHelpers.AddUsing(
                CancellationTokenFixHelpers.AddSystemThreadingUsing(compilationUnit),
                CompilerServicesNamespace
            );
        }

        return document.WithSyntaxRoot(newRoot);
    }
}
