using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS009 = Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixVerifier<
    CancelCop.Analyzer.LoopCancellationAnalyzer,
    CancelCop.Analyzer.LoopCancellationCodeFixProvider,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;
using VerifyCS010 = Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixVerifier<
    CancelCop.Analyzer.AsyncEnumerableCancellationAnalyzer,
    CancelCop.Analyzer.AsyncEnumerableCancellationCodeFixProvider,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;
using VerifyCS012 = Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixVerifier<
    CancelCop.Analyzer.ExplicitNoneTokenAnalyzer,
    CancelCop.Analyzer.ExplicitNoneTokenCodeFixProvider,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;
using VerifyCS029 = Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixVerifier<
    CancelCop.Analyzer.LinkedTimeoutTokenSourceAnalyzer,
    CancelCop.Analyzer.LinkedTimeoutTokenSourceCodeFixProvider,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;
using VerifyCS034 = Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixVerifier<
    CancelCop.Analyzer.ParallelOptionsTokenAnalyzer,
    CancelCop.Analyzer.ParallelOptionsTokenCodeFixProvider,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace CancelCop.Analyzer.Tests;

public class FrameworkTokenSourceSiblingTests
{
    [Fact]
    public async Task LoopInMiddleware_ReportsCC009_AndFixUsesRequestAborted()
    {
        var test = @"
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

public class Middleware
{
    public async Task InvokeAsync(HttpContext context, List<int> items)
    {
        {|#0:foreach|} (var item in items)
        {
            await Task.Delay(1, context.RequestAborted);
        }
    }
}" + FrameworkTokenSourceTests.HttpContextStub;

        var fixedCode = @"
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

public class Middleware
{
    public async Task InvokeAsync(HttpContext context, List<int> items)
    {
        foreach (var item in items)
        {
            context.RequestAborted.ThrowIfCancellationRequested();
            await Task.Delay(1, context.RequestAborted);
        }
    }
}" + FrameworkTokenSourceTests.HttpContextStub;

        await VerifyCS009.VerifyCodeFixAsync(
            test,
            VerifyCS009.Diagnostic("CC009").WithLocation(0).WithArguments("context.RequestAborted"),
            fixedCode);
    }

    [Fact]
    public async Task LoopCheckingRequestAborted_ShouldNotReportCC009()
    {
        var test = @"
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

public class Middleware
{
    public async Task InvokeAsync(HttpContext context, List<int> items)
    {
        foreach (var item in items)
        {
            context.RequestAborted.ThrowIfCancellationRequested();
            await Task.Delay(1, context.RequestAborted);
        }
    }
}" + FrameworkTokenSourceTests.HttpContextStub;

        await VerifyCS009.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TimeoutCtsInMiddleware_LinksRequestAborted()
    {
        var test = @"
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

public class Middleware
{
    private Task DoAsync(CancellationToken token) => Task.CompletedTask;

    public async Task InvokeAsync(HttpContext context)
    {
        using var cts = {|#0:new CancellationTokenSource(TimeSpan.FromSeconds(30))|};
        await DoAsync(cts.Token);
    }
}" + FrameworkTokenSourceTests.HttpContextStub;

        var fixedCode = @"
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

public class Middleware
{
    private Task DoAsync(CancellationToken token) => Task.CompletedTask;

    public async Task InvokeAsync(HttpContext context)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        cts.CancelAfter(TimeSpan.FromSeconds(30));
        await DoAsync(cts.Token);
    }
}" + FrameworkTokenSourceTests.HttpContextStub;

        await VerifyCS029.VerifyCodeFixAsync(
            test,
            VerifyCS029.Diagnostic("CC029").WithLocation(0).WithArguments("context.RequestAborted"),
            fixedCode);
    }

    [Fact]
    public async Task ParallelOptionsInMiddleware_SetsRequestAborted()
    {
        var test = @"
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

public class Middleware
{
    public async Task InvokeAsync(HttpContext context, int[] items)
    {
        var options = {|#0:new ParallelOptions { MaxDegreeOfParallelism = 4 }|};
        Parallel.ForEach(items, options, i => { });
        await Task.Yield();
    }
}" + FrameworkTokenSourceTests.HttpContextStub;

        var fixedCode = @"
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

public class Middleware
{
    public async Task InvokeAsync(HttpContext context, int[] items)
    {
        var options = new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = context.RequestAborted };
        Parallel.ForEach(items, options, i => { });
        await Task.Yield();
    }
}" + FrameworkTokenSourceTests.HttpContextStub;

        await VerifyCS034.VerifyCodeFixAsync(
            test,
            VerifyCS034.Diagnostic("CC034").WithLocation(0).WithArguments("context.RequestAborted"),
            fixedCode);
    }

    [Fact]
    public async Task AwaitForeachInMiddleware_FlowsRequestAborted()
    {
        var test = @"
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

public class Middleware
{
    public async Task InvokeAsync(HttpContext context, IAsyncEnumerable<int> source)
    {
        await foreach (var item in {|#0:source|})
        {
        }
    }
}" + FrameworkTokenSourceTests.HttpContextStub;

        var fixedCode = @"
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

public class Middleware
{
    public async Task InvokeAsync(HttpContext context, IAsyncEnumerable<int> source)
    {
        await foreach (var item in source.WithCancellation(context.RequestAborted))
        {
        }
    }
}" + FrameworkTokenSourceTests.HttpContextStub;

        await VerifyCS010.VerifyCodeFixAsync(
            test,
            VerifyCS010.Diagnostic("CC010").WithLocation(0).WithArguments("context.RequestAborted"),
            fixedCode);
    }

    [Fact]
    public async Task ExplicitNoneInMiddleware_ReplacedWithRequestAborted()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

public class Middleware
{
    private Task DoAsync(CancellationToken token) => Task.CompletedTask;

    public async Task InvokeAsync(HttpContext context)
    {
        await DoAsync({|#0:CancellationToken.None|});
    }
}" + FrameworkTokenSourceTests.HttpContextStub;

        var fixedCode = @"
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

public class Middleware
{
    private Task DoAsync(CancellationToken token) => Task.CompletedTask;

    public async Task InvokeAsync(HttpContext context)
    {
        await DoAsync(context.RequestAborted);
    }
}" + FrameworkTokenSourceTests.HttpContextStub;

        await VerifyCS012.VerifyCodeFixAsync(
            test,
            VerifyCS012.Diagnostic("CC012").WithLocation(0).WithArguments("CancellationToken.None", "context.RequestAborted"),
            fixedCode);
    }
}
