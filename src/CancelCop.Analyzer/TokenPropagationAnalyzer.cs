using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CancelCop.Analyzer;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class TokenPropagationAnalyzer : DiagnosticAnalyzer
{
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
