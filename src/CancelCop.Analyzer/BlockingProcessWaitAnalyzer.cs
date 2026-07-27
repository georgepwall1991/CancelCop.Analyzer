using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CancelCop.Analyzer;

/// <summary>
/// Analyzer that detects a blocking <c>Process.WaitForExit()</c> inside async code, where
/// <c>await WaitForExitAsync(cancellationToken)</c> is available.
/// </summary>
/// <remarks>
/// <para>
/// <b>Rule ID:</b> CC030
/// </para>
/// <para>
/// <b>Why this matters:</b>
/// <c>WaitForExit()</c> blocks the calling thread until the child process ends. Unlike most blocking
/// calls the wait is unbounded and depends on a program outside your control, so a hung child pins a
/// thread-pool thread indefinitely — and no cancellation, shutdown signal, or request abort can
/// reclaim it. .NET 5 added <c>WaitForExitAsync(CancellationToken)</c>, which both yields the thread
/// and can be cancelled. Joins the blocking-in-async family alongside CC013
/// (<c>Thread.Sleep</c>), CC015 (<c>Task.Wait</c>/<c>.Result</c>), CC026
/// (<c>SemaphoreSlim.Wait</c>), and CC028 (blocking <c>System.IO</c>).
/// </para>
/// <para>
/// <b>What it detects:</b> a parameterless <c>WaitForExit()</c> call (including null-conditional
/// calls) on a <c>System.Diagnostics.Process</c> inside an <c>async</c> method, local function,
/// lambda, or anonymous method, when the target framework provides
/// <c>WaitForExitAsync(CancellationToken)</c>.
/// </para>
/// <para>
/// <b>Conservative by design:</b> the <c>WaitForExit(int)</c> timeout overload returns <c>bool</c>
/// and has no counterpart of that shape — <c>WaitForExitAsync</c> takes only a token — so it is not
/// flagged. Nor is a synchronous lambda inside an async method, where no <c>await</c> can be
/// inserted.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public async Task RunAsync(Process process, CancellationToken cancellationToken)
/// {
///     process.WaitForExit();   // CC030 -> await process.WaitForExitAsync(cancellationToken)
/// }
/// </code>
/// </example>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class BlockingProcessWaitAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// The diagnostic ID for this analyzer rule.
    /// </summary>
    public const string DiagnosticId = "CC030";

    /// <summary>
    /// Property key used to pass the in-scope token parameter name (if any) to the code fix provider.
    /// </summary>
    public const string TokenNameProperty = "TokenName";

    /// <summary>
    /// Property key set when the diagnostic is correct but no safe rewrite exists, so the code fix
    /// must not offer one.
    /// </summary>
    public const string NoFixProperty = "NoFix";

    private static readonly LocalizableString Title =
        "Avoid blocking Process.WaitForExit() in async code";
    // Token-neutral: the fix passes an in-scope token when there is one and calls the parameterless
    // form when there is not, so naming 'cancellationToken' here would hand anyone reading the
    // warning without applying the fix an identifier that may not exist.
    private static readonly LocalizableString MessageFormat =
        "Blocking 'WaitForExit()' in async code; await 'WaitForExitAsync' instead";
    private static readonly LocalizableString Description =
        "Process.WaitForExit() blocks the thread for an unbounded wait on an external process; in async code use WaitForExitAsync, which accepts a CancellationToken.";
    private const string Category = "Usage";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: Description,
        helpLinkUri: DiagnosticHelp.LinkUri
    );

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        var invokedName = invocation.Expression switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name,
            MemberBindingExpressionSyntax memberBinding => memberBinding.Name,
            // An inherited call written without `this.` inside a Process subclass.
            IdentifierNameSyntax identifier => identifier,
            _ => null,
        };
        if (invokedName is null || invokedName.Identifier.Text != "WaitForExit")
            return;

        if (
            context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol
            is not IMethodSymbol method
        )
            return;

        // Only the parameterless overload has a WaitForExitAsync counterpart; WaitForExit(int)
        // returns bool and no async form takes a timeout.
        if (method.Name != "WaitForExit" || method.Parameters.Length != 0)
            return;

        var containingType = method.ContainingType;
        if (
            containingType?.Name != "Process"
            || containingType.ContainingNamespace?.ToDisplayString() != "System.Diagnostics"
        )
            return;

        // Only claim an async alternative exists when the target framework actually has one:
        // WaitForExitAsync arrived in .NET 5, so .NET Framework consumers stay quiet rather than
        // receive a suggestion that cannot compile.
        if (!TryGetWaitForExitAsync(containingType, out var asyncCounterpart))
            return;

        // The fix inserts an await, so it only applies in async code.
        if (!CancellationTokenHelpers.IsInAsyncFunction(invocation))
            return;

        var properties = ImmutableDictionary<string, string?>.Empty;

        // The call is genuinely blocking either way, but where `await` is illegal (a lock body, an
        // exception filter, an unsafe context, most query clauses) the rewrite would not compile, so
        // the diagnostic is reported without a fix.
        if (CancellationTokenHelpers.AwaitIsForbiddenHere(invocation))
            properties = properties.Add(NoFixProperty, "await-not-allowed-here");
        else if (
            CancellationTokenHelpers.AwaitWouldSpanRefLikeLocal(context.SemanticModel, invocation)
        )
            properties = properties.Add(NoFixProperty, "await-would-span-ref-like-local");

        var tokenParameter = CancellationTokenHelpers.FindEnclosingCancellationTokenParameter(
            invocation,
            context.SemanticModel
        );

        // Finding the framework method on Process proves the API exists, not that the rewritten call
        // reaches it: a subclass may hide WaitForExitAsync with an unusable member, and the fix would
        // then await something that is not awaitable. Bind the call the fixer would emit and stay
        // quiet unless it resolves to the framework method this diagnostic is premised on. This runs
        // for null-conditional calls too, which get no fix but still make the claim.
        var tokenName = tokenParameter?.Name;
        if (
            tokenName != null
            && !ResolvesToTheFrameworkCounterpart(context, invocation, method, tokenName, asyncCounterpart!)
        )
        {
            // The token is in scope by symbol but its *name* may be shadowed here — a nested function
            // can bind that identifier to something else entirely. That makes the token unusable in
            // generated source, not the blocking call acceptable, so fall back to the parameterless
            // form rather than dropping the diagnostic.
            tokenName = null;
        }

        if (!ResolvesToTheFrameworkCounterpart(context, invocation, method, tokenName, asyncCounterpart!))
            return;

        if (tokenName != null)
            properties = properties.Add(TokenNameProperty, tokenName);

        context.ReportDiagnostic(Diagnostic.Create(Rule, invokedName.GetLocation(), properties));
    }

    /// <summary>
    /// Returns <c>true</c> when a call to <c>WaitForExitAsync</c> here would reach
    /// <paramref name="asyncCounterpart"/>, the framework method the diagnostic is premised on.
    /// </summary>
    /// <remarks>
    /// Where the rewritten call can be constructed, the compiler answers: it accounts for hiding,
    /// accessibility, and implicit conversions exactly as the user's build will. Conditional-access
    /// shapes cannot be rebuilt as a plain invocation — their receiver is spread across the
    /// <c>?.</c> chain — and get no fix anyway, so those fall back to walking the receiver's type for
    /// the member a call of this arity would select.
    /// </remarks>
    private static bool ResolvesToTheFrameworkCounterpart(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation,
        IMethodSymbol method,
        string? tokenName,
        IMethodSymbol asyncCounterpart
    )
    {
        var speculative = CancellationTokenHelpers.BuildRenamedInvocation(
            invocation,
            "WaitForExitAsync",
            tokenName
        );

        if (speculative != null)
        {
            return CancellationTokenHelpers.SpeculativelyBindsTo(
                context.SemanticModel,
                invocation.SpanStart,
                speculative,
                asyncCounterpart
            );
        }

        return ReachesCounterpart(
            method.ReceiverType,
            tokenName is null ? 0 : 1,
            asyncCounterpart
        );
    }

    /// <summary>
    /// Walks <paramref name="receiverType"/> and its bases for the first <c>WaitForExitAsync</c> a
    /// call with <paramref name="arity"/> arguments could select, and returns <c>true</c> when that
    /// is <paramref name="asyncCounterpart"/>.
    /// </summary>
    /// <remarks>
    /// C# hides methods by signature rather than by name, so a subclass declaring
    /// <c>new int WaitForExitAsync(CancellationToken)</c> does not hide the inherited parameterless
    /// form — a tokenless call still reaches the framework method.
    /// </remarks>
    private static bool ReachesCounterpart(
        ITypeSymbol? receiverType,
        int arity,
        IMethodSymbol asyncCounterpart
    )
    {
        for (var current = receiverType; current != null; current = current.BaseType)
        {
            foreach (var member in current.GetMembers("WaitForExitAsync").OfType<IMethodSymbol>())
            {
                var required = member.Parameters.Count(p => !p.IsOptional);
                if (arity < required || arity > member.Parameters.Length)
                    continue;

                return SymbolEqualityComparer.Default.Equals(
                    member.OriginalDefinition,
                    asyncCounterpart.OriginalDefinition
                );
            }
        }

        return false;
    }

    /// <summary>
    /// Returns <c>true</c> when <c>Process</c> exposes the public instance
    /// <c>WaitForExitAsync(CancellationToken)</c> returning <c>Task</c>.
    /// </summary>
    private static bool TryGetWaitForExitAsync(
        INamedTypeSymbol processType,
        out IMethodSymbol? asyncCounterpart
    )
    {
        asyncCounterpart = null;

        foreach (var member in processType.GetMembers("WaitForExitAsync"))
        {
            if (
                member
                    is IMethodSymbol
                    {
                        IsStatic: false,
                        DeclaredAccessibility: Accessibility.Public,
                        Parameters.Length: 1,
                        ReturnType.Name: "Task",
                    } candidate
                && candidate.ReturnType.ContainingNamespace?.ToDisplayString()
                    == "System.Threading.Tasks"
                && CancellationTokenHelpers.IsCancellationToken(candidate.Parameters[0].Type)
            )
            {
                asyncCounterpart = candidate;
                return true;
            }
        }

        return false;
    }
}
