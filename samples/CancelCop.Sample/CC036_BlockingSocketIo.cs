// =============================================================================
// CC036: Avoid blocking socket calls in async code
// =============================================================================
//
// WHY THIS MATTERS:
// A socket call blocks until the network responds — or until a TCP timeout that
// can run into minutes. Inside async code that parks a thread-pool thread on a
// remote party's behaviour, the least predictable thing a server waits on.
// Accept and Connect are worse still: they can block indefinitely, with no data
// to wait for.
//
// HOW THIS DIFFERS FROM CC028:
// CC028 covers blocking System.IO calls including every Stream, so a
// NetworkStream is already handled there. It offers a code fix, which it can only
// do safely because it requires the async counterpart to be SIGNATURE-COMPATIBLE.
// Socket's async APIs are not shaped that way — Receive(byte[]) pairs with
// ReceiveAsync(Memory<byte>, CancellationToken), and Accept() with
// AcceptAsync(CancellationToken) returning a different type. Loosening CC028's
// matching to reach them would give up the property that makes its rewrites safe,
// so this is a separate, analyzer-only rule.
// =============================================================================

using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace CancelCop.Sample;

/// <summary>
/// Demonstrates CC036: blocking socket calls in async code.
/// </summary>
public class CC036_BlockingSocketIo
{
    // VIOLATION (CC036 warns here) — parks a pooled thread until the network responds
    public async Task ReceiveBad(Socket socket, byte[] buffer)
    {
        socket.Receive(buffer);
        await Task.Yield();
    }

    // VIOLATION (CC036 warns here too) — can block indefinitely waiting for a connection
    public async Task AcceptBad(Socket listener)
    {
        var client = listener.Accept();
        await Task.Yield();
    }

    // FIXED — yields the thread and honours cancellation
    public async Task ReceiveGood(Socket socket, byte[] buffer, CancellationToken cancellationToken)
    {
        await socket.ReceiveAsync(buffer.AsMemory(), cancellationToken);
    }

    // FIXED
    public async Task AcceptGood(Socket listener, CancellationToken cancellationToken)
    {
        var client = await listener.AcceptAsync(cancellationToken);
    }

    // CLEAN — synchronous method, no pooled thread at stake
    public void ReceiveSync(Socket socket, byte[] buffer)
    {
        socket.Receive(buffer);
    }
}
