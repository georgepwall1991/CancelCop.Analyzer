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
    private static readonly LocalizableString MessageFormat =
        "Blocking 'WaitForExit()' in async code; use 'await WaitForExitAsync(cancellationToken)'";
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

        var tokenParameter = CancellationTokenHelpers.FindEnclosingCancellationTokenParameter(
            invocation,
            context.SemanticModel
        );
        if (tokenParameter != null)
            properties = properties.Add(TokenNameProperty, tokenParameter.Name);

        // Finding the framework method on Process proves the API exists, not that the rewritten call
        // reaches it: a subclass may hide WaitForExitAsync with an unusable member, and the fix would
        // then await something that is not awaitable. Bind the call the fixer would emit and stay
        // quiet unless it resolves to the framework method this diagnostic is premised on. This runs
        // for null-conditional calls too, which get no fix but still make the claim.
        if (
            !ResolvesToTheFrameworkCounterpart(
                context,
                invocation,
                tokenParameter?.Name,
                asyncCounterpart!
            )
        )
            return;

        context.ReportDiagnostic(Diagnostic.Create(Rule, invokedName.GetLocation(), properties));
    }

    /// <summary>
    /// Returns <c>true</c> when a call to <c>WaitForExitAsync</c> in this position resolves to
    /// <paramref name="asyncCounterpart"/>, the framework method the diagnostic is premised on.
    /// </summary>
    /// <remarks>
    /// A null-conditional call has no rewritable member access, so its receiver is lifted out of the
    /// enclosing conditional access and bound directly. No fix is offered for that shape, but the
    /// diagnostic still asserts an async alternative exists and that claim has to hold.
    /// </remarks>
    private static bool ResolvesToTheFrameworkCounterpart(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation,
        string? tokenName,
        IMethodSymbol asyncCounterpart
    )
    {
        var speculative = CancellationTokenHelpers.BuildRenamedInvocation(
            invocation,
            "WaitForExitAsync",
            tokenName
        );

        if (speculative is null)
        {
            var receiver = invocation
                .Ancestors()
                .OfType<ConditionalAccessExpressionSyntax>()
                .FirstOrDefault()
                ?.Expression;
            if (receiver is null)
                return false;

            speculative = invocation
                .WithExpression(
                    SyntaxFactory.MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        receiver,
                        SyntaxFactory.IdentifierName("WaitForExitAsync")
                    )
                )
                .WithArgumentList(
                    tokenName is null
                        ? invocation.ArgumentList
                        : invocation.ArgumentList.AddArguments(
                            SyntaxFactory.Argument(
                                CancellationTokenHelpers.IdentifierNameFor(tokenName)
                            )
                        )
                );
        }

        return CancellationTokenHelpers.SpeculativelyBindsTo(
            context.SemanticModel,
            invocation.SpanStart,
            speculative,
            asyncCounterpart
        );
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
