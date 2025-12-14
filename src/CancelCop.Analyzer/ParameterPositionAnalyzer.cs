using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CancelCop.Analyzer;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class ParameterPositionAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "CC006";
    private const string Category = "Style";

    private static readonly LocalizableString Title = "CancellationToken should be the last parameter";
    private static readonly LocalizableString MessageFormat = "Method '{0}' should have CancellationToken as the last parameter";
    private static readonly LocalizableString Description = "By convention, CancellationToken parameters should be positioned last in the parameter list.";

    private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Info,
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

        // Only check public and protected methods
        if (methodSymbol.DeclaredAccessibility != Accessibility.Public &&
            methodSymbol.DeclaredAccessibility != Accessibility.Protected)
            return;

        // Check if method has parameters
        if (!methodSymbol.Parameters.Any())
            return;

        // Find CancellationToken parameter
        var parameters = methodSymbol.Parameters;
        var cancellationTokenIndex = -1;

        for (int i = 0; i < parameters.Length; i++)
        {
            if (CancellationTokenHelpers.IsCancellationToken(parameters[i].Type))
            {
                cancellationTokenIndex = i;
                break;
            }
        }

        // If no CancellationToken parameter, nothing to check
        if (cancellationTokenIndex == -1)
            return;

        // If CancellationToken is not the last parameter, report diagnostic
        if (cancellationTokenIndex != parameters.Length - 1)
        {
            var cancellationTokenParameter = methodDeclaration.ParameterList.Parameters[cancellationTokenIndex];

            var diagnostic = Diagnostic.Create(
                Rule,
                cancellationTokenParameter.GetLocation(),
                methodSymbol.Name);

            context.ReportDiagnostic(diagnostic);
        }
    }
}
