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

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(MinimalApiCodeFixProvider)), Shared]
public class MinimalApiCodeFixProvider : CodeFixProvider
{
    private const string Title = "Add CancellationToken parameter";

    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create("CC005C");

    public sealed override FixAllProvider GetFixAllProvider() =>
        WellKnownFixAllProviders.BatchFixer;

    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root == null)
            return;

        var diagnostic = context.Diagnostics.First();
        var diagnosticSpan = diagnostic.Location.SourceSpan;

        var node = root.FindToken(diagnosticSpan.Start).Parent;
        if (node == null)
            return;

        // A method-group diagnostic spans the handler reference (`Handler` / `Handlers.Get`).
        // The fix targets the referenced method's declaration, not anything at the call site.
        var handlerExpression = node.AncestorsAndSelf()
            .OfType<ExpressionSyntax>()
            .FirstOrDefault(e => e.Span == diagnosticSpan);

        if (handlerExpression is IdentifierNameSyntax or MemberAccessExpressionSyntax)
        {
            await RegisterMethodGroupFixAsync(context, root, handlerExpression, diagnostic).ConfigureAwait(false);
            return;
        }

        // Lambda diagnostics span the whole lambda; matching on the exact span keeps a
        // diagnostic reported elsewhere from being "fixed" on an enclosing lambda.
        var parenthesizedLambda = node.AncestorsAndSelf()
            .OfType<ParenthesizedLambdaExpressionSyntax>()
            .FirstOrDefault(l => l.Span == diagnosticSpan);

        if (parenthesizedLambda != null)
        {
            context.RegisterCodeFix(
                CodeAction.Create(
                    title: Title,
                    createChangedDocument: c => AddCancellationTokenToParenthesizedLambdaAsync(
                        context.Document, parenthesizedLambda, c),
                    equivalenceKey: Title),
                diagnostic);
            return;
        }

        // A simple lambda has a single UNTYPED parameter (e.g. `async id => ...`). Adding a
        // typed CancellationToken would mix typed/untyped parameters (CS0748), and an untyped
        // token is not recognized as a CancellationToken (the fix would re-fire). Such a lambda
        // is not a bindable minimal-API handler anyway, so no safe fix can be offered — skip it.
    }

    private static async Task RegisterMethodGroupFixAsync(
        CodeFixContext context,
        SyntaxNode root,
        ExpressionSyntax handlerExpression,
        Diagnostic diagnostic)
    {
        var semanticModel = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (semanticModel == null)
            return;

        var symbolInfo = semanticModel.GetSymbolInfo(handlerExpression, context.CancellationToken);
        var handlerMethod = symbolInfo.Symbol as IMethodSymbol;
        if (handlerMethod == null && symbolInfo.CandidateSymbols.Length == 1)
            handlerMethod = symbolInfo.CandidateSymbols[0] as IMethodSymbol;
        if (handlerMethod == null)
            return;

        var declaration = handlerMethod.DeclaringSyntaxReferences.FirstOrDefault()
            ?.GetSyntax(context.CancellationToken);

        // Only same-document declarations are rewritten; a handler defined in another file keeps
        // the diagnostic but gets no automatic fix (the fix-all scope would otherwise surprise).
        if (declaration?.SyntaxTree != root.SyntaxTree)
            return;

        // The handler can be a method or a local function — both carry a parameter list.
        if (declaration is not MethodDeclarationSyntax and not LocalFunctionStatementSyntax)
            return;

        context.RegisterCodeFix(
            CodeAction.Create(
                title: Title,
                createChangedDocument: c => AddCancellationTokenToHandlerDeclarationAsync(
                    context.Document, declaration, c),
                equivalenceKey: Title),
            diagnostic);
    }

    private static async Task<Document> AddCancellationTokenToHandlerDeclarationAsync(
        Document document,
        SyntaxNode declaration,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null)
            return document;

        var (parameterList, body) = declaration switch
        {
            MethodDeclarationSyntax m => (m.ParameterList, m.Body ?? (SyntaxNode?)m.ExpressionBody),
            LocalFunctionStatementSyntax f => (f.ParameterList, f.Body ?? (SyntaxNode?)f.ExpressionBody),
            _ => (null, null),
        };
        if (parameterList == null)
            return document;

        var tokenName = CancellationTokenFixHelpers.GetUniqueTokenParameterName(parameterList, body);

        // `= default` keeps any other existing call sites of the handler compiling; ASP.NET Core
        // binds the parameter regardless of the default.
        var tokenParameter = SyntaxFactory.Parameter(SyntaxFactory.Identifier(tokenName))
            .WithType(SyntaxFactory.ParseTypeName("CancellationToken"))
            .WithDefault(CancellationTokenFixHelpers.DefaultValueClause());

        var newParameterList = CancellationTokenFixHelpers.InsertTokenParameter(parameterList, tokenParameter);

        var newDeclaration = declaration switch
        {
            MethodDeclarationSyntax m => (SyntaxNode)m.WithParameterList(newParameterList),
            LocalFunctionStatementSyntax f => f.WithParameterList(newParameterList),
            _ => declaration,
        };

        var newRoot = root.ReplaceNode(declaration, newDeclaration);

        if (newRoot is CompilationUnitSyntax compilationUnit)
            newRoot = CancellationTokenFixHelpers.AddSystemThreadingUsing(compilationUnit);

        return document.WithSyntaxRoot(newRoot);
    }

    private static async Task<Document> AddCancellationTokenToParenthesizedLambdaAsync(
        Document document,
        ParenthesizedLambdaExpressionSyntax lambda,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null)
            return document;

        // Avoid colliding with a parameter (CS0100) or a local declared in the lambda body (CS0136).
        var tokenName = CancellationTokenFixHelpers.GetUniqueTokenParameterName(lambda.ParameterList, lambda.Body);

        var tokenParameter = SyntaxFactory.Parameter(SyntaxFactory.Identifier(tokenName))
            .WithType(SyntaxFactory.ParseTypeName("CancellationToken"));

        var newParameterList = lambda.ParameterList.AddParameters(tokenParameter);
        var newLambda = lambda.WithParameterList(newParameterList);

        var newRoot = root.ReplaceNode(lambda, newLambda);

        if (newRoot is CompilationUnitSyntax compilationUnit)
            newRoot = CancellationTokenFixHelpers.AddSystemThreadingUsing(compilationUnit);

        return document.WithSyntaxRoot(newRoot);
    }
}
