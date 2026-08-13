using Microsoft.CodeAnalysis;
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
using VerifyCS013 = Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixVerifier<
    CancelCop.Analyzer.BlockingSleepAnalyzer,
    CancelCop.Analyzer.BlockingSleepCodeFixProvider,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;
using VerifyCS026 = Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixVerifier<
    CancelCop.Analyzer.BlockingSemaphoreAnalyzer,
    CancelCop.Analyzer.BlockingSemaphoreCodeFixProvider,
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

    [Fact]
    public async Task ThreadSleepInMiddleware_BecomesDelayWithRequestAborted()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

public class Middleware
{
    public async Task InvokeAsync(HttpContext context)
    {
        {|#0:Thread.Sleep(1000)|};
        await Task.Yield();
    }
}" + FrameworkTokenSourceTests.HttpContextStub;

        var fixedCode = @"
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

public class Middleware
{
    public async Task InvokeAsync(HttpContext context)
    {
        await Task.Delay(1000, context.RequestAborted);
        await Task.Yield();
    }
}" + FrameworkTokenSourceTests.HttpContextStub;

        await VerifyCS013.VerifyCodeFixAsync(
            test,
            VerifyCS013.Diagnostic("CC013").WithLocation(0),
            fixedCode);
    }

    [Fact]
    public async Task SemaphoreWaitInMiddleware_BecomesWaitAsyncWithRequestAborted()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

public class Middleware
{
    private readonly SemaphoreSlim _gate = new SemaphoreSlim(1);

    public async Task InvokeAsync(HttpContext context)
    {
        _gate.{|#0:Wait|}();
        await Task.Yield();
    }
}" + FrameworkTokenSourceTests.HttpContextStub;

        var fixedCode = @"
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

public class Middleware
{
    private readonly SemaphoreSlim _gate = new SemaphoreSlim(1);

    public async Task InvokeAsync(HttpContext context)
    {
        await _gate.WaitAsync(context.RequestAborted);
        await Task.Yield();
    }
}" + FrameworkTokenSourceTests.HttpContextStub;

        await VerifyCS026.VerifyCodeFixAsync(
            test,
            VerifyCS026.Diagnostic("CC026").WithLocation(0),
            fixedCode);
    }

    [Fact]
    public async Task FileReadAllTextInMiddleware_BecomesReadAllTextAsyncWithRequestAborted()
    {
        var testCode = @"
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

public class Middleware
{
    public async Task InvokeAsync(HttpContext context, string path)
    {
        var text = File.{|#0:ReadAllText|}(path);
        await Task.Yield();
    }
}" + FrameworkTokenSourceTests.HttpContextStub;

        var fixedCode = @"
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

public class Middleware
{
    public async Task InvokeAsync(HttpContext context, string path)
    {
        var text = await File.ReadAllTextAsync(path, context.RequestAborted);
        await Task.Yield();
    }
}" + FrameworkTokenSourceTests.HttpContextStub;

        var expected = new DiagnosticResult("CC028", DiagnosticSeverity.Warning)
            .WithLocation(0).WithArguments("ReadAllText");
        var test = new CSharpCodeFixTest<BlockingFileIoAnalyzer, BlockingFileIoCodeFixProvider, DefaultVerifier>
        {
            TestCode = testCode,
            FixedCode = fixedCode,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        test.ExpectedDiagnostics.Add(expected);
        await test.RunAsync();
    }

    [Fact]
    public async Task ProcessWaitForExitInMiddleware_FlowsRequestAborted()
    {
        var testCode = @"
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

public class Middleware
{
    public async Task InvokeAsync(HttpContext context, Process process)
    {
        process.{|#0:WaitForExit|}();
        await Task.Yield();
    }
}" + FrameworkTokenSourceTests.HttpContextStub;

        var fixedCode = @"
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

public class Middleware
{
    public async Task InvokeAsync(HttpContext context, Process process)
    {
        await process.WaitForExitAsync(context.RequestAborted);
        await Task.Yield();
    }
}" + FrameworkTokenSourceTests.HttpContextStub;

        var expected = new DiagnosticResult("CC030", DiagnosticSeverity.Warning).WithLocation(0);
        var test = new CSharpCodeFixTest<BlockingProcessWaitAnalyzer, BlockingProcessWaitCodeFixProvider, DefaultVerifier>
        {
            TestCode = testCode,
            FixedCode = fixedCode,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        test.ExpectedDiagnostics.Add(expected);
        await test.RunAsync();
    }
}
