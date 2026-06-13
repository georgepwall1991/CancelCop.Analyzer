using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CancelCop.Analyzer;

/// <summary>
/// Analyzer that suggests <c>await CancellationTokenSource.CancelAsync()</c> over the synchronous
/// <c>Cancel()</c> inside async code.
/// </summary>
/// <remarks>
/// <para>
/// <b>Rule ID:</b> CC022
/// </para>
/// <para>
/// <b>Why this matters:</b>
/// <c>CancellationTokenSource.Cancel()</c> runs every registered callback synchronously on the
/// thread that calls it, so a slow callback blocks the canceller (and an exception can surface in an
/// unexpected place). .NET 8 added <c>CancelAsync()</c>, which schedules the callbacks; in async
/// code it should be awaited instead. Reported as <b>Info</b> because <c>Cancel()</c> remains valid.
/// </para>
/// <para>
/// <b>What it detects:</b> a parameterless <c>Cancel()</c> call on a
/// <c>System.Threading.CancellationTokenSource</c> inside an <c>async</c> method, local function,
/// lambda, or anonymous method.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public async Task StopAsync(CancellationTokenSource cts)
/// {
///     cts.Cancel();              // CC022 -> await cts.CancelAsync();
/// }
/// </code>
/// </example>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class PreferCancelAsyncAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// The diagnostic ID for this analyzer rule.
    /// </summary>
    public const string DiagnosticId = "CC022";

    private static readonly LocalizableString Title = "Prefer CancelAsync over Cancel in async code";
    private static readonly LocalizableString MessageFormat = "Use 'await CancelAsync()' instead of 'Cancel()' in async code";
    private static readonly LocalizableString Description = "CancellationTokenSource.Cancel() runs callbacks synchronously; in async code prefer await CancelAsync().";
    private const string Category = "Usage";

    private static readonly DiagnosticDescriptor Rule = new(
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

        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            return;
        if (memberAccess.Name.Identifier.Text != "Cancel")
            return;

        if (context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is not IMethodSymbol method)
            return;

        // Only the parameterless Cancel() has a CancelAsync() counterpart.
        if (method.Name != "Cancel" || method.Parameters.Length != 0)
            return;
        if (method.ContainingType?.Name != "CancellationTokenSource" ||
            method.ContainingType.ContainingNamespace?.ToDisplayString() != "System.Threading")
            return;

        // The fix introduces an await, so it only applies in async code.
        if (!CancellationTokenHelpers.IsInAsyncFunction(invocation))
            return;

        context.ReportDiagnostic(Diagnostic.Create(Rule, memberAccess.Name.GetLocation()));
    }
}
