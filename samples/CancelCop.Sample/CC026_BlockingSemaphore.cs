// =============================================================================
// CC026: Avoid SemaphoreSlim.Wait() in async code
// =============================================================================
//
// WHY THIS MATTERS:
// SemaphoreSlim.Wait() blocks the calling thread until the semaphore is entered.
// In async code that ties up a thread-pool thread and is a classic deadlock
// source under a synchronization context. Use await WaitAsync() instead.
//
// THE RULE:
// - Flags a SemaphoreSlim.Wait (any overload) inside async code.
// - Code fix rewrites it to `await gate.WaitAsync(token)`, flowing the in-scope
//   token when one is available.
// =============================================================================

using System.Threading;
using System.Threading.Tasks;

namespace CancelCop.Sample;

/// <summary>
/// Demonstrates CC026: avoid SemaphoreSlim.Wait() in async code.
/// </summary>
public class CC026_BlockingSemaphore
{
    // VIOLATION (CC026 warns here)
    public async Task RunBad(SemaphoreSlim gate, CancellationToken ct)
    {
        gate.Wait();
        await Task.Yield();
    }

    // FIXED
    public async Task RunGood(SemaphoreSlim gate, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
    }
}
