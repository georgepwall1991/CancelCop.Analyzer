using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CancelCop.Analyzer;

/// <summary>
/// Analyzer that detects async method invocations that don't propagate an available CancellationToken.
/// </summary>
/// <remarks>
/// <para>
/// <b>Rule ID:</b> CC002
/// </para>
/// <para>
/// <b>Why this matters:</b>
/// Having a CancellationToken parameter is useless if you don't pass it to inner async calls.
/// When a method accepts a token but doesn't propagate it, cancellation requests are silently
/// ignored, operations continue despite cancellation, and resources are wasted.
/// </para>
/// <para>
/// <b>What it detects:</b>
/// <list type="bullet">
/// <item>Task.Delay() calls without token when one is available</item>
/// <item>Task.Run() calls without token when one is available</item>
/// <item>Custom async methods that have overloads accepting CancellationToken</item>
/// </list>
/// </para>
/// <para>
/// <b>Scope:</b>
/// Checks method bodies, local functions, and lambda expressions for token availability.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Violation:
/// public async Task ProcessAsync(CancellationToken ct)
/// {
///     await Task.Delay(1000);  // CC002: Should pass ct
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
public class TokenPropagationAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// The diagnostic ID for this analyzer rule.
    /// </summary>
    public const string DiagnosticId = "CC002";
    private const string Category = "Usage";

    private static readonly LocalizableString Title = "CancellationToken should be propagated to async calls";
    private static readonly LocalizableString MessageFormat = "Method '{0}' should receive CancellationToken parameter '{1}'";
    private static readonly LocalizableString Description = "When a method has a CancellationToken parameter, it should be propagated to all async method calls within the method.";

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
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        // Get the method symbol for the invocation
        var methodSymbol = context.SemanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
        if (methodSymbol == null)
            return;

        // Check if a CancellationToken was already passed in the invocation
        if (CancellationTokenHelpers.HasCancellationTokenArgument(invocation, context.SemanticModel))
            return;

        // Try to find CancellationToken parameter from containing local function first, then containing method
        var tokenParameter = FindContainingCancellationTokenParameter(invocation, context.SemanticModel);
        if (tokenParameter == null)
            return;

        // Check if there's an overload that accepts a CancellationToken
        if (!CancellationTokenHelpers.HasOverloadWithCancellationToken(methodSymbol))
            return;

        // Report diagnostic
        var methodName = methodSymbol.Name;
        var properties = ImmutableDictionary.CreateBuilder<string, string?>();
        properties.Add("TokenParameterName", tokenParameter.Name);

        var diagnostic = Diagnostic.Create(
            Rule,
            invocation.Expression is MemberAccessExpressionSyntax memberAccess
                ? memberAccess.Name.GetLocation()
                : invocation.Expression.GetLocation(),
            properties.ToImmutable(),
            methodName,
            tokenParameter.Name);

        context.ReportDiagnostic(diagnostic);
    }

    /// <summary>
    /// Finds a CancellationToken parameter from the nearest containing local function or method.
    /// Checks local functions first (innermost to outermost), then the containing method.
    /// </summary>
    private static IParameterSymbol? FindContainingCancellationTokenParameter(
        SyntaxNode node,
        SemanticModel semanticModel)
    {
        // Walk up the syntax tree looking for local functions and methods
        var current = node.Parent;
        while (current != null)
        {
            // Check local function first
            if (current is LocalFunctionStatementSyntax localFunction)
            {
                var localFunctionSymbol = semanticModel.GetDeclaredSymbol(localFunction);
                var tokenParam = CancellationTokenHelpers.FindCancellationTokenParameter(localFunctionSymbol);
                if (tokenParam != null)
                    return tokenParam;
            }
            // Check method declaration
            else if (current is MethodDeclarationSyntax methodDeclaration)
            {
                var methodSymbol = semanticModel.GetDeclaredSymbol(methodDeclaration);
                return CancellationTokenHelpers.FindCancellationTokenParameter(methodSymbol);
            }

            current = current.Parent;
        }

        return null;
    }
}
