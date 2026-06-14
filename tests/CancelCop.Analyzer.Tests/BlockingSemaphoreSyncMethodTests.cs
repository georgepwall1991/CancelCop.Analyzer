using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    CancelCop.Analyzer.BlockingSemaphoreAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace CancelCop.Analyzer.Tests;

public class BlockingSemaphoreSyncMethodTests
{
    [Fact]
    public async Task Wait_InSyncMethod_ShouldNotReportDiagnostic()
    {
        // CC026 only fires inside an async function. A synchronous method may legitimately call
        // SemaphoreSlim.Wait() and must not be flagged.
        var test = @"
using System.Threading;

public class TestClass
{
    private static readonly SemaphoreSlim gate = new SemaphoreSlim(1, 1);

    public void Run()
    {
        gate.Wait();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
