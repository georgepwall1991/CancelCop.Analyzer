// =============================================================================
// CC019: Broad catch swallows OperationCanceledException
// =============================================================================
//
// WHY THIS MATTERS:
// OperationCanceledException is how cooperative cancellation unwinds. A
// catch-all or catch (Exception) over awaited work that does not rethrow treats
// a cancelled operation as a generic failure (or silently succeeds), so callers
// awaiting the cancellation never see it. Reported as Info.
//
// THE RULE:
// - Flags a catch-all / catch (Exception) with no `when` filter, over a try that
//   contains an await, whose body never rethrows.
// - The fix is a `when` filter excluding cancellation, or rethrowing it.
// =============================================================================

using System;
using System.Threading;
using System.Threading.Tasks;

namespace CancelCop.Sample;

/// <summary>
/// Demonstrates CC019: a broad catch that swallows cancellation.
/// </summary>
public class CC019_SwallowedCancellation
{
    private Task WorkAsync(CancellationToken token) => Task.CompletedTask;

    // VIOLATION (CC019 suggests here)
    public async Task RunBad(CancellationToken token)
    {
        try { await WorkAsync(token); }
        catch (Exception) { }
    }

    // FIXED - cancellation is allowed to propagate
    public async Task RunGood(CancellationToken token)
    {
        try { await WorkAsync(token); }
        catch (Exception ex) when (ex is not OperationCanceledException) { _ = ex; }
    }
}
