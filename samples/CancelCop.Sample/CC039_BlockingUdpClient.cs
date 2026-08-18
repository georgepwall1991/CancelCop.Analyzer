// =============================================================================
// CC039: Avoid blocking UdpClient.Receive in async code
// =============================================================================
//
// WHY THIS MATTERS:
// UdpClient.Receive parks a thread-pool thread until a datagram arrives. That
// wait is unbounded and is not a CancellationToken. ReceiveAsync yields the
// thread and accepts a token on modern .NET.
//
// WHY THIS IS NOT CC036/CC037/CC038:
// CC036 is symbol-gated to Socket. CC037 is TcpClient.Connect. CC038 is
// TcpListener accept. The UDP wrapper is a fourth type.
// =============================================================================

using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace CancelCop.Sample;

/// <summary>
/// Demonstrates CC039: blocking UdpClient.Receive in async code.
/// </summary>
public class CC039_BlockingUdpClient
{
    // VIOLATION (CC039 warns here) — parks a pooled thread until a datagram arrives
    public async Task ReceiveBad(UdpClient client, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IPEndPoint? remote = null;
        client.Receive(ref remote);
        await Task.Yield();
    }

    // FIXED — yields the thread and honours cancellation
    public async Task ReceiveGood(UdpClient client, CancellationToken cancellationToken)
    {
        await client.ReceiveAsync(cancellationToken);
    }

    // CLEAN — synchronous method, no pooled thread at stake
    public void ReceiveSync(UdpClient client)
    {
        IPEndPoint? remote = null;
        client.Receive(ref remote);
    }
}
