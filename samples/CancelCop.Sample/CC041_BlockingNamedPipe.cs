// =============================================================================
// CC041: Avoid blocking NamedPipeServerStream.WaitForConnection in async code
// =============================================================================
//
// WHY THIS MATTERS:
// NamedPipeServerStream.WaitForConnection parks a thread-pool thread until a
// client connects. That wait is unbounded and is not a CancellationToken.
// WaitForConnectionAsync yields the thread; on modern .NET it takes a token.
//
// WHY THIS IS NOT CC028 / CC036–CC040:
// CC028 maps File/Stream Read/Write/CopyTo/Flush. CC036–CC040 are Socket /
// TcpClient / TcpListener / UdpClient / HttpListener. The named-pipe server
// is a sixth type.
// =============================================================================

using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

namespace CancelCop.Sample;

/// <summary>
/// Demonstrates CC041: blocking NamedPipeServerStream.WaitForConnection in async code.
/// </summary>
public class CC041_BlockingNamedPipe
{
    // VIOLATION (CC041 warns here) — parks a pooled thread until a client connects
    public async Task AcceptBad(NamedPipeServerStream server, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        server.WaitForConnection();
        await Task.Yield();
    }

    // FIXED — yields the thread; token-taking overload is modern .NET
    public async Task AcceptGood(NamedPipeServerStream server, CancellationToken cancellationToken)
    {
        await server.WaitForConnectionAsync(cancellationToken);
    }

    // CLEAN — synchronous method, no pooled thread at stake
    public void AcceptSync(NamedPipeServerStream server)
    {
        server.WaitForConnection();
    }
}
