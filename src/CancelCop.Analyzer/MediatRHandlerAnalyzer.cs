using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CancelCop.Analyzer;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class MediatRHandlerAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "CC005A";
    private const string Category = "Usage";

    private static readonly LocalizableString Title = "MediatR handler method should have CancellationToken parameter";
    private static readonly LocalizableString MessageFormat = "MediatR handler method '{0}' should have a CancellationToken parameter";
    private static readonly LocalizableString Description = "MediatR IRequestHandler.Handle methods should accept a CancellationToken parameter.";

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

        // Check if this is a MediatR IRequestHandler implementation
        if (!IsRequestHandlerImplementation(methodSymbol))
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

    private static bool IsRequestHandlerImplementation(IMethodSymbol methodSymbol)
    {
        // Check if the containing type implements IRequestHandler<TRequest> or IRequestHandler<TRequest, TResponse>
        var containingType = methodSymbol.ContainingType;
        if (containingType == null)
            return false;

        var implementsRequestHandler = containingType.AllInterfaces.Any(i =>
        {
            var interfaceName = i.Name;
            var namespaceName = i.ContainingNamespace?.ToDisplayString();

            return (interfaceName == "IRequestHandler" && namespaceName == "MediatR") &&
                   methodSymbol.Name == "Handle";
        });

        return implementsRequestHandler;
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
