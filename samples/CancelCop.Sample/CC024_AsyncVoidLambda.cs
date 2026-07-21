// =============================================================================
// CC024: Avoid async lambdas converted to void-returning delegates (async void)
// =============================================================================
//
// WHY THIS MATTERS:
// When an async lambda is assigned to any void-returning delegate (or passed where
// one is expected), it binds as async void: the caller cannot await it and an unhandled
// exception crashes the process. The classic trap is
// Parallel.ForEach(items, async item => await ...).
//
// THE RULE:
// - Flags an async lambda whose converted delegate returns void.
// - Task-returning delegates and event-handler delegate shapes are not flagged.
// =============================================================================

using System;
using System.Threading.Tasks;

namespace CancelCop.Sample;

/// <summary>
/// Demonstrates CC024: avoid async lambdas converted to void-returning delegates.
/// </summary>
public class CC024_AsyncVoidLambda
{
    private Task WorkAsync() => Task.CompletedTask;

    public void Register()
    {
        // VIOLATION (CC024 warns here) - runs as async void
        Action bad = async () => await WorkAsync();

        // FIXED - use a Func<Task> and an API that awaits it
        Func<Task> good = async () => await WorkAsync();

        _ = bad;
        _ = good;
    }
}
