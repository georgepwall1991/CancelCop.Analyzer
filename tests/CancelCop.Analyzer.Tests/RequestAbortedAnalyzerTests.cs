using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    CancelCop.Analyzer.RequestAbortedAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace CancelCop.Analyzer.Tests;

public class RequestAbortedAnalyzerTests
{
    // A faithful stand-in for Microsoft.AspNetCore.Http.HttpContext (the analyzer gates on the
    // parameter type's name + namespace), so the tests need no ASP.NET Core package.
    private const string ContextStub = @"
namespace Microsoft.AspNetCore.Http
{
    public abstract class HttpContext
    {
        public System.Threading.CancellationToken RequestAborted => default;
    }
}";

    [Fact]
    public async Task Method_IgnoresRequestAborted_ShouldReportDiagnostic()
    {
        var test = @"
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

public class Middleware
{
    private Task SaveAsync() => Task.CompletedTask;

    public async Task InvokeAsync(HttpContext {|#0:context|})
    {
        await SaveAsync();
    }
}" + ContextStub;

        var expected = VerifyCS.Diagnostic("CC021").WithLocation(0).WithArguments("context");
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task Method_ObservesRequestAborted_ShouldNotReportDiagnostic()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

public class Middleware
{
    private Task SaveAsync(CancellationToken token) => Task.CompletedTask;

    public async Task InvokeAsync(HttpContext context)
    {
        await SaveAsync(context.RequestAborted);
    }
}" + ContextStub;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task Method_NullConditionallyObservesRequestAborted_ShouldNotReportDiagnostic()
    {
        var test = @"
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

public class Middleware
{
    public async Task InvokeAsync(HttpContext context)
    {
        _ = context?.RequestAborted;
        await Task.Yield();
    }
}" + ContextStub;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task Method_NullConditionallyReadsRequestAbortedMember_ShouldNotReportDiagnostic()
    {
        var test = @"
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

public class Middleware
{
    public async Task InvokeAsync(HttpContext context)
    {
        _ = context?.RequestAborted.IsCancellationRequested;
        await Task.Yield();
    }
}" + ContextStub;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task Method_ObservesRequestAbortedViaAlias_ShouldNotReportDiagnostic()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

public class Middleware
{
    private Task SaveAsync(CancellationToken token) => Task.CompletedTask;

    public async Task InvokeAsync(HttpContext context)
    {
        var token = context.RequestAborted;
        await SaveAsync(token);
    }
}" + ContextStub;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task Method_NameofRequestAbortedOnly_ShouldReportDiagnostic()
    {
        var test = @"
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

public class Middleware
{
    public async Task InvokeAsync(HttpContext {|#0:context|})
    {
        _ = nameof(context.RequestAborted);
        await Task.Yield();
    }
}" + ContextStub;

        var expected = VerifyCS.Diagnostic("CC021").WithLocation(0).WithArguments("context");
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task Method_PassesContextOn_ShouldNotReportDiagnostic()
    {
        var test = @"
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

public class Middleware
{
    private Task Next(HttpContext c) => Task.CompletedTask;

    public async Task InvokeAsync(HttpContext context)
    {
        await Next(context);
    }
}" + ContextStub;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task Method_PassesContextAsReducedExtensionReceiver_ShouldNotReportDiagnostic()
    {
        var test = @"
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

public static class HttpContextExtensions
{
    public static Task ProcessAsync(this HttpContext context) => Task.CompletedTask;
}

public class Middleware
{
    public async Task InvokeAsync(HttpContext context)
    {
        await context.ProcessAsync();
    }
}" + ContextStub;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task Method_OrdinaryInstanceCall_ShouldReportDiagnostic()
    {
        var test = @"
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

public class Middleware
{
    public async Task InvokeAsync(HttpContext {|#0:context|})
    {
        _ = context.ToString();
        await Task.Yield();
    }
}" + ContextStub;

        var expected = VerifyCS.Diagnostic("CC021").WithLocation(0).WithArguments("context");
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task Method_NoAsyncWork_ShouldNotReportDiagnostic()
    {
        var test = @"
using Microsoft.AspNetCore.Http;

public class Middleware
{
    public void Invoke(HttpContext context)
    {
    }
}" + ContextStub;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task NonHttpContextMethod_ShouldNotReportDiagnostic()
    {
        var test = @"
using System.Threading.Tasks;

public class Service
{
    private Task SaveAsync() => Task.CompletedTask;

    public async Task RunAsync()
    {
        await SaveAsync();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
