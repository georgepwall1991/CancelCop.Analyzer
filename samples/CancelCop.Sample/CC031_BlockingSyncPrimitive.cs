// =============================================================================
// CC031: Avoid blocking synchronization primitives in async code
// =============================================================================
//
// WHY THIS MATTERS:
// ManualResetEventSlim.Wait(), CountdownEvent.Wait(), WaitHandle.WaitOne(),
// Monitor.Wait(), Thread.Join(), and ReaderWriterLockSlim.Enter*Lock() park a
// thread-pool thread until another thread signals (or every conflicting holder
// exits). In async code that is the worst kind of blocking: the wait is
// unbounded, it consumes a pooled thread that the continuations it waits for may
// themselves need, and under load it can deadlock the pool outright. None of
// them observes a CancellationToken by default, so shutdown and request abort
// cannot reclaim the thread.
//
// WHY THERE IS NO CODE FIX:
// Unlike CC013/CC015/CC026/CC028/CC030, these primitives have no ...Async
// counterpart in .NET. Resolving the finding is a design change — a SemaphoreSlim
// awaited with WaitAsync, a TaskCompletionSource signalled instead of an event,
// or awaiting the task rather than joining the thread — so CC031 is
// analyzer-only, like CC017, CC020, CC024, and CC027.
//
// THE RULE:
// - Flags the listed members, matched through overrides to the framework type
//   that declares them, inside async code.
// - A provably zero timeout is an immediate probe, not a wait, and is excluded.
// - SemaphoreSlim.Wait belongs to CC026, which can offer a real fix.
// - ReaderWriterLockSlim is not a WaitHandle; Enter*Lock was a silent false
//   negative until v1.39.1.
// =============================================================================

using System.Threading;
using System.Threading.Tasks;

namespace CancelCop.Sample;

/// <summary>
/// Demonstrates CC031: avoid blocking synchronization primitives in async code.
/// </summary>
public class CC031_BlockingSyncPrimitive
{
    private readonly ManualResetEventSlim _ready = new();
    private readonly SemaphoreSlim _readyAsync = new(0, 1);
    private readonly ReaderWriterLockSlim _rwlock = new();

    // VIOLATION (CC031 warns here) — parks a pooled thread until another signals
    public async Task WaitForReadyBad()
    {
        _ready.Wait();
        await Task.Yield();
    }

    // FIXED — an awaitable signal yields the thread and honours cancellation
    public async Task WaitForReadyGood(CancellationToken cancellationToken)
    {
        await _readyAsync.WaitAsync(cancellationToken);
    }

    // VIOLATION (CC031 warns here too — the classic "block until cancelled" trap)
    public async Task WaitForCancellationBad(CancellationToken cancellationToken)
    {
        cancellationToken.WaitHandle.WaitOne();
        await Task.Yield();
    }

    // FIXED — await a task that completes on cancellation instead
    public async Task WaitForCancellationGood(CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.Infinite, cancellationToken);
    }

    // VIOLATION (CC031 warns here — ReaderWriterLockSlim is not a WaitHandle)
    public async Task ReadUnderLockBad()
    {
        _rwlock.EnterReadLock();
        try
        {
            await Task.Yield();
        }
        finally
        {
            _rwlock.ExitReadLock();
        }
    }

    // VIOLATION (CC031 warns here too — Timeout.Infinite is an unbounded enter)
    public async Task TryEnterWriteLockInfiniteBad()
    {
        if (_rwlock.TryEnterWriteLock(Timeout.Infinite))
        {
            try
            {
                await Task.Yield();
            }
            finally
            {
                _rwlock.ExitWriteLock();
            }
        }
    }

    // CLEAN — a zero timeout is an immediate probe, not a wait
    public async Task<bool> ProbeReady()
    {
        var ready = _ready.Wait(0);
        await Task.Yield();
        return ready;
    }

    // CLEAN — synchronous method, no pooled thread at stake
    public void WaitForReadySync()
    {
        _ready.Wait();
    }
}
