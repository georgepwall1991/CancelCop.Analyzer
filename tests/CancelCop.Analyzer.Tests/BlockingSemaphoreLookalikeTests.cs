using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    CancelCop.Analyzer.BlockingSemaphoreAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace CancelCop.Analyzer.Tests;

public class BlockingSemaphoreLookalikeTests
{
    [Fact]
    public async Task UserDefinedWait_ShouldNotReportDiagnostic()
    {
        // CC026 is symbol-resolved to System.Threading.SemaphoreSlim.Wait. A user-defined type that
        // happens to expose a parameterless Wait() is a different symbol and must stay clean.
        var test = @"
using System.Threading.Tasks;

public class TestClass
{
    private sealed class Gate
    {
        public void Wait() { }
    }

    public async Task RunAsync()
    {
        var g = new Gate();
        g.Wait();
        await Task.Yield();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
