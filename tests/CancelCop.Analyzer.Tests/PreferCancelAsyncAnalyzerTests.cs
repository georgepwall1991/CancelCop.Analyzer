using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace CancelCop.Analyzer.Tests;

public class PreferCancelAsyncAnalyzerTests
{
    private static CSharpAnalyzerTest<PreferCancelAsyncAnalyzer, DefaultVerifier> CreateTest(
        string testCode, params DiagnosticResult[] expected)
    {
        var test = new CSharpAnalyzerTest<PreferCancelAsyncAnalyzer, DefaultVerifier>
        {
            TestCode = testCode,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        test.ExpectedDiagnostics.AddRange(expected);
        return test;
    }

    [Fact]
    public async Task Cancel_OnFieldSource_ShouldReportDiagnostic()
    {
        // Receiver-agnostic: a parameterless Cancel() on a CancellationTokenSource field is flagged
        // exactly like one on a parameter.
        var test = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    private readonly CancellationTokenSource _cts = new CancellationTokenSource();

    public async Task StopAsync()
    {
        _cts.{|#0:Cancel|}();
        await Task.Yield();
    }
}";

        var expected = new DiagnosticResult("CC022", DiagnosticSeverity.Info).WithLocation(0);
        await CreateTest(test, expected).RunAsync();
    }

    [Fact]
    public async Task Cancel_InAsyncMethod_ShouldReportDiagnostic()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task StopAsync(CancellationTokenSource cts)
    {
        cts.{|#0:Cancel|}();
        await Task.Yield();
    }
}";

        var expected = new DiagnosticResult("CC022", DiagnosticSeverity.Info).WithLocation(0);
        await CreateTest(test, expected).RunAsync();
    }

    [Fact]
    public async Task Cancel_InAsyncLambda_ShouldReportDiagnostic()
    {
        var test = @"
using System;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public void Register(CancellationTokenSource cts)
    {
        Func<Task> f = async () =>
        {
            cts.{|#0:Cancel|}();
            await Task.Yield();
        };
    }
}";

        var expected = new DiagnosticResult("CC022", DiagnosticSeverity.Info).WithLocation(0);
        await CreateTest(test, expected).RunAsync();
    }

    [Fact]
    public async Task Cancel_InAsyncLocalFunction_ShouldReportDiagnostic()
    {
        // The async-context check covers nested async local functions, like the lambda case.
        var test = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public void Configure(CancellationTokenSource cts)
    {
        async Task StopAsync()
        {
            cts.{|#0:Cancel|}();
            await Task.Yield();
        }

        _ = StopAsync();
    }
}";

        var expected = new DiagnosticResult("CC022", DiagnosticSeverity.Info).WithLocation(0);
        await CreateTest(test, expected).RunAsync();
    }

    [Fact]
    public async Task Cancel_InTopLevelAsyncProgram_ShouldReportDiagnostic()
    {
        var testCode = @"
using System.Threading;
using System.Threading.Tasks;

using var cts = new CancellationTokenSource();
cts.{|#0:Cancel|}();
await Task.Yield();";

        var expected = new DiagnosticResult("CC022", DiagnosticSeverity.Info).WithLocation(0);
        var test = CreateTest(testCode, expected);
        test.TestState.OutputKind = OutputKind.ConsoleApplication;
        await test.RunAsync();
    }

    [Fact]
    public async Task Cancel_InTopLevelSyncProgram_ShouldNotReportDiagnostic()
    {
        var testCode = @"
using System.Threading;

using var cts = new CancellationTokenSource();
cts.Cancel();";

        var test = CreateTest(testCode);
        test.TestState.OutputKind = OutputKind.ConsoleApplication;
        await test.RunAsync();
    }

    [Fact]
    public async Task Cancel_InSyncMethod_ShouldNotReportDiagnostic()
    {
        var test = @"
using System.Threading;

public class TestClass
{
    public void Stop(CancellationTokenSource cts)
    {
        cts.Cancel();
    }
}";

        await CreateTest(test).RunAsync();
    }

    [Fact]
    public async Task CancelWithArgument_ShouldNotReportDiagnostic()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task StopAsync(CancellationTokenSource cts)
    {
        cts.Cancel(true);
        await Task.Yield();
    }
}";

        await CreateTest(test).RunAsync();
    }

    [Fact]
    public async Task CancelOnNonTokenSource_ShouldNotReportDiagnostic()
    {
        var test = @"
using System.Threading.Tasks;

public class Custom { public void Cancel() { } }

public class TestClass
{
    public async Task StopAsync(Custom c)
    {
        c.Cancel();
        await Task.Yield();
    }
}";

        await CreateTest(test).RunAsync();
    }
}
