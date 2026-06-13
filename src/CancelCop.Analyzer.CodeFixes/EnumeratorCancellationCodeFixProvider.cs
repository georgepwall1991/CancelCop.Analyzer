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
/// Code fix provider that marks an async iterator's <c>CancellationToken</c> parameter with
/// <c>[EnumeratorCancellation]</c> (adding the <c>System.Runtime.CompilerServices</c> import).
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(EnumeratorCancellationCodeFixProvider)), Shared]
public class EnumeratorCancellationCodeFixProvider : CodeFixProvider
{
    private const string Title = "Add [EnumeratorCancellation]";
    private const string CompilerServicesNamespace = "System.Runtime.CompilerServices";

    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(EnumeratorCancellationAnalyzer.DiagnosticId);

    public sealed override FixAllProvider GetFixAllProvider() =>
        WellKnownFixAllProviders.BatchFixer;

    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root == null)
            return;

        var diagnostic = context.Diagnostics.First();
        var token = root.FindToken(diagnostic.Location.SourceSpan.Start);
        var parameter = token.Parent?.AncestorsAndSelf().OfType<ParameterSyntax>().FirstOrDefault();
        if (parameter == null)
            return;

        context.RegisterCodeFix(
            CodeAction.Create(
                title: Title,
                createChangedDocument: c => AddAttributeAsync(context.Document, parameter, c),
                equivalenceKey: Title),
            diagnostic);
    }

    private static async Task<Document> AddAttributeAsync(
        Document document,
        ParameterSyntax parameter,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is not CompilationUnitSyntax compilationUnit)
            return document;

        var attribute = SyntaxFactory.Attribute(SyntaxFactory.IdentifierName("EnumeratorCancellation"));
        var attributeList = SyntaxFactory.AttributeList(SyntaxFactory.SingletonSeparatedList(attribute))
            .WithTrailingTrivia(SyntaxFactory.Space);

        // Keep the parameter's leading trivia on the attribute list so spacing/indentation is preserved.
        var newParameter = parameter
            .WithoutLeadingTrivia()
            .WithAttributeLists(parameter.AttributeLists.Insert(0, attributeList))
            .WithLeadingTrivia(parameter.GetLeadingTrivia());

        compilationUnit = compilationUnit.ReplaceNode(parameter, newParameter);
        compilationUnit = CancellationTokenFixHelpers.AddUsing(compilationUnit, CompilerServicesNamespace);

        return document.WithSyntaxRoot(compilationUnit);
    }
}
