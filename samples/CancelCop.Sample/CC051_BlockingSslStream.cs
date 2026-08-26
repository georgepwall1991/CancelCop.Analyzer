// =============================================================================
// CC051: Avoid blocking SslStream.AuthenticateAsClient in async code
// =============================================================================
//
// WHY THIS MATTERS:
// The TLS handshake parks a pooled thread for several network round trips.
// AuthenticateAsClientAsync yields the thread.
// Only the SslClientAuthenticationOptions arity takes a CancellationToken.
// =============================================================================

using System.IO;
using System.Net.Security;
using System.Threading.Tasks;

namespace CancelCop.Sample;

/// <summary>
/// Demonstrates CC051: blocking SslStream.AuthenticateAsClient in async code.
/// </summary>
public class CC051_BlockingSslStream
{
    // VIOLATION (CC051 warns here) — parks a pooled thread on the TLS handshake
    public async Task HandshakeBad(SslStream stream)
    {
        stream.AuthenticateAsClient("example.com");
        await Task.Yield();
    }

    // FIXED — yields the thread
    public async Task HandshakeGood(
        SslStream stream,
        System.Net.Security.SslClientAuthenticationOptions options)
    {
        await stream.AuthenticateAsClientAsync(options);
    }

    // CLEAN — synchronous method, no pooled thread at stake
    public void HandshakeSync(SslStream stream)
    {
        stream.AuthenticateAsClient("example.com");
    }
}
