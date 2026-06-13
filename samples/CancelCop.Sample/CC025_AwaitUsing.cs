// =============================================================================
// CC025: Prefer await using for IAsyncDisposable
// =============================================================================
//
// WHY THIS MATTERS:
// A type implementing IAsyncDisposable releases its resources asynchronously.
// Disposing it through a synchronous `using` calls Dispose() — which typically
// blocks on the async cleanup. In async code `await using` awaits DisposeAsync().
//
// THE RULE (Info):
// - Flags a `using` (without await) over an IAsyncDisposable resource in async code.
// - Code fix turns it into `await using`.
// =============================================================================

using System;
using System.Threading.Tasks;

namespace CancelCop.Sample;

/// <summary>
/// Demonstrates CC025: prefer await using for IAsyncDisposable.
/// </summary>
public class CC025_AwaitUsing
{
    // Implements both so the synchronous `using` in RunBad still compiles.
    private sealed class AsyncResource : IDisposable, IAsyncDisposable
    {
        public void Dispose() { }
        public ValueTask DisposeAsync() => default;
    }

    // VIOLATION (CC025 suggests here)
    public async Task RunBad()
    {
        using var resource = new AsyncResource();
        await Task.Yield();
    }

    // FIXED
    public async Task RunGood()
    {
        await using var resource = new AsyncResource();
        await Task.Yield();
    }
}
