using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CancelCop.Analyzer;

/// <summary>
/// Analyzer that detects an async iterator (a method or local function returning
/// <c>IAsyncEnumerable&lt;T&gt;</c> with <c>yield</c>) whose
/// <c>CancellationToken</c> parameter is not annotated with
/// <c>[EnumeratorCancellation]</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Rule ID:</b> CC011
/// </para>
/// <para>
/// <b>Why this matters:</b>
/// A consumer flows a token into an async stream with <c>source.WithCancellation(token)</c>. That
/// token is delivered only to the iterator parameter marked
/// <c>[EnumeratorCancellation]</c>; without the attribute the parameter silently receives
/// <c>default</c> and cancellation never reaches the producer's loops and awaits. This is the
/// producer-side complement to CC010.
/// </para>
/// <para>
/// <b>What it detects:</b> an iterator method/local function returning <c>IAsyncEnumerable&lt;T&gt;</c>
/// that declares a <c>CancellationToken</c> parameter, where none of its token parameters carry
/// <c>[EnumeratorCancellation]</c>.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Violation:
/// async IAsyncEnumerable&lt;int&gt; ReadAsync(CancellationToken token)   // CC011
/// {
///     yield return await NextAsync(token);
/// }
///
/// // Fixed:
/// async IAsyncEnumerable&lt;int&gt; ReadAsync([EnumeratorCancellation] CancellationToken token)
/// {
///     yield return await NextAsync(token);
/// }
/// </code>
/// </example>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class EnumeratorCancellationAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// The diagnostic ID for this analyzer rule.
    /// </summary>
    public const string DiagnosticId = "CC011";

    private static readonly LocalizableString Title = "Async iterator CancellationToken should be [EnumeratorCancellation]";
    private static readonly LocalizableString MessageFormat = "CancellationToken parameter '{0}' of async iterator '{1}' should be marked [EnumeratorCancellation] so WithCancellation can deliver the token";
    private static readonly LocalizableString Description = "An async-iterator CancellationToken parameter must be marked [EnumeratorCancellation], otherwise a token passed via WithCancellation never reaches it.";
    private const string Category = "Usage";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: Description,
        helpLinkUri: DiagnosticHelp.LinkUri);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(AnalyzeMethod, SyntaxKind.MethodDeclaration);
        context.RegisterSyntaxNodeAction(AnalyzeLocalFunction, SyntaxKind.LocalFunctionStatement);
    }

    private void AnalyzeMethod(SyntaxNodeAnalysisContext context)
    {
        var method = (MethodDeclarationSyntax)context.Node;
        Analyze(context, method.ReturnType, method.ParameterList, method.Body, method.Identifier.Text);
    }

    private void AnalyzeLocalFunction(SyntaxNodeAnalysisContext context)
    {
        var local = (LocalFunctionStatementSyntax)context.Node;
        Analyze(context, local.ReturnType, local.ParameterList, local.Body, local.Identifier.Text);
    }

    private static void Analyze(
        SyntaxNodeAnalysisContext context,
        TypeSyntax returnType,
        ParameterListSyntax? parameterList,
        BlockSyntax? body,
        string enclosingName)
    {
        // Async iterators have a block body containing yield; an expression-bodied or abstract
        // member cannot be an iterator.
        if (parameterList == null || body == null)
            return;

        // Must be a producer of IAsyncEnumerable<T>.
        if (context.SemanticModel.GetTypeInfo(returnType, context.CancellationToken).Type is not INamedTypeSymbol returnSymbol ||
            !IsAsyncEnumerable(returnSymbol))
            return;

        // Must actually be an iterator (contains yield in its own body, not a nested function's).
        if (!ContainsDirectYield(body))
            return;

        var tokenParameters = parameterList.Parameters
            .Where(p => p.Type != null &&
                        CancellationTokenHelpers.IsCancellationToken(
                            context.SemanticModel.GetTypeInfo(p.Type, context.CancellationToken).Type))
            .ToImmutableArray();

        if (tokenParameters.Length == 0)
            return;

        // If any token parameter is already annotated, the iterator is wired correctly.
        if (tokenParameters.Any(p => HasEnumeratorCancellation(p, context.SemanticModel, context.CancellationToken)))
            return;

        var target = tokenParameters[0];
        var diagnostic = Diagnostic.Create(
            Rule, target.Identifier.GetLocation(), target.Identifier.Text, enclosingName);
        context.ReportDiagnostic(diagnostic);
    }

    private static bool IsAsyncEnumerable(INamedTypeSymbol type)
    {
        return type.Name == "IAsyncEnumerable" &&
               type.IsGenericType &&
               type.ContainingNamespace?.ToDisplayString() == "System.Collections.Generic";
    }

    /// <summary>
    /// Returns true when <paramref name="body"/> contains a <c>yield</c> statement that belongs to
    /// this member — descent stops at nested local functions and lambdas, whose yields define their
    /// own iterators.
    /// </summary>
    private static bool ContainsDirectYield(SyntaxNode body)
    {
        foreach (var node in body.DescendantNodes(descendIntoChildren: child =>
                     child is not LocalFunctionStatementSyntax &&
                     child is not AnonymousFunctionExpressionSyntax))
        {
            if (node is YieldStatementSyntax)
                return true;
        }

        return false;
    }

    private static bool HasEnumeratorCancellation(
        ParameterSyntax parameter, SemanticModel semanticModel, System.Threading.CancellationToken cancellationToken)
    {
        if (semanticModel.GetDeclaredSymbol(parameter, cancellationToken) is not IParameterSymbol symbol)
            return false;

        return symbol.GetAttributes().Any(a =>
            a.AttributeClass?.Name == "EnumeratorCancellationAttribute" &&
            a.AttributeClass.ContainingNamespace?.ToDisplayString() == "System.Runtime.CompilerServices");
    }
}
