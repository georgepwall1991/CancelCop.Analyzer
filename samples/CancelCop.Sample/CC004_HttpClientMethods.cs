// =============================================================================
// CC004: HttpClient methods must pass CancellationToken
// =============================================================================
//
// WHY THIS MATTERS:
// HTTP requests are inherently unreliable and can take unpredictable time:
// - Network latency varies wildly
// - Remote servers may be slow or unresponsive
// - DNS resolution can hang
// - SSL handshake can timeout
// - Large responses take time to download
//
// Without cancellation:
// - Users wait forever for hung requests
// - Connection pool gets exhausted
// - Server resources tied up in abandoned requests
// - Application appears frozen
//
// THE RULE:
// - HttpClient async methods (GetAsync, PostAsync, SendAsync, GetStringAsync, etc.)
//   should receive a CancellationToken when one is available
// - This allows HTTP requests to be aborted immediately
//
// =============================================================================

using System.Net.Http;
using System.Text;

namespace CancelCop.Sample;

/// <summary>
/// Demonstrates CC004: HttpClient methods must pass CancellationToken.
/// </summary>
public class CC004_HttpClientMethods
{
    private readonly HttpClient _httpClient = new();

    // -------------------------------------------------------------------------
    // VIOLATIONS (CC004 will warn on these)
    // -------------------------------------------------------------------------

    /// <summary>
    /// CC004 WARNING: GetStringAsync should receive CancellationToken.
    /// If the server is slow, this will block indefinitely.
    /// </summary>
    public async Task<string> GetDataWithoutTokenAsync(CancellationToken cancellationToken)
    {
        // BAD: No way to cancel if server doesn't respond
        return await _httpClient.GetStringAsync("https://api.example.com/data");
    }

    /// <summary>
    /// CC004 WARNING: GetAsync should receive CancellationToken.
    /// </summary>
    public async Task<HttpResponseMessage> FetchWithoutTokenAsync(CancellationToken cancellationToken)
    {
        // BAD: Request will continue even if user navigates away
        return await _httpClient.GetAsync("https://api.example.com/resource");
    }

    /// <summary>
    /// CC004 WARNING: PostAsync should receive CancellationToken.
    /// POST requests can be especially slow with large payloads.
    /// </summary>
    public async Task<HttpResponseMessage> PostWithoutTokenAsync(
        object data,
        CancellationToken cancellationToken)
    {
        var content = new StringContent(
            System.Text.Json.JsonSerializer.Serialize(data),
            Encoding.UTF8,
            "application/json");

        // BAD: Large uploads could take minutes
        return await _httpClient.PostAsync("https://api.example.com/upload", content);
    }

    /// <summary>
    /// CC004 WARNING: PutAsync should receive CancellationToken.
    /// </summary>
    public async Task<HttpResponseMessage> UpdateWithoutTokenAsync(
        int id,
        StringContent content,
        CancellationToken cancellationToken)
    {
        // BAD: Update requests should be cancellable
        return await _httpClient.PutAsync($"https://api.example.com/items/{id}", content);
    }

    /// <summary>
    /// CC004 WARNING: DeleteAsync should receive CancellationToken.
    /// </summary>
    public async Task<HttpResponseMessage> DeleteWithoutTokenAsync(
        int id,
        CancellationToken cancellationToken)
    {
        // BAD: Even delete requests can hang
        return await _httpClient.DeleteAsync($"https://api.example.com/items/{id}");
    }

    // -------------------------------------------------------------------------
    // CORRECT PATTERNS (No warnings)
    // -------------------------------------------------------------------------

    /// <summary>
    /// CORRECT: GetStringAsync with CancellationToken.
    /// Request will be aborted immediately if cancelled.
    /// </summary>
    public async Task<string> GetDataCorrectAsync(CancellationToken cancellationToken)
    {
        return await _httpClient.GetStringAsync(
            "https://api.example.com/data",
            cancellationToken);
    }

    /// <summary>
    /// CORRECT: GetAsync with CancellationToken.
    /// </summary>
    public async Task<HttpResponseMessage> FetchCorrectAsync(CancellationToken cancellationToken)
    {
        return await _httpClient.GetAsync(
            "https://api.example.com/resource",
            cancellationToken);
    }

    /// <summary>
    /// CORRECT: PostAsync with CancellationToken.
    /// </summary>
    public async Task<HttpResponseMessage> PostCorrectAsync(
        object data,
        CancellationToken cancellationToken)
    {
        var content = new StringContent(
            System.Text.Json.JsonSerializer.Serialize(data),
            Encoding.UTF8,
            "application/json");

        return await _httpClient.PostAsync(
            "https://api.example.com/upload",
            content,
            cancellationToken);
    }

    /// <summary>
    /// CORRECT: SendAsync with HttpRequestMessage and CancellationToken.
    /// This gives you full control over the request.
    /// </summary>
    public async Task<HttpResponseMessage> SendCustomRequestAsync(
        HttpMethod method,
        string url,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Add("X-Custom-Header", "value");

        return await _httpClient.SendAsync(request, cancellationToken);
    }

    /// <summary>
    /// CORRECT: GetByteArrayAsync with CancellationToken.
    /// Especially important for large downloads.
    /// </summary>
    public async Task<byte[]> DownloadFileAsync(string url, CancellationToken cancellationToken)
    {
        return await _httpClient.GetByteArrayAsync(url, cancellationToken);
    }

    /// <summary>
    /// CORRECT: GetStreamAsync with CancellationToken.
    /// Streaming large responses needs cancellation support.
    /// </summary>
    public async Task<Stream> GetStreamAsync(string url, CancellationToken cancellationToken)
    {
        return await _httpClient.GetStreamAsync(url, cancellationToken);
    }
}
