using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CancelCop.Analyzer;

/// <summary>
/// Analyzer that detects loops without cancellation checks in methods that have a CancellationToken parameter.
/// </summary>
/// <remarks>
/// <para>
/// <b>Rule ID:</b> CC009
/// </para>
/// <para>
/// <b>Why this matters:</b>
/// Loops are where code spends most of its time. A loop processing millions of items could take
/// minutes or hours. Without cancellation checks, users cannot stop operations, application
/// shutdown is blocked, and resources are consumed long after they're needed.
/// </para>
/// <para>
/// <b>What it detects:</b>
/// <list type="bullet">
/// <item><c>for</c> loops without cancellation checks</item>
/// <item><c>foreach</c> loops without cancellation checks</item>
/// <item><c>while</c> loops without cancellation checks</item>
/// <item><c>do-while</c> loops without cancellation checks</item>
/// </list>
/// </para>
/// <para>
/// <b>What satisfies the check:</b>
/// <list type="bullet">
/// <item>Calling <c>ThrowIfCancellationRequested()</c> inside the loop</item>
/// <item>Checking <c>IsCancellationRequested</c> property inside the loop</item>
/// </list>
/// </para>
/// <para>
/// <b>Scope:</b>
/// Only analyzes loops in methods, local functions, or lambdas that have a CancellationToken
/// parameter available.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Violation:
/// public void ProcessItems(List&lt;Item&gt; items, CancellationToken ct)
/// {
///     foreach (var item in items)  // CC009: Loop should check cancellation
///     {
///         Process(item);
///     }
/// }
///
/// // Fixed:
/// public void ProcessItems(List&lt;Item&gt; items, CancellationToken ct)
/// {
///     foreach (var item in items)
///     {
///         ct.ThrowIfCancellationRequested();
///         Process(item);
///     }
/// }
/// </code>
/// </example>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class LoopCancellationAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// The diagnostic ID for this analyzer rule.
    /// </summary>
    public const string DiagnosticId = "CC009";

    /// <summary>
    /// Property key used to pass the token parameter name to the code fix provider.
    /// </summary>
    public const string TokenNameProperty = "TokenName";

    private static readonly LocalizableString Title = "Loop should check for cancellation";
    private static readonly LocalizableString MessageFormat = "Loop should call {0}.ThrowIfCancellationRequested() or check {0}.IsCancellationRequested";
    private static readonly LocalizableString Description = "Loops in methods with CancellationToken parameters should periodically check for cancellation to allow graceful shutdown.";
    private const string Category = "Usage";

    private static readonly DiagnosticDescriptor Rule = new(
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

        context.RegisterSyntaxNodeAction(AnalyzeForStatement, SyntaxKind.ForStatement);
        context.RegisterSyntaxNodeAction(AnalyzeForEachStatement, SyntaxKind.ForEachStatement);
        context.RegisterSyntaxNodeAction(AnalyzeWhileStatement, SyntaxKind.WhileStatement);
        context.RegisterSyntaxNodeAction(AnalyzeDoStatement, SyntaxKind.DoStatement);
    }

    private void AnalyzeForStatement(SyntaxNodeAnalysisContext context)
    {
        var forStatement = (ForStatementSyntax)context.Node;
        AnalyzeLoop(context, forStatement, forStatement.Statement, forStatement.ForKeyword);
    }

    private void AnalyzeForEachStatement(SyntaxNodeAnalysisContext context)
    {
        var foreachStatement = (ForEachStatementSyntax)context.Node;
        AnalyzeLoop(context, foreachStatement, foreachStatement.Statement, foreachStatement.ForEachKeyword);
    }

    private void AnalyzeWhileStatement(SyntaxNodeAnalysisContext context)
    {
        var whileStatement = (WhileStatementSyntax)context.Node;
        AnalyzeLoop(context, whileStatement, whileStatement.Statement, whileStatement.WhileKeyword);
    }

    private void AnalyzeDoStatement(SyntaxNodeAnalysisContext context)
    {
        var doStatement = (DoStatementSyntax)context.Node;
        AnalyzeLoop(context, doStatement, doStatement.Statement, doStatement.DoKeyword);
    }

    private void AnalyzeLoop(SyntaxNodeAnalysisContext context, SyntaxNode loopNode, StatementSyntax loopBody, SyntaxToken loopKeyword)
    {
        // Find the containing method or local function that has a CancellationToken parameter
        var tokenParameter = FindContainingCancellationTokenParameter(loopNode, context.SemanticModel);
        if (tokenParameter == null)
            return;

        // Check if the loop body contains a cancellation check
        if (HasCancellationCheck(loopBody, tokenParameter.Name))
            return;

        // Report diagnostic
        var properties = ImmutableDictionary<string, string?>.Empty.Add(TokenNameProperty, tokenParameter.Name);
        var diagnostic = Diagnostic.Create(Rule, loopKeyword.GetLocation(), properties, tokenParameter.Name);
        context.ReportDiagnostic(diagnostic);
    }

    /// <summary>
    /// Finds the CancellationToken parameter from the containing method or local function.
    /// Walks up the syntax tree to find the nearest scope with a CancellationToken parameter.
    /// </summary>
    private static IParameterSymbol? FindContainingCancellationTokenParameter(SyntaxNode node, SemanticModel semanticModel)
    {
        var current = node.Parent;
        while (current != null)
        {
            if (current is LocalFunctionStatementSyntax localFunction)
            {
                var localFunctionSymbol = semanticModel.GetDeclaredSymbol(localFunction) as IMethodSymbol;
                var tokenParam = CancellationTokenHelpers.FindCancellationTokenParameter(localFunctionSymbol);
                if (tokenParam != null)
                    return tokenParam;
                // Continue walking up to find outer scope's token if local function doesn't have one
            }
            else if (current is MethodDeclarationSyntax methodDeclaration)
            {
                var methodSymbol = semanticModel.GetDeclaredSymbol(methodDeclaration) as IMethodSymbol;
                return CancellationTokenHelpers.FindCancellationTokenParameter(methodSymbol);
            }
            else if (current is LambdaExpressionSyntax lambda)
            {
                var lambdaSymbol = semanticModel.GetSymbolInfo(lambda).Symbol as IMethodSymbol;
                var tokenParam = CancellationTokenHelpers.FindCancellationTokenParameter(lambdaSymbol);
                if (tokenParam != null)
                    return tokenParam;
                // Continue walking up to find outer scope's token if lambda doesn't have one
            }

            current = current.Parent;
        }

        return null;
    }

    /// <summary>
    /// Checks if the loop body contains a cancellation check (ThrowIfCancellationRequested or IsCancellationRequested).
    /// </summary>
    private static bool HasCancellationCheck(StatementSyntax loopBody, string tokenName)
    {
        if (loopBody == null)
            return false;

        // Look for ThrowIfCancellationRequested() calls or IsCancellationRequested property access
        var descendants = loopBody.DescendantNodesAndSelf();

        foreach (var node in descendants)
        {
            // Check for ThrowIfCancellationRequested() call
            if (node is InvocationExpressionSyntax invocation)
            {
                if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
                {
                    if (memberAccess.Name.Identifier.Text == "ThrowIfCancellationRequested")
                    {
                        // Verify it's on the correct token (or any CancellationToken)
                        var expressionText = memberAccess.Expression.ToString();
                        if (expressionText == tokenName || IsCancellationTokenExpression(expressionText))
                            return true;
                    }
                }
            }

            // Check for IsCancellationRequested property access
            if (node is MemberAccessExpressionSyntax propAccess)
            {
                if (propAccess.Name.Identifier.Text == "IsCancellationRequested")
                {
                    var expressionText = propAccess.Expression.ToString();
                    if (expressionText == tokenName || IsCancellationTokenExpression(expressionText))
                        return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Checks if the expression looks like a CancellationToken (common naming patterns).
    /// </summary>
    private static bool IsCancellationTokenExpression(string expression)
    {
        // Common token naming patterns
        var lowerExpression = expression.ToLowerInvariant();
        return lowerExpression.Contains("cancellation") ||
               lowerExpression.Contains("token") ||
               lowerExpression == "ct";
    }
}
