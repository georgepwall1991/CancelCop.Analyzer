using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CancelCop.Analyzer;

/// <summary>
/// Analyzer that detects usage of CancellationToken.None when a CancellationToken is available in scope.
/// </summary>
/// <remarks>
/// <para>
/// <b>Rule ID:</b> CC007
/// </para>
/// <para>
/// <b>Why this matters:</b>
/// Using CancellationToken.None explicitly ignores cancellation, even when a token is available.
/// This defeats the purpose of accepting a CancellationToken parameter and can lead to
/// operations that cannot be cancelled, wasting resources and blocking graceful shutdowns.
/// </para>
/// <para>
/// <b>What it detects:</b>
/// <list type="bullet">
/// <item>CancellationToken.None passed as argument when a token is available</item>
/// <item>default(CancellationToken) passed as argument when a token is available</item>
/// </list>
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Violation:
/// public async Task ProcessAsync(CancellationToken ct)
/// {
///     await Task.Delay(1000, CancellationToken.None);  // CC007
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
public class CancellationTokenNoneAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// The diagnostic ID for this analyzer rule.
    /// </summary>
    public const string DiagnosticId = "CC007";
    private const string Category = "Usage";

    private static readonly LocalizableString Title = "Avoid using CancellationToken.None when a token is available";
    private static readonly LocalizableString MessageFormat = "Use '{0}' instead of CancellationToken.None";
    private static readonly LocalizableString Description = "When a CancellationToken is available in scope, it should be used instead of CancellationToken.None to enable proper cancellation handling.";

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
        context.RegisterSyntaxNodeAction(AnalyzeMemberAccess, SyntaxKind.SimpleMemberAccessExpression);
        context.RegisterSyntaxNodeAction(AnalyzeDefaultExpression, SyntaxKind.DefaultExpression);
        context.RegisterSyntaxNodeAction(AnalyzeDefaultLiteral, SyntaxKind.DefaultLiteralExpression);
    }

    private static void AnalyzeMemberAccess(SyntaxNodeAnalysisContext context)
    {
        var memberAccess = (MemberAccessExpressionSyntax)context.Node;

        // Check for CancellationToken.None
        if (memberAccess.Name.Identifier.Text != "None")
            return;

        var typeInfo = context.SemanticModel.GetTypeInfo(memberAccess.Expression);
        if (!IsCancellationTokenType(typeInfo.Type))
            return;

        ReportIfTokenAvailable(context, memberAccess);
    }

    private static void AnalyzeDefaultExpression(SyntaxNodeAnalysisContext context)
    {
        var defaultExpression = (DefaultExpressionSyntax)context.Node;

        // Check if it's default(CancellationToken)
        var typeInfo = context.SemanticModel.GetTypeInfo(defaultExpression.Type);
        if (!CancellationTokenHelpers.IsCancellationToken(typeInfo.Type))
            return;

        ReportIfTokenAvailable(context, defaultExpression);
    }

    private static void AnalyzeDefaultLiteral(SyntaxNodeAnalysisContext context)
    {
        var defaultLiteral = (LiteralExpressionSyntax)context.Node;

        // Check if this default literal is being used as a CancellationToken argument
        if (defaultLiteral.Parent is not ArgumentSyntax argument)
            return;

        var typeInfo = context.SemanticModel.GetTypeInfo(defaultLiteral);
        if (!CancellationTokenHelpers.IsCancellationToken(typeInfo.ConvertedType))
            return;

        ReportIfTokenAvailable(context, defaultLiteral);
    }

    private static void ReportIfTokenAvailable(SyntaxNodeAnalysisContext context, SyntaxNode node)
    {
        // Find available CancellationToken in scope
        var tokenParameter = FindAvailableCancellationToken(node, context.SemanticModel);
        if (tokenParameter == null)
            return;

        var properties = ImmutableDictionary.CreateBuilder<string, string?>();
        properties.Add("TokenName", tokenParameter);

        var diagnostic = Diagnostic.Create(
            Rule,
            node.GetLocation(),
            properties.ToImmutable(),
            tokenParameter);

        context.ReportDiagnostic(diagnostic);
    }

    private static string? FindAvailableCancellationToken(SyntaxNode node, SemanticModel semanticModel)
    {
        var current = node.Parent;
        while (current != null)
        {
            // Check local function
            if (current is LocalFunctionStatementSyntax localFunction)
            {
                var localFunctionSymbol = semanticModel.GetDeclaredSymbol(localFunction);
                var tokenParam = CancellationTokenHelpers.FindCancellationTokenParameter(localFunctionSymbol);
                if (tokenParam != null)
                    return tokenParam.Name;
            }
            // Check method declaration
            else if (current is MethodDeclarationSyntax methodDeclaration)
            {
                var methodSymbol = semanticModel.GetDeclaredSymbol(methodDeclaration);
                var tokenParam = CancellationTokenHelpers.FindCancellationTokenParameter(methodSymbol);
                if (tokenParam != null)
                    return tokenParam.Name;
            }
            // Check constructor
            else if (current is ConstructorDeclarationSyntax constructorDeclaration)
            {
                var constructorSymbol = semanticModel.GetDeclaredSymbol(constructorDeclaration);
                var tokenParam = constructorSymbol?.Parameters.FirstOrDefault(p =>
                    CancellationTokenHelpers.IsCancellationToken(p.Type));
                if (tokenParam != null)
                    return tokenParam.Name;
            }

            current = current.Parent;
        }

        return null;
    }

    private static bool IsCancellationTokenType(ITypeSymbol? type)
    {
        if (type == null)
            return false;

        return type.Name == "CancellationToken" &&
               type.ContainingNamespace?.ToString() == "System.Threading";
    }
}
