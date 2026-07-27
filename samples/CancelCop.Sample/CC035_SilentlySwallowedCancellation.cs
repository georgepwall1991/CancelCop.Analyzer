// =============================================================================
// CC035: Cancellation is silently swallowed by an empty catch
// =============================================================================
//
// WHY THIS MATTERS:
// Cancellation is reported by an exception precisely so the caller learns the
// work did NOT finish. An empty catch discards that signal: execution continues
// past the try as though the operation succeeded, and the caller sees a normal
// return. Downstream code then acts on results that were never produced — a
// partially written file treated as complete, an empty collection treated as
// "no matches".
//
// HOW THIS DIFFERS FROM CC019:
// CC019 covers a BROAD catch — `catch` or `catch (Exception)` — that happens to
// swallow cancellation among everything else. A clause naming
// OperationCanceledException explicitly is outside its scope, yet it is the more
// deliberate-looking version of the same defect.
//
// SCOPED TO THE EMPTY BODY DELIBERATELY:
// Catching cancellation to stop quietly is real at a boundary, and such handlers
// log, set state, or break a loop. Any statement, a `when` filter, a rethrow — or
// even a comment explaining the intent — means the author considered the case,
// and the rule stays quiet. Info severity: a deliberate silent stop is unusual
// but legitimate.
// =============================================================================

using System;
using System.Threading;
using System.Threading.Tasks;

namespace CancelCop.Sample;

/// <summary>
/// Demonstrates CC035: cancellation discarded by an empty catch.
/// </summary>
public class CC035_SilentlySwallowedCancellation
{
    // VIOLATION (CC035 reports here) — the caller cannot tell the save did not happen
    public async Task SaveBad(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(1000, cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
    }

    // FIXED — the caller learns the work did not complete
    public async Task SaveGood(CancellationToken cancellationToken)
    {
        await Task.Delay(1000, cancellationToken);
    }

    // CLEAN — the handler does something, so the cancellation was considered
    public async Task SaveAndLog(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(1000, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("save cancelled");
        }
    }

    // CLEAN — waiting until cancelled is idiomatic, and the note records the intent
    public async Task WaitForShutdown(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
        catch (TaskCanceledException)
        {
            // expected on shutdown
        }
    }
}
