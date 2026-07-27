// =============================================================================
// CC030: Avoid blocking Process.WaitForExit() in async code
// =============================================================================
//
// WHY THIS MATTERS:
// Process.WaitForExit() blocks the calling thread until the child process ends.
// Unlike most blocking calls the wait is unbounded and depends on a program
// outside your control, so a hung child pins a thread-pool thread indefinitely —
// and no cancellation, shutdown signal, or request abort can reclaim it.
// .NET 5 added WaitForExitAsync(CancellationToken), which yields the thread and
// can be cancelled. CC002 cannot catch this: the async form is a differently
// named method, not an overload.
//
// THE RULE:
// - Flags a parameterless WaitForExit() on a System.Diagnostics.Process inside
//   async code, when the target framework provides WaitForExitAsync.
// - The WaitForExit(int) timeout overload is NOT flagged: it returns bool and
//   WaitForExitAsync takes only a token, so no rewrite preserves its meaning.
// =============================================================================

using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace CancelCop.Sample;

/// <summary>
/// Demonstrates CC030: avoid blocking Process.WaitForExit() in async code.
/// </summary>
public class CC030_BlockingProcessWait
{
    // VIOLATION (CC030 warns here)
    public async Task RunToolBad(Process process)
    {
        process.WaitForExit();
        await Task.Yield();
    }

    // FIXED — yields the thread and honours cancellation
    public async Task RunToolGood(Process process, CancellationToken cancellationToken)
    {
        await process.WaitForExitAsync(cancellationToken);
    }

    // CLEAN — the timeout overload has no async counterpart of the same shape
    public async Task<bool> RunToolWithTimeout(Process process)
    {
        var exited = process.WaitForExit(5000);
        await Task.Yield();
        return exited;
    }

    // CLEAN — synchronous method, nothing to await
    public void RunToolSync(Process process)
    {
        process.WaitForExit();
    }
}
