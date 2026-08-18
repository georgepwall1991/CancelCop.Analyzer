// =============================================================================
// CC044: Avoid blocking Dns.GetHostEntry in async code
// =============================================================================
//
// WHY THIS MATTERS:
// Dns.GetHostEntry parks a thread-pool thread on a DNS query, including
// reverse lookup of a numeric IP. GetHostEntryAsync yields the thread; on
// modern .NET the string overloads take a token.
//
// WHY THIS IS NOT CC043:
// CC043 is GetHostAddresses only. A numeric IP is a parse for
// GetHostAddresses, but GetHostEntry still does reverse DNS.
// =============================================================================

using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace CancelCop.Sample;

/// <summary>
/// Demonstrates CC044: blocking Dns.GetHostEntry in async code.
/// </summary>
public class CC044_BlockingDnsGetHostEntry
{
    // VIOLATION (CC044 warns here) — parks a pooled thread on a DNS query
    public async Task ResolveBad(string host, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Dns.GetHostEntry(host);
        await Task.Yield();
    }

    // FIXED — yields the thread; token-taking string overload is modern .NET
    public async Task ResolveGood(string host, CancellationToken cancellationToken)
    {
        await Dns.GetHostEntryAsync(host, cancellationToken);
    }

    // CLEAN — synchronous method, no pooled thread at stake
    public void ResolveSync(string host)
    {
        Dns.GetHostEntry(host);
    }
}
