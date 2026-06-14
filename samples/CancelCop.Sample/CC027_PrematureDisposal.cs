// =============================================================================
// CC027: Returned task uses a disposed 'using' resource
// =============================================================================
//
// WHY THIS MATTERS:
// A `using` declaration disposes its resource when the method returns. If the
// method returns a task produced by calling that resource, the resource is
// disposed while the task is still running, so the caller awaits an operation on
// a disposed object (often an ObjectDisposedException). Make the method async and
// await the call so the resource lives until completion.
//
// THE RULE:
// - Flags a non-async Task-returning method whose `return` is a call whose
//   left-most receiver is a `using`-declared local. High confidence: a resource
//   read synchronously into a completed task is not flagged.
// =============================================================================

using System;
using System.Threading.Tasks;

namespace CancelCop.Sample;

/// <summary>
/// Demonstrates CC027: a returned task that uses a disposed using resource.
/// </summary>
public class CC027_PrematureDisposal
{
    private sealed class Resource : IDisposable
    {
        public void Dispose() { }
        public Task<int> LoadAsync() => Task.FromResult(0);
    }

    // VIOLATION (CC027 warns here) - resource is disposed before the task completes
    public Task<int> ReadBad()
    {
        using var resource = new Resource();
        return resource.LoadAsync();
    }

    // FIXED - async + await keeps the resource alive until completion
    public async Task<int> ReadGood()
    {
        using var resource = new Resource();
        return await resource.LoadAsync();
    }
}
