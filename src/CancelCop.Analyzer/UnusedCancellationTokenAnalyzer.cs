using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CancelCop.Analyzer;

/// <summary>
/// Analyzer that detects CancellationToken parameters that are never used within the method body.
/// </summary>
/// <remarks>
/// <para>
/// <b>Rule ID:</b> CC008
/// </para>
/// <para>
/// <b>Why this matters:</b>
/// Accepting a CancellationToken parameter creates an expectation that the method supports cancellation.
/// If the token is never used, callers are misled about the method's behavior, and cancellation
/// requests will be silently ignored.
/// </para>
/// <para>
/// <b>What it detects:</b>
/// <list type="bullet">
/// <item>CancellationToken parameters that are not referenced in the method body</item>
/// <item>CancellationToken parameters not passed to any called methods</item>
/// <item>CancellationToken parameters not used with ThrowIfCancellationRequested()</item>
/// </list>
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Violation:
/// public async Task ProcessAsync(CancellationToken ct)
/// {
///     await Task.Delay(1000);  // CC008: ct is not used
/// }
///
/// // Fixed:
/// public async Task ProcessAsync(CancellationToken ct)
/// {
///     await Task.Delay(1000, ct);
/// }
/// </code>
/// </example>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class UnusedCancellationTokenAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// The diagnostic ID for this analyzer rule.
    /// </summary>
    public const string DiagnosticId = "CC008";
    private const string Category = "Usage";

    private static readonly LocalizableString Title = "CancellationToken parameter is not used";
    private static readonly LocalizableString MessageFormat = "CancellationToken parameter '{0}' is not used in method '{1}'";
    private static readonly LocalizableString Description = "CancellationToken parameters should be used to support cancellation. An unused token misleads callers about the method's cancellation support.";

    private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: Description);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeMethodDeclaration, SyntaxKind.MethodDeclaration);
        context.RegisterSyntaxNodeAction(AnalyzeLocalFunction, SyntaxKind.LocalFunctionStatement);
    }

    private static void AnalyzeMethodDeclaration(SyntaxNodeAnalysisContext context)
    {
        var methodDeclaration = (MethodDeclarationSyntax)context.Node;
        var methodSymbol = context.SemanticModel.GetDeclaredSymbol(methodDeclaration);

        if (methodSymbol == null)
            return;

        // Skip interface implementations and abstract methods
        if (methodDeclaration.Body == null && methodDeclaration.ExpressionBody == null)
            return;

        AnalyzeMethod(context, methodSymbol, methodDeclaration.ParameterList,
            methodDeclaration.Body, methodDeclaration.ExpressionBody);
    }

    private static void AnalyzeLocalFunction(SyntaxNodeAnalysisContext context)
    {
        var localFunction = (LocalFunctionStatementSyntax)context.Node;
        var methodSymbol = context.SemanticModel.GetDeclaredSymbol(localFunction);

        if (methodSymbol == null)
            return;

        if (localFunction.Body == null && localFunction.ExpressionBody == null)
            return;

        AnalyzeMethod(context, methodSymbol, localFunction.ParameterList,
            localFunction.Body, localFunction.ExpressionBody);
    }

    private static void AnalyzeMethod(
        SyntaxNodeAnalysisContext context,
        IMethodSymbol methodSymbol,
        ParameterListSyntax parameterList,
        BlockSyntax? body,
        ArrowExpressionClauseSyntax? expressionBody)
    {
        // Find CancellationToken parameters
        var ctParameters = methodSymbol.Parameters
            .Where(p => CancellationTokenHelpers.IsCancellationToken(p.Type))
            .ToList();

        if (!ctParameters.Any())
            return;

        // Get the method body
        SyntaxNode? methodBody = body ?? (SyntaxNode?)expressionBody?.Expression;
        if (methodBody == null)
            return;

        foreach (var ctParameter in ctParameters)
        {
            // Check if the parameter is referenced in the body
            var isUsed = IsParameterUsed(methodBody, ctParameter, context.SemanticModel);

            if (!isUsed)
            {
                // Find the parameter syntax for location
                var parameterSyntax = parameterList.Parameters
                    .FirstOrDefault(p => p.Identifier.Text == ctParameter.Name);

                if (parameterSyntax != null)
                {
                    var diagnostic = Diagnostic.Create(
                        Rule,
                        parameterSyntax.GetLocation(),
                        ctParameter.Name,
                        methodSymbol.Name);

                    context.ReportDiagnostic(diagnostic);
                }
            }
        }
    }

    private static bool IsParameterUsed(SyntaxNode body, IParameterSymbol parameter, SemanticModel semanticModel)
    {
        // Find all identifier names in the body
        var identifiers = body.DescendantNodes().OfType<IdentifierNameSyntax>();

        foreach (var identifier in identifiers)
        {
            // Check if this identifier refers to our parameter
            var symbol = semanticModel.GetSymbolInfo(identifier).Symbol;
            if (SymbolEqualityComparer.Default.Equals(symbol, parameter))
            {
                return true;
            }
        }

        return false;
    }
}
