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
using Microsoft.CodeAnalysis.Simplification;

namespace CancelCop.Analyzer;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(MissingCancellationTokenCodeFixProvider)), Shared]
public class MissingCancellationTokenCodeFixProvider : CodeFixProvider
{
    private const string Title = "Add CancellationToken parameter";
    private const string CompilerServicesNamespace = "System.Runtime.CompilerServices";

    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(MissingCancellationTokenAnalyzer.DiagnosticId);

    public sealed override FixAllProvider GetFixAllProvider() =>
        WellKnownFixAllProviders.BatchFixer;

    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root == null)
            return;

        var diagnostic = context.Diagnostics.First();
        var diagnosticSpan = diagnostic.Location.SourceSpan;

        var methodDeclaration = root.FindToken(diagnosticSpan.Start)
            .Parent?
            .AncestorsAndSelf()
            .OfType<MethodDeclarationSyntax>()
            .FirstOrDefault();

        if (methodDeclaration == null)
            return;

        context.RegisterCodeFix(
            CodeAction.Create(
                title: Title,
                createChangedDocument: c => AddCancellationTokenParameterAsync(context.Document, methodDeclaration, c),
                equivalenceKey: Title),
            diagnostic);
    }

    private static async Task<Document> AddCancellationTokenParameterAsync(
        Document document,
        MethodDeclarationSyntax methodDeclaration,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null)
            return document;

        // Choose a parameter name that does not collide with an existing parameter (CS0100) or
        // a local declared in the body (CS0136).
        var tokenName = CancellationTokenFixHelpers.GetUniqueTokenParameterName(
            methodDeclaration.ParameterList,
            methodDeclaration.Body ?? (SyntaxNode?)methodDeclaration.ExpressionBody);

        var cancellationTokenParameter = SyntaxFactory.Parameter(
                SyntaxFactory.Identifier(tokenName))
            .WithType(SyntaxFactory.ParseTypeName("CancellationToken"))
            .WithDefault(SyntaxFactory.EqualsValueClause(
                SyntaxFactory.LiteralExpression(SyntaxKind.DefaultLiteralExpression,
                    SyntaxFactory.Token(SyntaxKind.DefaultKeyword))));

        // On an async iterator a bare token is worse than useless: the compiler-generated
        // GetAsyncEnumerator ignores it, so a consumer's .WithCancellation(token) silently fails to
        // reach it — which is precisely what CC011 exists to report. Adding the attribute here means
        // the fix produces working cancellation instead of trading CC001 for CC011.
        var semanticModel = await document
            .GetSemanticModelAsync(cancellationToken)
            .ConfigureAwait(false);
        var isAsyncIterator = IsAsyncEnumerableIterator(methodDeclaration, semanticModel, cancellationToken);
        if (isAsyncIterator)
        {
            // Fully qualified, with a simplifier annotation so the IDE shortens it back to
            // `[EnumeratorCancellation]`. An unqualified name would bind to a local
            // EnumeratorCancellationAttribute if the consumer's namespace declares one, producing
            // CS8425 and leaving the token unconsumed — the exact failure this fix exists to prevent.
            // global:: so a consumer's own nested `System` namespace cannot capture the name; the
            // simplifier still shortens it back to `[EnumeratorCancellation]` where unambiguous.
            var attributeName = SyntaxFactory
                .ParseName("global::" + CompilerServicesNamespace + ".EnumeratorCancellation")
                .WithAdditionalAnnotations(Simplifier.Annotation);

            cancellationTokenParameter = cancellationTokenParameter.WithAttributeLists(
                SyntaxFactory.SingletonList(
                    SyntaxFactory.AttributeList(
                        SyntaxFactory.SingletonSeparatedList(
                            SyntaxFactory.Attribute(attributeName)))));
        }

        // Insert before any trailing 'params' parameter (CS0231 guard); otherwise append last.
        var newParameterList = CancellationTokenFixHelpers.InsertTokenParameter(
            methodDeclaration.ParameterList, cancellationTokenParameter);
        var newMethodDeclaration = methodDeclaration.WithParameterList(newParameterList)
            .WithAdditionalAnnotations(Formatter.Annotation);

        var newRoot = root.ReplaceNode(methodDeclaration, newMethodDeclaration);

        if (newRoot is CompilationUnitSyntax compilationUnit)
        {
            newRoot = CancellationTokenFixHelpers.AddSystemThreadingUsing(compilationUnit);

            if (isAsyncIterator && newRoot is CompilationUnitSyntax withThreading)
            {
                newRoot = CancellationTokenFixHelpers.AddUsing(
                    withThreading, CompilerServicesNamespace);
            }
        }

        return document.WithSyntaxRoot(newRoot);
    }

    /// <summary>
    /// Returns <c>true</c> when the declaration is an async iterator returning
    /// <c>IAsyncEnumerable&lt;T&gt;</c> — the only shape <c>[EnumeratorCancellation]</c> applies to.
    /// </summary>
    /// <remarks>
    /// The return type is checked semantically, not just the presence of a <c>yield</c>: CC001 also
    /// covers iterators returning <c>IAsyncEnumerator&lt;T&gt;</c>, and the attribute is only
    /// effective on <c>IAsyncEnumerable&lt;T&gt;</c> — putting it elsewhere is CS8424, which breaks
    /// any project treating warnings as errors. A <c>yield</c> inside a nested local function or
    /// lambda belongs to that function's iterator, so the walk stops at those boundaries.
    /// </remarks>
    private static bool IsAsyncEnumerableIterator(
        MethodDeclarationSyntax declaration,
        SemanticModel? semanticModel,
        CancellationToken cancellationToken)
    {
        if (!declaration.Modifiers.Any(SyntaxKind.AsyncKeyword))
            return false;

        var yields = declaration
            .DescendantNodes(descendIntoChildren: node =>
                node == declaration ||
                node is not (LocalFunctionStatementSyntax or AnonymousFunctionExpressionSyntax))
            .Any(node => node is YieldStatementSyntax);
        if (!yields)
            return false;

        return semanticModel?.GetDeclaredSymbol(declaration, cancellationToken) is IMethodSymbol method &&
               method.ReturnType is INamedTypeSymbol { Name: "IAsyncEnumerable", TypeArguments.Length: 1 } returnType &&
               returnType.ContainingNamespace?.ToDisplayString() == "System.Collections.Generic";
    }
}
