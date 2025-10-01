using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CancelCop.Analyzer;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class MissingCancellationTokenAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "CC001";
    private const string Category = "Usage";

    private static readonly LocalizableString Title = "Public async method should have CancellationToken parameter";
    private static readonly LocalizableString MessageFormat = "Public async method '{0}' should have a CancellationToken parameter";
    private static readonly LocalizableString Description = "Public async methods should accept a CancellationToken parameter to allow cancellation of async operations.";

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

        // Check if method is async
        if (!methodDeclaration.Modifiers.Any(SyntaxKind.AsyncKeyword))
            return;

        // Check if method is public or protected
        var isPublicOrProtected = methodDeclaration.Modifiers.Any(m =>
            m.IsKind(SyntaxKind.PublicKeyword) || m.IsKind(SyntaxKind.ProtectedKeyword));

        if (!isPublicOrProtected)
            return;

        // Check if method returns Task or Task<T>
        var methodSymbol = context.SemanticModel.GetDeclaredSymbol(methodDeclaration);
        if (methodSymbol == null)
            return;

        var returnType = methodSymbol.ReturnType;
        if (returnType.Name != "Task")
            return;

        // Check if method already has CancellationToken parameter
        var hasCancellationToken = methodSymbol.Parameters.Any(p =>
            p.Type.Name == "CancellationToken" &&
            p.Type.ContainingNamespace?.ToString() == "System.Threading");

        if (hasCancellationToken)
            return;

        // Report diagnostic
        var diagnostic = Diagnostic.Create(Rule, methodDeclaration.Identifier.GetLocation(), methodDeclaration.Identifier.Text);
        context.ReportDiagnostic(diagnostic);
    }
}
