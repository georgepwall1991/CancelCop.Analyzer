using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    CancelCop.Analyzer.BlockingOnAsyncAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace CancelCop.Analyzer.Tests;

public class BlockingOnAsyncLookalikeTests
{
    [Fact]
    public async Task Result_OnNonTaskType_ShouldNotReportDiagnostic()
    {
        // CC015 is symbol-resolved to Task/Task<T>/ValueTask. A user-defined type that exposes a
        // 'Result' property is a different symbol and must stay clean even inside async code.
        var test = @"
using System.Threading.Tasks;

public class TestClass
{
    private sealed class Box
    {
        public int Result => 42;
    }

    public async Task<int> RunAsync()
    {
        await Task.Yield();
        return new Box().Result;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
