using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CancelCop.Analyzer;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class MinimalApiAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "CC005C";
    private const string Category = "Usage";

    private static readonly LocalizableString Title = "Minimal API endpoint should have CancellationToken parameter";
    private static readonly LocalizableString MessageFormat = "Minimal API '{0}' endpoint handler should have a CancellationToken parameter";
    private static readonly LocalizableString Description = "ASP.NET Core Minimal API endpoint handlers should accept a CancellationToken parameter.";

    private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: Description);

    // Minimal API mapping methods
    private static readonly ImmutableHashSet<string> MapMethods = ImmutableHashSet.Create(
        "MapGet", "MapPost", "MapPut", "MapDelete", "MapPatch"
    );

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

        // Check if this is a MapGet/MapPost/etc. call
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            return;

        var methodName = memberAccess.Name.Identifier.Text;
        if (!MapMethods.Contains(methodName))
            return;

        // Get the second argument (the handler lambda/delegate)
        var arguments = invocation.ArgumentList.Arguments;
        if (arguments.Count < 2)
            return;

        var handlerArgument = arguments[1].Expression;

        // Check if it's a lambda expression
        if (handlerArgument is not ParenthesizedLambdaExpressionSyntax and not SimpleLambdaExpressionSyntax)
            return;

        // Check if the lambda is async
        var isAsync = false;
        ParameterListSyntax? parameterList = null;

        if (handlerArgument is ParenthesizedLambdaExpressionSyntax parenthesizedLambda)
        {
            isAsync = parenthesizedLambda.AsyncKeyword.IsKind(SyntaxKind.AsyncKeyword);
            parameterList = parenthesizedLambda.ParameterList;
        }
        else if (handlerArgument is SimpleLambdaExpressionSyntax simpleLambda)
        {
            isAsync = simpleLambda.AsyncKeyword.IsKind(SyntaxKind.AsyncKeyword);
            // For simple lambda, we need to check if it has CancellationToken parameter
            var parameterSymbol = context.SemanticModel.GetDeclaredSymbol(simpleLambda.Parameter);
            if (parameterSymbol != null && IsCancellationToken(parameterSymbol.Type))
                return; // Already has CancellationToken
        }

        if (!isAsync)
            return;

        // Check if any parameter is CancellationToken
        if (parameterList != null)
        {
            foreach (var parameter in parameterList.Parameters)
            {
                var parameterSymbol = context.SemanticModel.GetDeclaredSymbol(parameter);
                if (parameterSymbol != null && IsCancellationToken(parameterSymbol.Type))
                    return; // Already has CancellationToken
            }
        }

        // Report diagnostic on the lambda expression
        var diagnostic = Diagnostic.Create(
            Rule,
            handlerArgument.GetLocation(),
            methodName);

        context.ReportDiagnostic(diagnostic);
    }

    private static bool IsCancellationToken(ITypeSymbol type)
    {
        return type.Name == "CancellationToken" &&
               type.ContainingNamespace?.ToString() == "System.Threading";
    }
}
