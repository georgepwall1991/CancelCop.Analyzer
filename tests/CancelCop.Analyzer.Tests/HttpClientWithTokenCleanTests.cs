using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace CancelCop.Analyzer.Tests;

public class HttpClientWithTokenCleanTests
{
    [Fact]
    public async Task GetAsyncWithToken_ShouldNotReportDiagnostic()
    {
        // CC004 flags an HttpClient call that drops an in-scope token. When the token is passed to
        // GetAsync, propagation is correct and nothing is flagged.
        var test = @"
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(HttpClient client, CancellationToken cancellationToken)
    {
        await client.GetAsync(""https://example.com"", cancellationToken);
    }
}";

        var t = new CSharpAnalyzerTest<HttpClientAnalyzer, DefaultVerifier>
        {
            TestCode = test,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        await t.RunAsync();
    }
}
