// =============================================================================
// CC029: Timeout CancellationTokenSource should link the in-scope token
// =============================================================================
//
// WHY THIS MATTERS:
// Adding a timeout with `new CancellationTokenSource(TimeSpan)` or
// `cts.CancelAfter(...)` on a stand-alone source silently drops any ambient
// parent token (request abort, host stopping token, caller cancellation). The
// operation then continues after the parent is cancelled until the timeout
// alone fires — a common ASP.NET / worker bug.
//
// THE RULE:
// - Flags timeout constructors (TimeSpan / int) when a token is in scope.
// - Flags CancelAfter on a local created with parameterless
//   `new CancellationTokenSource()` when a token is in scope.
// - Code fix: CreateLinkedTokenSource(parent) + CancelAfter(delay).
// =============================================================================

using System;
using System.Threading;
using System.Threading.Tasks;

namespace CancelCop.Sample;

/// <summary>
/// Demonstrates CC029: timeout CTS should link the in-scope token.
/// </summary>
public class CC029_LinkedTimeoutTokenSource
{
    private Task DoAsync(CancellationToken token) => Task.CompletedTask;

    // VIOLATION (CC029 warns here)
    public async Task RunBad(CancellationToken cancellationToken)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await DoAsync(cts.Token);
    }

    // FIXED
    public async Task RunGood(CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(30));
        await DoAsync(cts.Token);
    }
}
