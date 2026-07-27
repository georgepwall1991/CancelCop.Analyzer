// =============================================================================
// CC032: Async call is not awaited in non-async code
// =============================================================================
//
// WHY THIS MATTERS:
// A dropped task cannot be cancelled, cannot be waited on at shutdown, and its
// failure is never observed — the exception surfaces later on an unrelated
// thread, or nowhere at all. Work started this way outlives the request or host
// that started it, which is the same class of problem as a token that is never
// passed.
//
// THE GAP THIS FILLS:
// The compiler's CS4014 only fires INSIDE an async method. In a constructor, a
// synchronous method, or a non-async lambda — exactly where the mistake is
// easiest to make, because there is no await to reach for — it says nothing.
// CC032 covers only that gap and stays quiet where CS4014 already reports.
//
// THE RULE:
// - Flags a bare Task/ValueTask-returning call as an expression statement, or as
//   the body of a void-returning expression-bodied lambda, in non-async code.
// - A task that is assigned, returned, passed as an argument, or explicitly
//   discarded with `_ =` is not dropped, and is not flagged.
// - Analyzer-only: the right resolution depends on intent.
// =============================================================================

using System;
using System.Threading;
using System.Threading.Tasks;

namespace CancelCop.Sample;

/// <summary>
/// Demonstrates CC032: async call is not awaited in non-async code.
/// </summary>
public class CC032_UnawaitedAsyncCall
{
    // VIOLATION (CC032 warns here) — a constructor cannot be async, so CS4014 is silent
    public CC032_UnawaitedAsyncCall()
    {
        InitializeAsync();
    }

    // VIOLATION (CC032 warns here too) — synchronous method, compiler silent
    public void StartBad()
    {
        SaveAsync();
    }

    // VIOLATION (CC032 warns here too) — void-returning lambda drops the task
    public void RegisterBad(Action register)
    {
        Action callback = () => SaveAsync();
        callback();
    }

    // FIXED — the caller becomes async and awaits, so cancellation and failures flow
    public async Task StartGood(CancellationToken cancellationToken)
    {
        await SaveCoreAsync(cancellationToken);
    }

    // CLEAN — an explicit discard is the documented way to opt in deliberately
    public void StartDeliberately()
    {
        _ = SaveAsync();
    }

    // CLEAN — the task is handed to something that observes it
    public Task StartAndReturn() => SaveAsync();

    private Task InitializeAsync() => Task.CompletedTask;

    private Task SaveAsync() => Task.CompletedTask;

    private Task SaveCoreAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
