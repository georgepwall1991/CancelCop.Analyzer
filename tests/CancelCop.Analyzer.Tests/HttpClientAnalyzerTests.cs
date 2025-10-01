using System.Collections.Immutable;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.CodeAnalysis.Testing.Verifiers;
using Xunit;

namespace CancelCop.Analyzer.Tests;

public class HttpClientAnalyzerTests
{
    private static CSharpAnalyzerTest<HttpClientAnalyzer, XUnitVerifier> CreateTest(string testCode, params DiagnosticResult[] expected)
    {
        var test = new CSharpAnalyzerTest<HttpClientAnalyzer, XUnitVerifier>
        {
            TestCode = testCode,
            ReferenceAssemblies = Microsoft.CodeAnalysis.Testing.ReferenceAssemblies.Net.Net90
                .AddPackages(ImmutableArray.Create(new PackageIdentity("Microsoft.Extensions.Http", "9.0.0"))),
        };

        test.ExpectedDiagnostics.AddRange(expected);
        return test;
    }

    [Fact]
    public async Task GetStringAsync_WithoutToken_WhenTokenAvailable_ShouldReportDiagnostic()
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

        var expected = new DiagnosticResult("CC004", Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("GetStringAsync", "cancellationToken");

        await CreateTest(test, expected).RunAsync();
    }

    [Fact]
    public async Task GetStringAsync_WithToken_ShouldNotReportDiagnostic()
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
        return await _httpClient.GetStringAsync(""https://api.example.com"", cancellationToken);
    }
}";

        await CreateTest(test).RunAsync();
    }

    [Fact]
    public async Task GetAsync_WithoutToken_WhenTokenAvailable_ShouldReportDiagnostic()
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

        var expected = new DiagnosticResult("CC004", Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("GetAsync", "cancellationToken");

        await CreateTest(test, expected).RunAsync();
    }

    [Fact]
    public async Task PostAsync_WithoutToken_WhenTokenAvailable_ShouldReportDiagnostic()
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

        var expected = new DiagnosticResult("CC004", Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("PostAsync", "cancellationToken");

        await CreateTest(test, expected).RunAsync();
    }

    [Fact]
    public async Task PutAsync_WithoutToken_WhenTokenAvailable_ShouldReportDiagnostic()
    {
        var test = @"
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    private readonly HttpClient _httpClient = new HttpClient();

    public async Task<HttpResponseMessage> UpdateDataAsync(StringContent content, CancellationToken cancellationToken)
    {
        return await _httpClient.{|#0:PutAsync|}(""https://api.example.com"", content);
    }
}";

        var expected = new DiagnosticResult("CC004", Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("PutAsync", "cancellationToken");

        await CreateTest(test, expected).RunAsync();
    }

    [Fact]
    public async Task DeleteAsync_WithoutToken_WhenTokenAvailable_ShouldReportDiagnostic()
    {
        var test = @"
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    private readonly HttpClient _httpClient = new HttpClient();

    public async Task<HttpResponseMessage> DeleteDataAsync(CancellationToken cancellationToken)
    {
        return await _httpClient.{|#0:DeleteAsync|}(""https://api.example.com"");
    }
}";

        var expected = new DiagnosticResult("CC004", Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("DeleteAsync", "cancellationToken");

        await CreateTest(test, expected).RunAsync();
    }

    [Fact]
    public async Task SendAsync_WithoutToken_WhenTokenAvailable_ShouldReportDiagnostic()
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

        var expected = new DiagnosticResult("CC004", Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("SendAsync", "cancellationToken");

        await CreateTest(test, expected).RunAsync();
    }

    [Fact]
    public async Task HttpClientMethod_NoTokenParameter_ShouldNotReportDiagnostic()
    {
        var test = @"
using System.Net.Http;
using System.Threading.Tasks;

public class TestClass
{
    private readonly HttpClient _httpClient = new HttpClient();

    public async Task<string> FetchDataAsync()
    {
        return await _httpClient.GetStringAsync(""https://api.example.com"");
    }
}";

        await CreateTest(test).RunAsync();
    }

    [Fact]
    public async Task GetByteArrayAsync_WithoutToken_WhenTokenAvailable_ShouldReportDiagnostic()
    {
        var test = @"
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    private readonly HttpClient _httpClient = new HttpClient();

    public async Task<byte[]> FetchBytesAsync(CancellationToken cancellationToken)
    {
        return await _httpClient.{|#0:GetByteArrayAsync|}(""https://api.example.com"");
    }
}";

        var expected = new DiagnosticResult("CC004", Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("GetByteArrayAsync", "cancellationToken");

        await CreateTest(test, expected).RunAsync();
    }

    [Fact]
    public async Task GetStreamAsync_WithoutToken_WhenTokenAvailable_ShouldReportDiagnostic()
    {
        var test = @"
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    private readonly HttpClient _httpClient = new HttpClient();

    public async Task<Stream> FetchStreamAsync(CancellationToken cancellationToken)
    {
        return await _httpClient.{|#0:GetStreamAsync|}(""https://api.example.com"");
    }
}";

        var expected = new DiagnosticResult("CC004", Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("GetStreamAsync", "cancellationToken");

        await CreateTest(test, expected).RunAsync();
    }
}
