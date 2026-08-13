using System.Collections.Immutable;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixVerifier<
    CancelCop.Analyzer.TokenPropagationAnalyzer,
    CancelCop.Analyzer.TokenPropagationCodeFixProvider,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace CancelCop.Analyzer.Tests;

public class FrameworkTokenSourceCodeFixTests
{
    [Fact]
    public async Task DelayInMiddleware_PassesRequestAborted()
    {
        var test = @"
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

public class Middleware
{
    public async Task InvokeAsync(HttpContext context)
    {
        await Task.{|#0:Delay|}(100);
    }
}" + FrameworkTokenSourceTests.HttpContextStub;

        var fixedCode = @"
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

public class Middleware
{
    public async Task InvokeAsync(HttpContext context)
    {
        await Task.Delay(100, context.RequestAborted);
    }
}" + FrameworkTokenSourceTests.HttpContextStub;

        await VerifyCS.VerifyCodeFixAsync(
            test,
            VerifyCS.Diagnostic("CC002").WithLocation(0).WithArguments("Delay", "context.RequestAborted"),
            fixedCode);
    }

    [Fact]
    public async Task GetStringAsyncInMiddleware_PassesRequestAborted()
    {
        var test = @"
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

public class Middleware
{
    private readonly HttpClient _http = new HttpClient();

    public async Task InvokeAsync(HttpContext context)
    {
        await _http.{|#0:GetStringAsync|}(""https://api.example.com"");
    }
}" + FrameworkTokenSourceTests.HttpContextStub;

        var fixedCode = @"
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

public class Middleware
{
    private readonly HttpClient _http = new HttpClient();

    public async Task InvokeAsync(HttpContext context)
    {
        await _http.GetStringAsync(""https://api.example.com"", context.RequestAborted);
    }
}" + FrameworkTokenSourceTests.HttpContextStub;

        var t = new CSharpCodeFixTest<HttpClientAnalyzer, HttpClientCodeFixProvider, DefaultVerifier>
        {
            TestCode = test,
            FixedCode = fixedCode,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90
                .AddPackages(ImmutableArray.Create(new PackageIdentity("Microsoft.Extensions.Http", "9.0.0"))),
        };
        t.ExpectedDiagnostics.Add(
            new DiagnosticResult("CC004", Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
                .WithLocation(0)
                .WithArguments("GetStringAsync", "context.RequestAborted"));
        await t.RunAsync();
    }

    [Fact]
    public async Task DelayInGrpcMethod_PassesContextCancellationToken()
    {
        var test = @"
using System.Threading.Tasks;
using Grpc.Core;

public class GreeterService
{
    public async Task<string> SayHello(string request, ServerCallContext context)
    {
        await Task.{|#0:Delay|}(100);
        return ""hi"";
    }
}" + FrameworkTokenSourceTests.GrpcContextStub;

        var fixedCode = @"
using System.Threading.Tasks;
using Grpc.Core;

public class GreeterService
{
    public async Task<string> SayHello(string request, ServerCallContext context)
    {
        await Task.Delay(100, context.CancellationToken);
        return ""hi"";
    }
}" + FrameworkTokenSourceTests.GrpcContextStub;

        await VerifyCS.VerifyCodeFixAsync(
            test,
            VerifyCS.Diagnostic("CC002").WithLocation(0).WithArguments("Delay", "context.CancellationToken"),
            fixedCode);
    }
}
