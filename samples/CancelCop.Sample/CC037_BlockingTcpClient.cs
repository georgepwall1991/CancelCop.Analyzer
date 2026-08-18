// =============================================================================
// CC037: Avoid blocking TcpClient.Connect in async code
// =============================================================================
//
// WHY THIS MATTERS:
// TcpClient.Connect parks a thread-pool thread until the handshake finishes or
// TCP times out. That timeout can run into tens of seconds and is not a
// CancellationToken. ConnectAsync yields the thread and accepts a token.
//
// WHY THIS IS NOT CC036:
// CC036 is symbol-gated to Socket. Application code almost always uses the
// TcpClient wrapper, which none of the previous rules reported.
// =============================================================================

using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace CancelCop.Sample;

/// <summary>
/// Demonstrates CC037: blocking TcpClient.Connect in async code.
/// </summary>
public class CC037_BlockingTcpClient
{
    // VIOLATION (CC037 warns here) — parks a pooled thread until the handshake finishes
    public async Task ConnectBad(TcpClient client, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        client.Connect("localhost", 80);
        await Task.Yield();
    }

    // FIXED — yields the thread and honours cancellation
    public async Task ConnectGood(TcpClient client, CancellationToken cancellationToken)
    {
        await client.ConnectAsync("localhost", 80, cancellationToken);
    }

    // CLEAN — synchronous method, no pooled thread at stake
    public void ConnectSync(TcpClient client)
    {
        client.Connect("localhost", 80);
    }
}
