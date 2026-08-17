using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CancelCop.Analyzer;

/// <summary>
/// Analyzer that detects a blocking synchronization primitive — <c>ManualResetEventSlim.Wait</c>,
/// <c>CountdownEvent.Wait</c>, <c>WaitHandle.WaitOne</c>, <c>Monitor.Wait</c>,
/// <c>Thread.Join</c>, or <c>ReaderWriterLockSlim.Enter*Lock</c>/<c>TryEnter*Lock</c> —
/// inside async code.
/// </summary>
/// <remarks>
/// <para>
/// <b>Rule ID:</b> CC031
/// </para>
/// <para>
/// <b>Why this matters:</b>
/// These calls park a thread-pool thread until another thread signals. In async code that is the
/// worst kind of blocking: the wait is unbounded, it consumes a pooled thread that the continuations
/// it is waiting for may themselves need, and under load it can deadlock the pool outright. None of
/// them observes a <c>CancellationToken</c> by default either, so shutdown and request abort cannot
/// reclaim the thread.
/// </para>
/// <para>
/// <b>Why there is no code fix:</b> unlike the rest of the blocking-in-async family
/// (CC013, CC015, CC026, CC028, CC030), these primitives have <i>no</i> <c>…Async</c> counterpart in
/// .NET. Resolving the finding means changing the design — a <c>SemaphoreSlim</c> awaited with
/// <c>WaitAsync</c>, a <c>TaskCompletionSource</c> signalled instead of an event, or awaiting the
/// task rather than joining the thread. That is a judgement call, so CC031 is analyzer-only by
/// design, like CC017, CC020, CC024, and CC027.
/// </para>
/// <para>
/// <b>Conservative by design:</b> a provably zero timeout is an immediate probe rather than a wait
/// and is excluded, matching CC013/CC015/CC026. <c>SemaphoreSlim.Wait</c> is left to CC026, which
/// owns it and can offer a real fix. Synchronous methods, and synchronous lambdas inside async
/// methods, stay quiet.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public async Task RunAsync(ManualResetEventSlim ready)
/// {
///     ready.Wait();          // CC031: parks a pool thread; signal with a TaskCompletionSource
///     await Task.Yield();
/// }
/// </code>
/// </example>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class BlockingSyncPrimitiveAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// The diagnostic ID for this analyzer rule.
    /// </summary>
    public const string DiagnosticId = "CC031";

    /// <summary>
    /// The blocking members, keyed by the simple name of the <c>System.Threading</c> type that
    /// declares them. Each name is resolved to a real symbol per compilation and matched against the
    /// invoked method's original definition, so an override or a derived receiver
    /// (<c>ManualResetEvent.WaitOne</c>, declared on <c>WaitHandle</c>) still resolves here.
    /// </summary>
    private static readonly ImmutableDictionary<
        string,
        ImmutableHashSet<string>
    > BlockingMembersByType = ImmutableDictionary.CreateRange(
        new[]
        {
            new KeyValuePair<string, ImmutableHashSet<string>>(
                "ManualResetEventSlim",
                ImmutableHashSet.Create("Wait")
            ),
            new KeyValuePair<string, ImmutableHashSet<string>>(
                "CountdownEvent",
                ImmutableHashSet.Create("Wait")
            ),
            new KeyValuePair<string, ImmutableHashSet<string>>(
                "WaitHandle",
                ImmutableHashSet.Create("WaitOne", "WaitAll", "WaitAny")
            ),
            new KeyValuePair<string, ImmutableHashSet<string>>(
                "Monitor",
                ImmutableHashSet.Create("Wait")
            ),
            new KeyValuePair<string, ImmutableHashSet<string>>(
                "Thread",
                ImmutableHashSet.Create("Join")
            ),
            new KeyValuePair<string, ImmutableHashSet<string>>(
                "ReaderWriterLockSlim",
                ImmutableHashSet.Create(
                    "EnterReadLock",
                    "EnterWriteLock",
                    "EnterUpgradeableReadLock",
                    "TryEnterReadLock",
                    "TryEnterWriteLock",
                    "TryEnterUpgradeableReadLock"
                )
            ),
        }
    );

    /// <summary>
    /// Declaring types whose members can block even with a zero timeout, so the probe exclusion must
    /// not apply. <c>Monitor.Wait</c> releases the monitor and must reacquire it before returning.
    /// </summary>
    private static readonly ImmutableHashSet<string> ZeroTimeoutStillBlocks =
        ImmutableHashSet.Create("Monitor");

    private static readonly LocalizableString Title =
        "Avoid blocking synchronization primitives in async code";
    private static readonly LocalizableString MessageFormat =
        "'{0}' blocks a pooled thread in async code; signal asynchronously instead";
    private static readonly LocalizableString Description =
        "Blocking synchronization primitives park a thread-pool thread until another thread signals; in async code prefer an awaitable signal such as SemaphoreSlim.WaitAsync or a TaskCompletionSource.";
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

        // Resolve the framework types once per compilation and compare symbols, rather than matching
        // on name and namespace: a consumer may legally declare its own `System.Threading.Thread<T>`,
        // which shares both and is unrelated to the primitive this rule is about.
        context.RegisterCompilationStartAction(start =>
        {
            var blockingMembers = new Dictionary<INamedTypeSymbol, ImmutableHashSet<string>>(
                SymbolEqualityComparer.Default
            );

            foreach (var entry in BlockingMembersByType)
            {
                var type = start.Compilation.GetTypeByMetadataName("System.Threading." + entry.Key);
                if (type != null)
                    blockingMembers[type] = entry.Value;
            }

            if (blockingMembers.Count == 0)
                return;

            start.RegisterSyntaxNodeAction(
                nodeContext => AnalyzeInvocation(nodeContext, blockingMembers),
                SyntaxKind.InvocationExpression
            );
        });
    }

    private static void AnalyzeInvocation(
        SyntaxNodeAnalysisContext context,
        Dictionary<INamedTypeSymbol, ImmutableHashSet<string>> blockingMembersByType
    )
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        var invokedName = invocation.Expression switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name,
            MemberBindingExpressionSyntax memberBinding => memberBinding.Name,
            IdentifierNameSyntax identifier => identifier,
            _ => null,
        };
        if (invokedName is null)
            return;

        if (
            context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol
            is not IMethodSymbol method
        )
            return;

        // Walk to the original definition so an override, or a call through a derived receiver,
        // still resolves to the framework type that declares the blocking member.
        var definition = method;
        while (definition.OverriddenMethod != null)
            definition = definition.OverriddenMethod;

        var declaringType = definition.ContainingType;
        if (
            declaringType is null
            || !blockingMembersByType.TryGetValue(declaringType, out var blockingMembers)
            || !blockingMembers.Contains(definition.Name)
        )
            return;

        if (!CancellationTokenHelpers.IsInAsyncFunction(invocation))
            return;

        // A provably zero timeout is an immediate probe, not a wait — the same exclusion CC013,
        // CC015, and CC026 make. Monitor.Wait is the exception: a zero timeout only ends the
        // condition wait, and the call still cannot return until it reacquires the monitor, which
        // can block behind another thread.
        if (
            !ZeroTimeoutStillBlocks.Contains(declaringType.Name)
            && CancellationTokenHelpers.HasProvablyZeroTimeout(
                invocation,
                context.SemanticModel,
                context.CancellationToken
            )
        )
            return;

        context.ReportDiagnostic(
            Diagnostic.Create(
                Rule,
                invokedName.GetLocation(),
                $"{declaringType.Name}.{definition.Name}"
            )
        );
    }
}
