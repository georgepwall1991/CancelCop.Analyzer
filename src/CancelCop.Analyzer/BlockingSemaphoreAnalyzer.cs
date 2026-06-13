using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CancelCop.Analyzer;

/// <summary>
/// Analyzer that detects a synchronous <c>SemaphoreSlim.Wait()</c> inside async code.
/// </summary>
/// <remarks>
/// <para>
/// <b>Rule ID:</b> CC026
/// </para>
/// <para>
/// <b>Why this matters:</b>
/// <c>SemaphoreSlim.Wait()</c> blocks the calling thread until the semaphore is entered. In async
/// code that ties up a thread-pool thread and is a classic deadlock source under a synchronization
/// context. <c>SemaphoreSlim</c> exposes <c>WaitAsync()</c> (with a <c>CancellationToken</c>
/// overload) for exactly this case.
/// </para>
/// <para>
/// <b>What it detects:</b> a parameterless <c>Wait()</c> call on a
/// <c>System.Threading.SemaphoreSlim</c> inside an <c>async</c> method, local function, lambda, or
/// anonymous method.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public async Task RunAsync(SemaphoreSlim gate, CancellationToken ct)
/// {
///     gate.Wait();                 // CC026 -> await gate.WaitAsync(ct);
/// }
/// </code>
/// </example>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class BlockingSemaphoreAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// The diagnostic ID for this analyzer rule.
    /// </summary>
    public const string DiagnosticId = "CC026";

    /// <summary>
    /// Property key used to pass the in-scope token parameter name (if any) to the code fix provider.
    /// </summary>
    public const string TokenNameProperty = "TokenName";

    private static readonly LocalizableString Title = "Avoid SemaphoreSlim.Wait() in async code";
    private static readonly LocalizableString MessageFormat = "SemaphoreSlim.Wait() blocks the thread in async code; use 'await WaitAsync()'";
    private static readonly LocalizableString Description = "SemaphoreSlim.Wait() blocks the calling thread; in async code use await WaitAsync().";
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
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            return;
        if (memberAccess.Name.Identifier.Text != "Wait")
            return;

        if (context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is not IMethodSymbol method)
            return;

        // Only the parameterless Wait() maps cleanly to WaitAsync().
        if (method.Name != "Wait" || method.Parameters.Length != 0)
            return;
        if (method.ContainingType?.Name != "SemaphoreSlim" ||
            method.ContainingType.ContainingNamespace?.ToDisplayString() != "System.Threading")
            return;

        if (!CancellationTokenHelpers.IsInAsyncFunction(invocation))
            return;

        var tokenParameter = CancellationTokenHelpers.FindEnclosingCancellationTokenParameter(
            invocation, context.SemanticModel);

        var properties = ImmutableDictionary<string, string?>.Empty;
        if (tokenParameter != null)
            properties = properties.Add(TokenNameProperty, tokenParameter.Name);

        context.ReportDiagnostic(Diagnostic.Create(Rule, memberAccess.Name.GetLocation(), properties));
    }
}
