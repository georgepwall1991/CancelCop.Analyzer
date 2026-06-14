// =============================================================================
// CC028: Avoid blocking System.IO.File calls in async code
// =============================================================================
//
// WHY THIS MATTERS:
// Synchronous File helpers such as File.ReadAllText block the calling thread for
// the whole disk operation. Inside an async method that ties up a thread-pool
// thread and defeats the point of being async. The async counterparts
// (ReadAllTextAsync, WriteAllTextAsync, ...) yield the thread and accept a
// CancellationToken. This rounds out the blocking-in-async family alongside
// CC013 (Thread.Sleep), CC015 (Task.Wait/.Result) and CC026 (SemaphoreSlim.Wait).
//
// THE RULE:
// - Flags a well-known blocking System.IO.File method (read/write/append helpers)
//   that has an <name>Async counterpart, called inside async code.
// =============================================================================

using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace CancelCop.Sample;

/// <summary>
/// Demonstrates CC028: avoid blocking System.IO.File calls in async code.
/// </summary>
public class CC028_BlockingFileIo
{
    // VIOLATION (CC028 warns here)
    public async Task<string> LoadBad(string path)
    {
        var text = File.ReadAllText(path);
        await Task.Yield();
        return text;
    }

    // FIXED
    public async Task<string> LoadGood(string path, CancellationToken cancellationToken)
    {
        return await File.ReadAllTextAsync(path, cancellationToken);
    }
}
