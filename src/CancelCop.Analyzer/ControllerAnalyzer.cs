using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CancelCop.Analyzer;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class ControllerAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "CC005B";
    private const string Category = "Usage";

    private static readonly LocalizableString Title = "Controller action method should have CancellationToken parameter";
    private static readonly LocalizableString MessageFormat = "Controller action method '{0}' should have a CancellationToken parameter";
    private static readonly LocalizableString Description = "ASP.NET Core controller action methods should accept a CancellationToken parameter.";

    private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: Description);

    // HTTP method attributes
    private static readonly ImmutableHashSet<string> HttpMethodAttributes = ImmutableHashSet.Create(
        "HttpGet", "HttpPost", "HttpPut", "HttpDelete", "HttpPatch", "HttpHead", "HttpOptions"
    );

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeMethodDeclaration, SyntaxKind.MethodDeclaration);
    }

    private static void AnalyzeMethodDeclaration(SyntaxNodeAnalysisContext context)
    {
        var methodDeclaration = (MethodDeclarationSyntax)context.Node;
        var methodSymbol = context.SemanticModel.GetDeclaredSymbol(methodDeclaration);

        if (methodSymbol == null)
            return;

        // Check if method is async
        if (!methodSymbol.IsAsync && !IsTaskReturnType(methodSymbol.ReturnType))
            return;

        // Check if this is a controller action method
        if (!IsControllerActionMethod(methodSymbol))
            return;

        // Check if method already has CancellationToken parameter
        var hasCancellationToken = methodSymbol.Parameters.Any(p =>
            p.Type.Name == "CancellationToken" &&
            p.Type.ContainingNamespace?.ToString() == "System.Threading");

        if (hasCancellationToken)
            return;

        // Report diagnostic
        var diagnostic = Diagnostic.Create(
            Rule,
            methodDeclaration.Identifier.GetLocation(),
            methodSymbol.Name);

        context.ReportDiagnostic(diagnostic);
    }

    private static bool IsControllerActionMethod(IMethodSymbol methodSymbol)
    {
        // Check if the containing type inherits from ControllerBase or Controller
        var containingType = methodSymbol.ContainingType;
        if (containingType == null)
            return false;

        var inheritsFromController = InheritsFromControllerBase(containingType);
        if (!inheritsFromController)
            return false;

        // Check if method has an HTTP method attribute
        var hasHttpAttribute = methodSymbol.GetAttributes().Any(attr =>
        {
            var attributeName = attr.AttributeClass?.Name;
            if (attributeName == null)
                return false;

            return HttpMethodAttributes.Contains(attributeName) ||
                   HttpMethodAttributes.Contains(attributeName.Replace("Attribute", ""));
        });

        return hasHttpAttribute;
    }

    private static bool InheritsFromControllerBase(INamedTypeSymbol typeSymbol)
    {
        var baseType = typeSymbol.BaseType;
        while (baseType != null)
        {
            if (baseType.Name == "ControllerBase" || baseType.Name == "Controller")
                return true;

            baseType = baseType.BaseType;
        }

        return false;
    }

    private static bool IsTaskReturnType(ITypeSymbol returnType)
    {
        if (returnType.Name == "Task" && returnType.ContainingNamespace?.ToString() == "System.Threading.Tasks")
            return true;

        // Check for Task<T>
        if (returnType is INamedTypeSymbol namedType &&
            namedType.IsGenericType &&
            namedType.Name == "Task" &&
            namedType.ContainingNamespace?.ToString() == "System.Threading.Tasks")
            return true;

        return false;
    }
}
