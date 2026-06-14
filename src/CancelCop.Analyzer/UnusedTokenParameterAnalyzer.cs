using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CancelCop.Analyzer;

/// <summary>
/// Analyzer that detects an <c>async</c> method or local function which accepts a
/// <c>CancellationToken</c> parameter but never references it — a dead token.
/// </summary>
/// <remarks>
/// <para>
/// <b>Rule ID:</b> CC016
/// </para>
/// <para>
/// <b>Why this matters:</b>
/// Accepting a <c>CancellationToken</c> advertises that the operation honours cancellation. A body
/// that never touches the token silently breaks that promise: callers pass a token expecting it to
/// take effect, but nothing observes it. Reported as <b>Info</b> because the token is occasionally
/// reserved deliberately.
/// </para>
/// <para>
/// <b>What it detects:</b> a method/local function whose body performs asynchronous work (contains
/// an <c>await</c>) and declares a <c>CancellationToken</c> parameter that is never referenced.
/// Signatures dictated elsewhere (override, interface implementation, <c>extern</c>) are excluded —
/// they cannot drop the parameter — as are bodies with no <c>await</c>. A token marked
/// <c>[EnumeratorCancellation]</c> is also excluded: the async-iterator infrastructure delivers the
/// caller's token to it, so it is observed even without a body reference (cf. CC011).
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Violation:
/// public async Task SaveAsync(string text, CancellationToken cancellationToken)  // CC016
/// {
///     await File.WriteAllTextAsync("f.txt", text);   // token ignored
/// }
/// </code>
/// </example>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class UnusedTokenParameterAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// The diagnostic ID for this analyzer rule.
    /// </summary>
    public const string DiagnosticId = "CC016";

    private static readonly LocalizableString Title = "CancellationToken parameter is never used";
    private static readonly LocalizableString MessageFormat = "CancellationToken parameter '{0}' is never used; observe it or remove it";
    private static readonly LocalizableString Description = "An async method that accepts a CancellationToken but never references it does not actually honour cancellation.";
    private const string Category = "Usage";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Info,
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
        SyntaxNode? body = method.Body ?? (SyntaxNode?)method.ExpressionBody;
        Analyze(context, method.ParameterList, body,
            context.SemanticModel.GetDeclaredSymbol(method, context.CancellationToken));
    }

    private void AnalyzeLocalFunction(SyntaxNodeAnalysisContext context)
    {
        var local = (LocalFunctionStatementSyntax)context.Node;
        SyntaxNode? body = local.Body ?? (SyntaxNode?)local.ExpressionBody;
        Analyze(context, local.ParameterList, body,
            context.SemanticModel.GetDeclaredSymbol(local, context.CancellationToken) as IMethodSymbol);
    }

    private static void Analyze(
        SyntaxNodeAnalysisContext context,
        ParameterListSyntax parameterList,
        SyntaxNode? body,
        IMethodSymbol? methodSymbol)
    {
        if (body == null || methodSymbol == null)
            return;

        // A signature dictated elsewhere cannot drop the parameter, so an unused token there is not
        // actionable.
        if (CancellationTokenHelpers.IsSignatureExternallyControlled(methodSymbol))
            return;

        // Only flag where there is asynchronous work that ought to observe the token.
        if (!body.DescendantNodes().OfType<AwaitExpressionSyntax>().Any())
            return;

        foreach (var parameter in parameterList.Parameters)
        {
            if (parameter.Type == null)
                continue;
            if (!CancellationTokenHelpers.IsCancellationToken(
                    context.SemanticModel.GetTypeInfo(parameter.Type, context.CancellationToken).Type))
                continue;

            if (context.SemanticModel.GetDeclaredSymbol(parameter, context.CancellationToken) is not IParameterSymbol parameterSymbol)
                continue;

            // A token marked [EnumeratorCancellation] is consumed by the generated async-iterator
            // enumerator (it receives the caller's WithCancellation token), so it is not dead even when
            // the body never references it directly. CC011 is the rule that ensures this attribute is
            // present.
            if (HasEnumeratorCancellation(parameterSymbol))
                continue;

            if (CancellationTokenHelpers.IsParameterReferenced(
                    body, parameterSymbol, context.SemanticModel, context.CancellationToken))
                continue;

            context.ReportDiagnostic(Diagnostic.Create(
                Rule, parameter.Identifier.GetLocation(), parameter.Identifier.Text));
        }
    }

    /// <summary>
    /// Returns true when the parameter carries
    /// <c>System.Runtime.CompilerServices.EnumeratorCancellationAttribute</c> — the token is delivered
    /// to it by the async-iterator infrastructure, so it is observed even without a body reference.
    /// </summary>
    private static bool HasEnumeratorCancellation(IParameterSymbol parameter) =>
        parameter.GetAttributes().Any(a =>
            a.AttributeClass?.Name == "EnumeratorCancellationAttribute" &&
            a.AttributeClass.ContainingNamespace?.ToDisplayString() == "System.Runtime.CompilerServices");
}
