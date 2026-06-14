using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    CancelCop.Analyzer.BlockingSleepAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace CancelCop.Analyzer.Tests;

public class BlockingSleepTaskDelayCleanTests
{
    [Fact]
    public async Task TaskDelay_InAsyncMethod_ShouldNotReportDiagnostic()
    {
        // CC013 targets Thread.Sleep specifically. The async-friendly await Task.Delay is the correct
        // replacement and must not be flagged by CC013.
        var test = @"
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync()
    {
        await Task.Delay(100);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
