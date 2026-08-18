// =============================================================================
// CC042: Avoid blocking NamedPipeClientStream.Connect in async code
// =============================================================================
//
// WHY THIS MATTERS:
// NamedPipeClientStream.Connect parks a thread-pool thread until the server
// accepts (or a timeout elapses). That wait is not a CancellationToken.
// ConnectAsync yields the thread; on modern .NET it takes a token.
//
// WHY THIS IS NOT CC041:
// CC041 is NamedPipeServerStream.WaitForConnection. The client connect is a
// sibling type.
// =============================================================================

using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

namespace CancelCop.Sample;

/// <summary>
/// Demonstrates CC042: blocking NamedPipeClientStream.Connect in async code.
/// </summary>
public class CC042_BlockingNamedPipeClient
{
    // VIOLATION (CC042 warns here) — parks a pooled thread until the server accepts
    public async Task ConnectBad(NamedPipeClientStream client, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        client.Connect();
        await Task.Yield();
    }

    // FIXED — yields the thread; token-taking overload is modern .NET
    public async Task ConnectGood(NamedPipeClientStream client, CancellationToken cancellationToken)
    {
        await client.ConnectAsync(cancellationToken);
    }

    // CLEAN — synchronous method, no pooled thread at stake
    public void ConnectSync(NamedPipeClientStream client)
    {
        client.Connect();
    }
}
