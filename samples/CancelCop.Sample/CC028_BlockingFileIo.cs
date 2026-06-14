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
// - Flags a well-known blocking System.IO method that has a signature-compatible
//   <name>Async counterpart, called inside async code: the System.IO.File
//   read/write/append helpers, StreamReader.ReadToEnd()/ReadLine(), and
//   StreamWriter.Write/WriteLine/Flush. The token is only flowed when the matched
//   async overload accepts one.
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

    // VIOLATION (CC028 warns here too — StreamReader.ReadToEnd blocks)
    public async Task<string> DrainBad(StreamReader reader)
    {
        var text = reader.ReadToEnd();
        await Task.Yield();
        return text;
    }

    // FIXED
    public async Task<string> DrainGood(StreamReader reader, CancellationToken cancellationToken)
    {
        return await reader.ReadToEndAsync(cancellationToken);
    }

    // VIOLATION (CC028 warns here too — StreamWriter.Write/Flush block in async code)
    public async Task PersistBad(StreamWriter writer, string text)
    {
        writer.Write(text);
        writer.Flush();
        await Task.Yield();
    }

    // FIXED — WriteAsync(string) has no token overload; FlushAsync flows the token
    public async Task PersistGood(StreamWriter writer, string text, CancellationToken cancellationToken)
    {
        await writer.WriteAsync(text);
        await writer.FlushAsync(cancellationToken);
    }
}
