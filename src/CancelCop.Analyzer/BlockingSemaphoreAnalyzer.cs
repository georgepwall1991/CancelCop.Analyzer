using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

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
/// <b>What it detects:</b> a potentially blocking <c>Wait(...)</c> call on a
/// <c>System.Threading.SemaphoreSlim</c> inside an <c>async</c> method, local function, lambda, or
/// anonymous method. The guaranteed non-blocking <c>Wait(0)</c> try-enter form is excluded.
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

    /// <summary>
    /// Property key set when the diagnostic is correct but inserting an <c>await</c> here would not
    /// compile, so the code fix must not offer a rewrite.
    /// </summary>
    public const string NoFixProperty = "NoFix";

    private static readonly LocalizableString Title = "Avoid SemaphoreSlim.Wait() in async code";
    private static readonly LocalizableString MessageFormat = "SemaphoreSlim.Wait blocks the thread in async code; use 'await WaitAsync(...)'";
    private static readonly LocalizableString Description = "SemaphoreSlim.Wait() blocks the calling thread; in async code use await WaitAsync().";
    private const string Category = "Usage";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: Description,
        helpLinkUri: DiagnosticHelp.LinkUri);

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
        var memberName = invocation.Expression switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name,
            MemberBindingExpressionSyntax memberBinding => memberBinding.Name,
            _ => null,
        };
        if (memberName is null)
            return;
        if (memberName.Identifier.Text != "Wait")
            return;

        if (context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is not IMethodSymbol method)
            return;

        // Potentially blocking Wait overloads should become WaitAsync; a constant zero-millisecond
        // timeout is the guaranteed non-blocking try-enter form and should remain synchronous.
        if (method.Name != "Wait")
            return;
        if (method.ContainingType?.Name != "SemaphoreSlim" ||
            method.ContainingType.ContainingNamespace?.ToDisplayString() != "System.Threading")
            return;

        if (
            CancellationTokenHelpers.HasProvablyZeroTimeout(
                invocation,
                context.SemanticModel,
                context.CancellationToken
            )
        )
            return;

        if (!CancellationTokenHelpers.IsInAsyncFunction(invocation))
            return;

        var token = CancellationTokenHelpers.FindEnclosingCancellationToken(
            invocation, context.SemanticModel);

        var properties = ImmutableDictionary<string, string?>.Empty;
        if (token != null)
            properties = properties.Add(TokenNameProperty, token.ExpressionText);

        // The call is blocking either way, but where an inserted await would not compile — a lock
        // body, an exception filter, an unsafe context, most query clauses, or across a ref-like
        // lifetime — the diagnostic is reported without a fix.
        if (CancellationTokenHelpers.AwaitInsertionIsUnsafe(context.SemanticModel, invocation))
            properties = properties.Add(
                NoFixProperty,
                CancellationTokenHelpers.AwaitUnsafeReason
            );

        context.ReportDiagnostic(Diagnostic.Create(Rule, memberName.GetLocation(), properties));
    }
}
