// =============================================================================
// CC052: Avoid blocking WebRequest.GetResponse in async code
// =============================================================================
//
// WHY THIS MATTERS:
// GetResponse parks a pooled thread on the HTTP round trip. The TAP
// counterpart is the parameterless virtual GetResponseAsync — no
// CancellationToken arity exists anywhere in the family, so rewrites are
// honestly tokenless. APM BeginGetResponse/EndGetResponse never count.
// =============================================================================

using System.Net;
using System.Threading.Tasks;

namespace CancelCop.Sample;

/// <summary>
/// Demonstrates CC052: blocking WebRequest.GetResponse in async code.
/// </summary>
public class CC052_BlockingWebRequest
{
    // VIOLATION (CC052 warns here) — parks a pooled thread on the response wait
    public async Task FetchBad(WebRequest request)
    {
        request.GetResponse();
        await Task.Yield();
    }

    // FIXED — yields the thread (tokenless: no ct arity exists in the family)
    public async Task FetchGood(WebRequest request)
    {
        await request.GetResponseAsync();
    }

    // CLEAN — CC050–CC052 only inspect async functions, so a sync method stays quiet
    public void FetchSync(WebRequest request)
    {
        request.GetResponse();
    }
}
