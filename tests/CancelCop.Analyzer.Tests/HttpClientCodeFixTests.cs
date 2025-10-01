using System.Collections.Immutable;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.CodeAnalysis.Testing.Verifiers;
using Xunit;

namespace CancelCop.Analyzer.Tests;

public class HttpClientCodeFixTests
{
    private static CSharpCodeFixTest<HttpClientAnalyzer, HttpClientCodeFixProvider, XUnitVerifier> CreateTest(
        string testCode,
        string fixedCode,
        params DiagnosticResult[] expected)
    {
        var test = new CSharpCodeFixTest<HttpClientAnalyzer, HttpClientCodeFixProvider, XUnitVerifier>
        {
            TestCode = testCode,
            FixedCode = fixedCode,
            ReferenceAssemblies = Microsoft.CodeAnalysis.Testing.ReferenceAssemblies.Net.Net90
                .AddPackages(ImmutableArray.Create(new PackageIdentity("Microsoft.Extensions.Http", "9.0.0"))),
        };

        test.ExpectedDiagnostics.AddRange(expected);
        return test;
    }

    [Fact]
    public async Task GetStringAsync_WithoutToken_AddsTokenParameter()
    {
        var test = @"
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    private readonly HttpClient _httpClient = new HttpClient();

    public async Task<string> FetchDataAsync(CancellationToken cancellationToken)
    {
        return await _httpClient.{|#0:GetStringAsync|}(""https://api.example.com"");
    }
}";

        var fixedCode = @"
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    private readonly HttpClient _httpClient = new HttpClient();

    public async Task<string> FetchDataAsync(CancellationToken cancellationToken)
    {
        return await _httpClient.GetStringAsync(""https://api.example.com"", cancellationToken);
    }
}";

        var expected = new DiagnosticResult("CC004", Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("GetStringAsync", "cancellationToken");

        await CreateTest(test, fixedCode, expected).RunAsync();
    }

    [Fact]
    public async Task GetAsync_WithoutToken_AddsTokenParameter()
    {
        var test = @"
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    private readonly HttpClient _httpClient = new HttpClient();

    public async Task<HttpResponseMessage> FetchAsync(CancellationToken cancellationToken)
    {
        return await _httpClient.{|#0:GetAsync|}(""https://api.example.com"");
    }
}";

        var fixedCode = @"
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    private readonly HttpClient _httpClient = new HttpClient();

    public async Task<HttpResponseMessage> FetchAsync(CancellationToken cancellationToken)
    {
        return await _httpClient.GetAsync(""https://api.example.com"", cancellationToken);
    }
}";

        var expected = new DiagnosticResult("CC004", Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("GetAsync", "cancellationToken");

        await CreateTest(test, fixedCode, expected).RunAsync();
    }

    [Fact]
    public async Task PostAsync_WithExistingArguments_AddsTokenAsLastArgument()
    {
        var test = @"
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    private readonly HttpClient _httpClient = new HttpClient();

    public async Task<HttpResponseMessage> PostDataAsync(StringContent content, CancellationToken cancellationToken)
    {
        return await _httpClient.{|#0:PostAsync|}(""https://api.example.com"", content);
    }
}";

        var fixedCode = @"
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    private readonly HttpClient _httpClient = new HttpClient();

    public async Task<HttpResponseMessage> PostDataAsync(StringContent content, CancellationToken cancellationToken)
    {
        return await _httpClient.PostAsync(""https://api.example.com"", content, cancellationToken);
    }
}";

        var expected = new DiagnosticResult("CC004", Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("PostAsync", "cancellationToken");

        await CreateTest(test, fixedCode, expected).RunAsync();
    }

    [Fact]
    public async Task SendAsync_WithRequestMessage_AddsTokenParameter()
    {
        var test = @"
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    private readonly HttpClient _httpClient = new HttpClient();

    public async Task<HttpResponseMessage> SendRequestAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return await _httpClient.{|#0:SendAsync|}(request);
    }
}";

        var fixedCode = @"
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    private readonly HttpClient _httpClient = new HttpClient();

    public async Task<HttpResponseMessage> SendRequestAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return await _httpClient.SendAsync(request, cancellationToken);
    }
}";

        var expected = new DiagnosticResult("CC004", Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("SendAsync", "cancellationToken");

        await CreateTest(test, fixedCode, expected).RunAsync();
    }
}
