using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace CancelCop.Analyzer.Tests;

public class PreferCancelAsyncCodeFixTests
{
    private static CSharpCodeFixTest<PreferCancelAsyncAnalyzer, PreferCancelAsyncCodeFixProvider, DefaultVerifier> CreateTest(
        string testCode, string fixedCode, params DiagnosticResult[] expected)
    {
        var test = new CSharpCodeFixTest<PreferCancelAsyncAnalyzer, PreferCancelAsyncCodeFixProvider, DefaultVerifier>
        {
            TestCode = testCode,
            FixedCode = fixedCode,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        test.ExpectedDiagnostics.AddRange(expected);
        return test;
    }

    [Fact]
    public async Task FixAll_TwoCancels_BothBecomeAwaitCancelAsync()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task StopAsync(CancellationTokenSource cts)
    {
        cts.{|#0:Cancel|}();
        cts.{|#1:Cancel|}();
        await Task.Yield();
    }
}";

        var fixedCode = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task StopAsync(CancellationTokenSource cts)
    {
        await cts.CancelAsync();
        await cts.CancelAsync();
        await Task.Yield();
    }
}";

        await CreateTest(
            test,
            fixedCode,
            new DiagnosticResult("CC022", DiagnosticSeverity.Info).WithLocation(0),
            new DiagnosticResult("CC022", DiagnosticSeverity.Info).WithLocation(1))
            .RunAsync();
    }

    [Fact]
    public async Task Cancel_BecomesAwaitCancelAsync()
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

        var fixedCode = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task StopAsync(CancellationTokenSource cts)
    {
        await cts.CancelAsync();
        await Task.Yield();
    }
}";

        var expected = new DiagnosticResult("CC022", DiagnosticSeverity.Info).WithLocation(0);
        await CreateTest(test, fixedCode, expected).RunAsync();
    }
}
