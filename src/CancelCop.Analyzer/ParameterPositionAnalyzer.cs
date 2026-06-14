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
        description: Description,
        helpLinkUri: DiagnosticHelp.LinkUri);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeMethodDeclaration, SyntaxKind.MethodDeclaration);
        context.RegisterSyntaxNodeAction(AnalyzeConstructorDeclaration, SyntaxKind.ConstructorDeclaration);
        context.RegisterSyntaxNodeAction(AnalyzeLocalFunction, SyntaxKind.LocalFunctionStatement);
        // Primary constructors (C# 12) put their parameters on the type declaration.
        context.RegisterSyntaxNodeAction(
            AnalyzePrimaryConstructor,
            SyntaxKind.ClassDeclaration,
            SyntaxKind.StructDeclaration,
            SyntaxKind.RecordDeclaration,
            SyntaxKind.RecordStructDeclaration);
    }

    private static void AnalyzeMethodDeclaration(SyntaxNodeAnalysisContext context)
    {
        var declaration = (MethodDeclarationSyntax)context.Node;
        var symbol = context.SemanticModel.GetDeclaredSymbol(declaration);
        // Public-surface convention: only public/protected methods are checked.
        Analyze(context, symbol, declaration.ParameterList, requireAccessibleSurface: true);
    }

    private static void AnalyzeConstructorDeclaration(SyntaxNodeAnalysisContext context)
    {
        var declaration = (ConstructorDeclarationSyntax)context.Node;
        var symbol = context.SemanticModel.GetDeclaredSymbol(declaration);
        Analyze(context, symbol, declaration.ParameterList, requireAccessibleSurface: true);
    }

    private static void AnalyzeLocalFunction(SyntaxNodeAnalysisContext context)
    {
        var declaration = (LocalFunctionStatementSyntax)context.Node;
        var symbol = context.SemanticModel.GetDeclaredSymbol(declaration) as IMethodSymbol;
        // Local functions have no public surface, but the convention still applies.
        Analyze(context, symbol, declaration.ParameterList, requireAccessibleSurface: false);
    }

    private static void AnalyzePrimaryConstructor(SyntaxNodeAnalysisContext context)
    {
        var typeDeclaration = (TypeDeclarationSyntax)context.Node;
        if (typeDeclaration.ParameterList == null)
            return;

        var typeSymbol = context.SemanticModel.GetDeclaredSymbol(typeDeclaration);
        var primaryConstructor = typeSymbol?.InstanceConstructors.FirstOrDefault(c =>
            c.DeclaringSyntaxReferences.Any(r => r.Span == typeDeclaration.Span));

        Analyze(context, primaryConstructor, typeDeclaration.ParameterList, requireAccessibleSurface: true);
    }

    private static void Analyze(
        SyntaxNodeAnalysisContext context,
        IMethodSymbol? methodSymbol,
        ParameterListSyntax parameterListSyntax,
        bool requireAccessibleSurface)
    {
        if (methodSymbol == null)
            return;

        // Only check public and protected members (where applicable to the surface).
        if (requireAccessibleSurface &&
            methodSymbol.DeclaredAccessibility != Accessibility.Public &&
            methodSymbol.DeclaredAccessibility != Accessibility.Protected)
            return;

        // Don't flag members whose parameter order is fixed by a base type or interface — the
        // override/implementation cannot reorder its parameters (CA1068 makes the same exception).
        if (CancellationTokenHelpers.IsSignatureExternallyControlled(methodSymbol))
            return;

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
            var cancellationTokenParameter = parameterListSyntax.Parameters[cancellationTokenIndex];

            var diagnostic = Diagnostic.Create(
                Rule,
                cancellationTokenParameter.GetLocation(),
                methodSymbol.Name);

            context.ReportDiagnostic(diagnostic);
        }
    }
}
