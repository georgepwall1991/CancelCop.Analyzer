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
//   read/write/append helpers, StreamReader.ReadToEnd()/ReadLine(),
//   StreamWriter.Write/WriteLine/Flush, and the Stream primitives themselves
//   (Read/Write/CopyTo/Flush) on any type deriving from System.IO.Stream. The token
//   is only flowed when the matched async overload accepts one.
// - MemoryStream is excluded: it is backed by an in-memory buffer, so the blocking
//   call never leaves the CPU and the async form only wraps the same work.
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

    // VIOLATION (CC028 warns here too — Stream.CopyTo blocks for the whole transfer)
    public async Task ArchiveBad(Stream source, Stream destination)
    {
        source.CopyTo(destination);
        await Task.Yield();
    }

    // FIXED
    public async Task ArchiveGood(
        Stream source,
        Stream destination,
        CancellationToken cancellationToken
    )
    {
        await source.CopyToAsync(destination, cancellationToken);
    }

    // CLEAN — a MemoryStream read is an in-memory buffer copy, not blocking I/O
    public async Task<int> BufferOk(MemoryStream buffer, byte[] target)
    {
        var read = buffer.Read(target, 0, target.Length);
        await Task.Yield();
        return read;
    }
}
