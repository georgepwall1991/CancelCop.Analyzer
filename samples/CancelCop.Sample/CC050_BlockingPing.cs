// =============================================================================
// CC050: Avoid blocking Ping.Send in async code
// =============================================================================
//
// WHY THIS MATTERS:
// Ping.Send parks a pooled thread until the ICMP echo round trip finishes
// (up to the timeout). SendPingAsync yields the thread.
// Token-taking SendPingAsync exists only on the TimeSpan arities, modern .NET.
// Do not confuse it with the event-based SendAsync.
// =============================================================================

using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;

namespace CancelCop.Sample;

/// <summary>
/// Demonstrates CC050: blocking Ping.Send in async code.
/// </summary>
public class CC050_BlockingPing
{
    // VIOLATION (CC050 warns here) — parks a pooled thread on an ICMP round trip
    public async Task ProbeBad(Ping ping)
    {
        ping.Send("example.com");
        await Task.Yield();
    }

    // FIXED — yields the thread
    public async Task ProbeGood(Ping ping)
    {
        await ping.SendPingAsync("example.com");
    }

    // CLEAN — CC050–CC052 only inspect async functions, so a sync method stays quiet
    public void ProbeSync(Ping ping)
    {
        ping.Send("example.com");
    }
}
