using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace CancelCop.Analyzer.Tests;

public class HttpClientNoTokenInScopeTests
{
    [Fact]
    public async Task GetAsyncWithNoTokenInScope_ShouldNotReportDiagnostic()
    {
        // CC004 only fires when a CancellationToken is in scope to propagate. With no token available,
        // there is nothing to pass and the call must not be flagged.
        var test = @"
using System.Net.Http;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(HttpClient client)
    {
        await client.GetAsync(""https://example.com"");
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
