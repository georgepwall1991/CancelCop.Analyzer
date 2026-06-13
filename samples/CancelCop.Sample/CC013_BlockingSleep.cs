// =============================================================================
// CC013: Avoid Thread.Sleep in async code
// =============================================================================
//
// WHY THIS MATTERS:
// Thread.Sleep blocks the calling thread. Inside an async method it ties up a
// thread-pool thread for the whole delay (risking pool starvation) and cannot
// be cancelled. The async equivalent `await Task.Delay(delay, token)` yields the
// thread and observes cancellation.
//
// THE RULE:
// - Flags Thread.Sleep lexically inside an async method/local function/lambda.
// - Code fix rewrites it to `await Task.Delay(delay, token)`.
// =============================================================================

using System.Threading;
using System.Threading.Tasks;

namespace CancelCop.Sample;

/// <summary>
/// Demonstrates CC013: avoid Thread.Sleep in async code.
/// </summary>
public class CC013_BlockingSleep
{
    // VIOLATION (CC013 warns here)
    public async Task RunBad(CancellationToken ct)
    {
        Thread.Sleep(1000);
        await Task.Yield();
    }

    // FIXED
    public async Task RunGood(CancellationToken ct)
    {
        await Task.Delay(1000, ct);
    }
}
