// =============================================================================
// Framework token sources: HttpContext.RequestAborted in convention middleware
// =============================================================================
//
// Convention middleware Invoke/InvokeAsync is not an interface member, so CC001
// does not add a CancellationToken parameter (ASP.NET Core DI would not inject it).
// The request's token is HttpContext.RequestAborted. The shared walk treats that
// property as in-scope, so CC002/CC004 (and siblings) require it to be passed.
//
// =============================================================================

namespace Microsoft.AspNetCore.Http
{
    /// <summary>Stand-in so the sample compiles without an ASP.NET Core package.</summary>
    public abstract class HttpContext
    {
        public CancellationToken RequestAborted => default;
    }
}

namespace CancelCop.Sample
{
    using System.Net.Http;
    using Microsoft.AspNetCore.Http;

    /// <summary>
    /// Demonstrates framework property tokens in convention middleware.
    /// </summary>
    public class FrameworkTokenMiddleware
    {
        private readonly HttpClient _http = new();

        /// <summary>
        /// CC002 WARNING: Task.Delay should receive context.RequestAborted.
        /// CC004 WARNING: GetStringAsync should receive context.RequestAborted.
        /// CC001 is not reported — adding a CancellationToken parameter would not be injected.
        /// </summary>
        public async Task InvokeAsync(HttpContext context)
        {
            await Task.Delay(100);
            _ = await _http.GetStringAsync("https://api.example.com/data");
        }

        /// <summary>
        /// CORRECT: RequestAborted is observed and flowed; CC001 stays quiet on Invoke.
        /// </summary>
        public async Task Invoke(HttpContext context)
        {
            await Task.Delay(100, context.RequestAborted);
            _ = await _http.GetStringAsync("https://api.example.com/data", context.RequestAborted);
        }
    }
}
