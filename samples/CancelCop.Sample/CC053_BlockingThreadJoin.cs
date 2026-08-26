// =============================================================================
// CC053: Avoid blocking Thread.Join in async code (analyzer-only)
// =============================================================================
//
// WHY THIS MATTERS:
// Joining a raw thread parks a pooled thread for an unbounded or
// timeout-bounded wait — a deadlock risk under a starving pool. Await the
// task that represents the work instead of joining the thread.
//
// ANALYZER-ONLY BY DESIGN:
// Verified against the net9/net10 reference packs: System.Threading.Thread
// declares only Join(), Join(int), and Join(TimeSpan), declares no TAP
// JoinAsync counterpart, and is sealed. Every diagnostic is reported without
// a rewrite.
// =============================================================================

using System.Threading;
using System.Threading.Tasks;

namespace CancelCop.Sample;

/// <summary>
/// Demonstrates CC053: blocking Thread.Join in async code (no fix by design).
/// </summary>
public class CC053_BlockingThreadJoin
{
    // VIOLATION (CC053 warns here) — parks a pooled thread until `worker` exits;
    // no rewrite exists because no framework JoinAsync is shipped yet.
    public async Task WaitBad(Thread worker)
    {
        worker.Join();
        await Task.Yield();
    }

    // PREFERRED — await a Task that represents the work instead of joining.
    public async Task WaitGood(Task workerTask)
    {
        await workerTask;
    }
}
