// =============================================================================
// CC043: Avoid blocking Dns.GetHostAddresses in async code
// =============================================================================
//
// WHY THIS MATTERS:
// Dns.GetHostAddresses parks a thread-pool thread on a DNS query. That wait
// is unbounded and is not a CancellationToken. GetHostAddressesAsync yields
// the thread; on modern .NET it takes a token.
//
// WHY THIS IS NOT CC036–CC042:
// Those rules are Socket / Tcp / Udp / HttpListener / named-pipe. DNS is a
// separate type. CC002 cannot see it (no token overload of this method).
// =============================================================================

using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace CancelCop.Sample;

/// <summary>
/// Demonstrates CC043: blocking Dns.GetHostAddresses in async code.
/// </summary>
public class CC043_BlockingDns
{
    // VIOLATION (CC043 warns here) — parks a pooled thread on a DNS query
    public async Task ResolveBad(string host, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Dns.GetHostAddresses(host);
        await Task.Yield();
    }

    // FIXED — yields the thread; token-taking overload is modern .NET
    public async Task ResolveGood(string host, CancellationToken cancellationToken)
    {
        await Dns.GetHostAddressesAsync(host, cancellationToken);
    }

    // CLEAN — synchronous method, no pooled thread at stake
    public void ResolveSync(string host)
    {
        Dns.GetHostAddresses(host);
    }
}
