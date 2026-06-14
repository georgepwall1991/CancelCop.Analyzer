using System.Collections.Immutable;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace CancelCop.Analyzer.Tests;

public class HttpClientCodeFixTests
{
    private static CSharpCodeFixTest<HttpClientAnalyzer, HttpClientCodeFixProvider, DefaultVerifier> CreateTest(
        string testCode,
        string fixedCode,
        params DiagnosticResult[] expected)
    {
        var test = new CSharpCodeFixTest<HttpClientAnalyzer, HttpClientCodeFixProvider, DefaultVerifier>
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
    public async Task FixAll_TwoCalls_BothGetTokenArgument()
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
        var a = await _httpClient.{|#0:GetStringAsync|}(""https://api.example.com/a"");
        var b = await _httpClient.{|#1:GetStringAsync|}(""https://api.example.com/b"");
        return a + b;
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
        var a = await _httpClient.GetStringAsync(""https://api.example.com/a"", cancellationToken);
        var b = await _httpClient.GetStringAsync(""https://api.example.com/b"", cancellationToken);
        return a + b;
    }
}";

        await CreateTest(
            test,
            fixedCode,
            new DiagnosticResult("CC004", Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
                .WithLocation(0).WithArguments("GetStringAsync", "cancellationToken"),
            new DiagnosticResult("CC004", Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
                .WithLocation(1).WithArguments("GetStringAsync", "cancellationToken")).RunAsync();
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

    [Fact]
    public async Task PostAsync_WithOutOfPositionNamedArguments_AddsNamedTokenArgument()
    {
        // Appending a positional argument after an out-of-position named argument is CS8323;
        // the fix must emit `cancellationToken: ct` instead.
        var test = @"
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    private readonly HttpClient _httpClient = new HttpClient();

    public async Task PostDataAsync(HttpContent body, CancellationToken ct)
    {
        await _httpClient.{|#0:PostAsync|}(content: body, requestUri: ""https://api.example.com"");
    }
}";

        var fixedCode = @"
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    private readonly HttpClient _httpClient = new HttpClient();

    public async Task PostDataAsync(HttpContent body, CancellationToken ct)
    {
        await _httpClient.PostAsync(content: body, requestUri: ""https://api.example.com"", cancellationToken: ct);
    }
}";

        var expected = new DiagnosticResult("CC004", Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("PostAsync", "ct");

        await CreateTest(test, fixedCode, expected).RunAsync();
    }
}
