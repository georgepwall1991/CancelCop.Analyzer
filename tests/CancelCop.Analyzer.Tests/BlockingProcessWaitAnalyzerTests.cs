using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace CancelCop.Analyzer.Tests;

/// <summary>
/// CC030: <c>Process.WaitForExit()</c> blocks the calling thread until the child process ends —
/// unbounded, and by definition waiting on something outside the process. .NET 5 added
/// <c>WaitForExitAsync(CancellationToken)</c>.
/// </summary>
public class BlockingProcessWaitAnalyzerTests
{
    private static CSharpAnalyzerTest<BlockingProcessWaitAnalyzer, DefaultVerifier> Test(
        string source
    ) => new() { TestCode = source, ReferenceAssemblies = ReferenceAssemblies.Net.Net90 };

    [Fact]
    public async Task WaitForExit_InAsyncMethod_ShouldReportDiagnostic()
    {
        var test =
            @"
using System.Diagnostics;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(Process process)
    {
        process.{|#0:WaitForExit|}();
        await Task.Yield();
    }
}";

        var t = Test(test);
        t.ExpectedDiagnostics.Add(
            new DiagnosticResult("CC030", DiagnosticSeverity.Warning).WithLocation(0)
        );
        await t.RunAsync();
    }

    [Fact]
    public async Task WaitForExit_InAsyncLambda_ShouldReportDiagnostic()
    {
        var test =
            @"
using System;
using System.Diagnostics;
using System.Threading.Tasks;

public class TestClass
{
    public void Run(Process process)
    {
        Func<Task> f = async () =>
        {
            process.{|#0:WaitForExit|}();
            await Task.Yield();
        };
    }
}";

        var t = Test(test);
        t.ExpectedDiagnostics.Add(
            new DiagnosticResult("CC030", DiagnosticSeverity.Warning).WithLocation(0)
        );
        await t.RunAsync();
    }

    [Fact]
    public async Task WaitForExit_NullConditional_ShouldReportDiagnostic()
    {
        var test =
            @"
using System.Diagnostics;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(Process? process)
    {
        process?.{|#0:WaitForExit|}();
        await Task.Yield();
    }
}";

        var t = Test(test);
        t.ExpectedDiagnostics.Add(
            new DiagnosticResult("CC030", DiagnosticSeverity.Warning).WithLocation(0)
        );
        await t.RunAsync();
    }

    [Fact]
    public async Task WaitForExitWithTimeout_ShouldNotReportDiagnostic()
    {
        // WaitForExit(int) returns bool and has no async counterpart with that shape —
        // WaitForExitAsync only takes a token. There is no mechanical rewrite, so the rule stays
        // quiet rather than suggest something that changes the call's meaning.
        var test =
            @"
using System.Diagnostics;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(Process process)
    {
        process.WaitForExit(5000);
        await Task.Yield();
    }
}";

        await Test(test).RunAsync();
    }

    [Fact]
    public async Task WaitForExit_InSyncMethod_ShouldNotReportDiagnostic()
    {
        var test =
            @"
using System.Diagnostics;

public class TestClass
{
    public void Run(Process process)
    {
        process.WaitForExit();
    }
}";

        await Test(test).RunAsync();
    }

    [Fact]
    public async Task WaitForExit_InSyncLambdaInsideAsyncMethod_ShouldNotReportDiagnostic()
    {
        // The lambda is its own function and is not async, so no await can be inserted there.
        var test =
            @"
using System;
using System.Diagnostics;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(Process process)
    {
        Action a = () => process.WaitForExit();
        a();
        await Task.Yield();
    }
}";

        await Test(test).RunAsync();
    }

    [Fact]
    public async Task LookalikeWaitForExit_ShouldNotReportDiagnostic()
    {
        // Same method name, unrelated type. CC030 is symbol-gated.
        var test =
            @"
using System.Threading.Tasks;

public class Job
{
    public void WaitForExit() { }
}

public class TestClass
{
    public async Task RunAsync(Job job)
    {
        job.WaitForExit();
        await Task.Yield();
    }
}";

        await Test(test).RunAsync();
    }
}
