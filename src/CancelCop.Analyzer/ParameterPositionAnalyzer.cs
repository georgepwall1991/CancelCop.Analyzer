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

        // Don't flag methods whose parameter order is fixed by a base type or interface — the
        // override/implementation cannot reorder its parameters (CA1068 makes the same exception).
        if (CancellationTokenHelpers.IsSignatureExternallyControlled(methodSymbol))
            return;

        // Check if method has parameters
        if (!methodSymbol.Parameters.Any())
            return;

        // The 'this' receiver of an extension method must be first and cannot be moved, so it is
        // exempt; start the search after it (any further token parameter can still be moved last).
        var parameters = methodSymbol.Parameters;
        var startIndex = methodSymbol.IsExtensionMethod ? 1 : 0;
        var cancellationTokenIndex = -1;

        for (int i = startIndex; i < parameters.Length; i++)
        {
            if (CancellationTokenHelpers.IsCancellationToken(parameters[i].Type))
            {
                cancellationTokenIndex = i;
                break;
            }
        }

        // If no movable CancellationToken parameter, nothing to check
        if (cancellationTokenIndex == -1)
            return;

        // A trailing 'params' parameter must stay last, so a token immediately before it is
        // already in its best possible position and cannot be moved further right.
        if (cancellationTokenIndex == parameters.Length - 2 && parameters[parameters.Length - 1].IsParams)
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
