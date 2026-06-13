// =============================================================================
// CC015: Avoid blocking on async code (sync-over-async)
// =============================================================================
//
// WHY THIS MATTERS:
// Blocking on a task synchronously (.Result, .Wait(), .GetAwaiter().GetResult())
// ties up the thread, can deadlock when a synchronization context is present,
// and wraps cancellation in an AggregateException. In an async method the task
// should be awaited instead.
//
// THE RULE:
// - Flags .Result / .Wait() / .GetAwaiter().GetResult() on a task inside an
//   async method/local function/lambda.
// - Code fix rewrites the blocking call to `await`.
// =============================================================================

using System.Threading.Tasks;

namespace CancelCop.Sample;

/// <summary>
/// Demonstrates CC015: avoid blocking on async code.
/// </summary>
public class CC015_BlockingOnAsync
{
    private Task<int> GetValueAsync() => Task.FromResult(0);

    // VIOLATION (CC015 warns here)
    public async Task<int> RunBad()
    {
        await Task.Yield();
        return GetValueAsync().Result;
    }

    // FIXED
    public async Task<int> RunGood()
        => await GetValueAsync();
}
