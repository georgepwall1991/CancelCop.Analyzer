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
        var cancellationTokenPassed = invocation.ArgumentList.Arguments.Any(arg =>
        {
            var argType = context.SemanticModel.GetTypeInfo(arg.Expression).Type;
            return argType?.Name == "CancellationToken" &&
                   argType.ContainingNamespace?.ToString() == "System.Threading";
        });

        if (cancellationTokenPassed)
            return;

        // Find the containing method
        var containingMethod = invocation.FirstAncestorOrSelf<MethodDeclarationSyntax>();
        if (containingMethod == null)
            return;

        var containingMethodSymbol = context.SemanticModel.GetDeclaredSymbol(containingMethod);
        if (containingMethodSymbol == null)
            return;

        // Check if the containing method has a CancellationToken parameter
        var tokenParameter = containingMethodSymbol.Parameters.FirstOrDefault(p =>
            p.Type.Name == "CancellationToken" &&
            p.Type.ContainingNamespace?.ToString() == "System.Threading");

        if (tokenParameter == null)
            return;

        // Check if there's an overload that accepts a CancellationToken
        var containingType = methodSymbol.ContainingType;
        if (containingType == null)
            return;

        var overloads = containingType.GetMembers(methodSymbol.Name)
            .OfType<IMethodSymbol>()
            .Where(m => m.Parameters.Any(p =>
                p.Type.Name == "CancellationToken" &&
                p.Type.ContainingNamespace?.ToString() == "System.Threading"));

        if (!overloads.Any())
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
}
