using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    CancelCop.Analyzer.BlockingSemaphoreAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace CancelCop.Analyzer.Tests;

public class BlockingSemaphoreTryFinallyTests
{
    [Fact]
    public async Task Wait_InTryFinallyGuard_InAsyncMethod_ShouldReportDiagnostic()
    {
        // The classic gate pattern (Wait in try / Release in finally) still blocks the thread in async
        // code, so CC028's sibling CC026 flags the synchronous Wait().
        var test = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    private static readonly SemaphoreSlim gate = new SemaphoreSlim(1, 1);

    public async Task RunAsync()
    {
        gate.{|#0:Wait|}();
        try
        {
            await Task.Yield();
        }
        finally
        {
            gate.Release();
        }
    }
}";

        var expected = VerifyCS.Diagnostic("CC026").WithLocation(0);
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }
}
