// =============================================================================
// CC022: Prefer await CancelAsync() over Cancel() in async code
// =============================================================================
//
// WHY THIS MATTERS:
// CancellationTokenSource.Cancel() runs every registered callback synchronously
// on the calling thread, so a slow callback blocks the canceller. .NET 8 added
// CancelAsync(), which schedules the callbacks; in async code await it instead.
//
// THE RULE (Info):
// - Flags a parameterless Cancel() on a CancellationTokenSource, inside async code.
// - Code fix rewrites it to `await cts.CancelAsync()`.
// =============================================================================

using System.Threading;
using System.Threading.Tasks;

namespace CancelCop.Sample;

/// <summary>
/// Demonstrates CC022: prefer await CancelAsync() over Cancel() in async code.
/// </summary>
public class CC022_PreferCancelAsync
{
    // VIOLATION (CC022 suggests here)
    public async Task StopBad(CancellationTokenSource cts)
    {
        cts.Cancel();
        await Task.Yield();
    }

    // FIXED
    public async Task StopGood(CancellationTokenSource cts)
    {
        await cts.CancelAsync();
    }
}
