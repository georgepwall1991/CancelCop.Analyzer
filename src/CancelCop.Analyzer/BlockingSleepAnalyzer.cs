using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CancelCop.Analyzer;

/// <summary>
/// Analyzer that detects a blocking <c>Thread.Sleep</c> call inside an <c>async</c> function.
/// </summary>
/// <remarks>
/// <para>
/// <b>Rule ID:</b> CC013
/// </para>
/// <para>
/// <b>Why this matters:</b>
/// <c>Thread.Sleep</c> blocks the current thread. Inside an <c>async</c> method it ties up a thread
/// pool thread for the whole delay (risking pool starvation) and cannot be cancelled. The async
/// equivalent <c>await Task.Delay(delay, token)</c> yields the thread and observes cancellation.
/// </para>
/// <para>
/// <b>What it detects:</b> a call to <c>System.Threading.Thread.Sleep</c> lexically inside an
/// <c>async</c> method, local function, lambda, or anonymous method.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Violation:
/// public async Task RunAsync(CancellationToken ct)
/// {
///     Thread.Sleep(1000);                 // CC013
/// }
///
/// // Fixed:
/// public async Task RunAsync(CancellationToken ct)
/// {
///     await Task.Delay(1000, ct);
/// }
/// </code>
/// </example>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class BlockingSleepAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// The diagnostic ID for this analyzer rule.
    /// </summary>
    public const string DiagnosticId = "CC013";

    /// <summary>
    /// Property key used to pass the in-scope token parameter name (if any) to the code fix provider.
    /// </summary>
    public const string TokenNameProperty = "TokenName";

    private static readonly LocalizableString Title = "Avoid Thread.Sleep in async code";
    private static readonly LocalizableString MessageFormat = "Thread.Sleep blocks the thread in an async method; use 'await Task.Delay' instead";
    private static readonly LocalizableString Description = "Thread.Sleep blocks the calling thread and ignores cancellation. In async code use await Task.Delay(delay, token).";
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

        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        if (context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is not IMethodSymbol method)
            return;

        if (method.Name != "Sleep" ||
            method.ContainingType?.Name != "Thread" ||
            method.ContainingType.ContainingNamespace?.ToDisplayString() != "System.Threading")
            return;

        // Only flag inside async code: a blocking sleep in a synchronous method is a different
        // (and sometimes legitimate) decision.
        if (!CancellationTokenHelpers.IsInAsyncFunction(invocation))
            return;

        var tokenParameter = CancellationTokenHelpers.FindEnclosingCancellationTokenParameter(
            invocation, context.SemanticModel);

        var properties = ImmutableDictionary<string, string?>.Empty;
        if (tokenParameter != null)
            properties = properties.Add(TokenNameProperty, tokenParameter.Name);

        var diagnostic = Diagnostic.Create(Rule, invocation.GetLocation(), properties);
        context.ReportDiagnostic(diagnostic);
    }
}
