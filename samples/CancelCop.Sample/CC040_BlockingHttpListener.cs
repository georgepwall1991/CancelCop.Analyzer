// =============================================================================
// CC040: Avoid blocking HttpListener.GetContext in async code
// =============================================================================
//
// WHY THIS MATTERS:
// HttpListener.GetContext parks a thread-pool thread until a request arrives.
// That wait is unbounded and is not a CancellationToken. GetContextAsync
// yields the thread (it does not take a token).
//
// WHY THIS IS NOT CC036–CC039:
// Those rules are Socket / TcpClient / TcpListener / UdpClient. The HTTP
// listener is a fifth type.
// =============================================================================

using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace CancelCop.Sample;

/// <summary>
/// Demonstrates CC040: blocking HttpListener.GetContext in async code.
/// </summary>
public class CC040_BlockingHttpListener
{
    // VIOLATION (CC040 warns here) — parks a pooled thread until a request arrives
    public async Task AcceptBad(HttpListener listener, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        listener.GetContext();
        await Task.Yield();
    }

    // FIXED — yields the thread
    public async Task AcceptGood(HttpListener listener, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await listener.GetContextAsync();
    }

    // CLEAN — synchronous method, no pooled thread at stake
    public void AcceptSync(HttpListener listener)
    {
        listener.GetContext();
    }
}
