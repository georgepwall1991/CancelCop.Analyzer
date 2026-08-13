using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    CancelCop.Analyzer.MissingCancellationTokenAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace CancelCop.Analyzer.Tests;

/// <summary>
/// Convention ASP.NET middleware <c>Invoke</c>/<c>InvokeAsync</c> is not an interface member, so
/// CC001 would otherwise suggest adding a <c>CancellationToken</c> parameter that DI does not
/// inject. Cancellation there is <c>HttpContext.RequestAborted</c>.
/// </summary>
public class MissingCancellationTokenMiddlewareTests
{
    [Fact]
    public async Task ConventionInvokeAsync_ShouldNotReportCC001()
    {
        var test = @"
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

public class Middleware
{
    public async Task InvokeAsync(HttpContext context)
    {
        await Task.Delay(1, context.RequestAborted);
    }
}" + FrameworkTokenSourceTests.HttpContextStub;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ConventionInvoke_ShouldNotReportCC001()
    {
        var test = @"
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

public class Middleware
{
    public async Task Invoke(HttpContext context)
    {
        await Task.Delay(1, context.RequestAborted);
    }
}" + FrameworkTokenSourceTests.HttpContextStub;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task IMiddlewareInvokeAsync_ShouldNotReportCC001()
    {
        var test = @"
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace Microsoft.AspNetCore.Http
{
    public interface IMiddleware
    {
        Task InvokeAsync(HttpContext context);
    }
}

public class Middleware : IMiddleware
{
    public async Task InvokeAsync(HttpContext context)
    {
        await Task.Delay(1, context.RequestAborted);
    }
}" + FrameworkTokenSourceTests.HttpContextStub;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task OrdinaryPublicAsync_StillReportsCC001()
    {
        var test = @"
using System.Threading.Tasks;

public class TestClass
{
    public async Task {|#0:WorkAsync|}()
    {
        await Task.Delay(1);
    }
}";

        var expected = VerifyCS.Diagnostic("CC001")
            .WithLocation(0)
            .WithArguments("WorkAsync");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task HandleAsyncWithHttpContext_StillReportsCC001()
    {
        var test = @"
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

public class TestClass
{
    public async Task {|#0:HandleAsync|}(HttpContext context)
    {
        await Task.Delay(1, context.RequestAborted);
    }
}" + FrameworkTokenSourceTests.HttpContextStub;

        var expected = VerifyCS.Diagnostic("CC001")
            .WithLocation(0)
            .WithArguments("HandleAsync");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task InvokeAsync_HttpContextNotFirstParameter_StillReportsCC001()
    {
        var test = @"
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

public class TestClass
{
    public async Task {|#0:InvokeAsync|}(string name, HttpContext context)
    {
        await Task.Delay(1, context.RequestAborted);
    }
}" + FrameworkTokenSourceTests.HttpContextStub;

        var expected = VerifyCS.Diagnostic("CC001")
            .WithLocation(0)
            .WithArguments("InvokeAsync");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task InvokeAsync_LookalikeHttpContext_StillReportsCC001()
    {
        var test = @"
using System.Threading.Tasks;

namespace Other.Http
{
    public class HttpContext
    {
        public System.Threading.CancellationToken RequestAborted => default;
    }
}

public class TestClass
{
    public async Task {|#0:InvokeAsync|}(Other.Http.HttpContext context)
    {
        await Task.Delay(1);
    }
}";

        var expected = VerifyCS.Diagnostic("CC001")
            .WithLocation(0)
            .WithArguments("InvokeAsync");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task StaticInvokeAsync_StillReportsCC001()
    {
        var test = @"
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

public class TestClass
{
    public static async Task {|#0:InvokeAsync|}(HttpContext context)
    {
        await Task.Delay(1, context.RequestAborted);
    }
}" + FrameworkTokenSourceTests.HttpContextStub;

        var expected = VerifyCS.Diagnostic("CC001")
            .WithLocation(0)
            .WithArguments("InvokeAsync");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task ProtectedInvokeAsync_StillReportsCC001()
    {
        var test = @"
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

public class TestClass
{
    protected async Task {|#0:InvokeAsync|}(HttpContext context)
    {
        await Task.Delay(1, context.RequestAborted);
    }
}" + FrameworkTokenSourceTests.HttpContextStub;

        var expected = VerifyCS.Diagnostic("CC001")
            .WithLocation(0)
            .WithArguments("InvokeAsync");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task InvokeAsync_NoParameters_StillReportsCC001()
    {
        var test = @"
using System.Threading.Tasks;

public class TestClass
{
    public async Task {|#0:InvokeAsync|}()
    {
        await Task.Delay(1);
    }
}";

        var expected = VerifyCS.Diagnostic("CC001")
            .WithLocation(0)
            .WithArguments("InvokeAsync");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task InvokeAsync_HttpContextThenNextDelegate_ShouldNotReportCC001()
    {
        var test = @"
using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

public class Middleware
{
    public async Task InvokeAsync(HttpContext context, Func<Task> next)
    {
        await Task.Delay(1, context.RequestAborted);
        await next();
    }
}" + FrameworkTokenSourceTests.HttpContextStub;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
