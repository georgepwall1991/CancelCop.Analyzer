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
/// Only analyzes loops in scopes with a CancellationToken parameter available: methods,
/// constructors, local functions, lambdas, and instance members of C# 12 primary-constructor
/// types whose primary constructor declares a token.
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
        AnalyzeLoop(context, forStatement, forStatement.Statement, forStatement.ForKeyword, forStatement.Condition);
    }

    private void AnalyzeForEachStatement(SyntaxNodeAnalysisContext context)
    {
        var foreachStatement = (ForEachStatementSyntax)context.Node;
        AnalyzeLoop(context, foreachStatement, foreachStatement.Statement, foreachStatement.ForEachKeyword, condition: null);
    }

    private void AnalyzeWhileStatement(SyntaxNodeAnalysisContext context)
    {
        var whileStatement = (WhileStatementSyntax)context.Node;
        AnalyzeLoop(context, whileStatement, whileStatement.Statement, whileStatement.WhileKeyword, whileStatement.Condition);
    }

    private void AnalyzeDoStatement(SyntaxNodeAnalysisContext context)
    {
        var doStatement = (DoStatementSyntax)context.Node;
        AnalyzeLoop(context, doStatement, doStatement.Statement, doStatement.DoKeyword, doStatement.Condition);
    }

    private void AnalyzeLoop(SyntaxNodeAnalysisContext context, SyntaxNode loopNode, StatementSyntax loopBody, SyntaxToken loopKeyword, ExpressionSyntax? condition)
    {
        // Find the containing method, local function, or lambda that has a CancellationToken parameter
        var tokenParameter = CancellationTokenHelpers.FindEnclosingCancellationTokenParameter(
            loopNode, context.SemanticModel);
        if (tokenParameter == null)
            return;

        // A cancellation check satisfies the rule whether it is in the loop body or the loop
        // condition — `while (!token.IsCancellationRequested)` and
        // `for (...; !token.IsCancellationRequested; ...)` are canonical cancellation-aware loops.
        if (HasCancellationCheck(loopBody, tokenParameter, context.SemanticModel) ||
            (condition != null && HasCancellationCheck(condition, tokenParameter, context.SemanticModel)))
            return;

        // Report diagnostic
        var properties = ImmutableDictionary<string, string?>.Empty.Add(TokenNameProperty, tokenParameter.Name);
        var diagnostic = Diagnostic.Create(Rule, loopKeyword.GetLocation(), properties, tokenParameter.Name);
        context.ReportDiagnostic(diagnostic);
    }

    /// <summary>
    /// Checks if <paramref name="loopBody"/> (a loop body or a loop condition) contains a
    /// cancellation check (ThrowIfCancellationRequested or IsCancellationRequested) on the specific
    /// in-scope token. The receiver is resolved through the semantic model and compared to
    /// <paramref name="tokenParameter"/> by symbol identity, so a look-alike named "...token" is
    /// rejected, a real token with a plain name is accepted, and a check on a different token does
    /// not satisfy the rule for the reported token.
    /// </summary>
    private static bool HasCancellationCheck(SyntaxNode loopBody, IParameterSymbol tokenParameter, SemanticModel semanticModel)
    {
        if (loopBody == null)
            return false;

        foreach (var node in loopBody.DescendantNodesAndSelf())
        {
            // ThrowIfCancellationRequested() invocation on the in-scope token.
            if (node is InvocationExpressionSyntax invocation &&
                invocation.Expression is MemberAccessExpressionSyntax call &&
                call.Name.Identifier.Text == "ThrowIfCancellationRequested" &&
                IsTokenReceiver(call.Expression, tokenParameter, semanticModel))
            {
                return true;
            }

            // IsCancellationRequested property access on the in-scope token.
            if (node is MemberAccessExpressionSyntax propAccess &&
                propAccess.Name.Identifier.Text == "IsCancellationRequested" &&
                IsTokenReceiver(propAccess.Expression, tokenParameter, semanticModel))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns true when the expression resolves to the supplied token parameter symbol.
    /// </summary>
    private static bool IsTokenReceiver(ExpressionSyntax expression, IParameterSymbol tokenParameter, SemanticModel semanticModel)
    {
        var symbol = semanticModel.GetSymbolInfo(expression).Symbol;
        return symbol != null && SymbolEqualityComparer.Default.Equals(symbol, tokenParameter);
    }
}
