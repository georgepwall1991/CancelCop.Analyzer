// =============================================================================
// CC038: Avoid blocking TcpListener accept in async code
// =============================================================================
//
// WHY THIS MATTERS:
// TcpListener.AcceptTcpClient / AcceptSocket park a thread-pool thread until a
// client connects. That wait is unbounded and is not a CancellationToken.
// AcceptTcpClientAsync / AcceptSocketAsync yield the thread and accept a token.
//
// WHY THIS IS NOT CC036/CC037:
// CC036 is symbol-gated to Socket. CC037 is TcpClient.Connect. The listener
// accept path is a third type, which none of the previous rules reported.
// =============================================================================

using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace CancelCop.Sample;

/// <summary>
/// Demonstrates CC038: blocking TcpListener accept in async code.
/// </summary>
public class CC038_BlockingTcpListener
{
    // VIOLATION (CC038 warns here) — parks a pooled thread until a client connects
    public async Task AcceptBad(TcpListener listener, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        listener.AcceptTcpClient();
        await Task.Yield();
    }

    // FIXED — yields the thread and honours cancellation
    public async Task AcceptGood(TcpListener listener, CancellationToken cancellationToken)
    {
        await listener.AcceptTcpClientAsync(cancellationToken);
    }

    // CLEAN — synchronous method, no pooled thread at stake
    public void AcceptSync(TcpListener listener)
    {
        listener.AcceptTcpClient();
    }
}
