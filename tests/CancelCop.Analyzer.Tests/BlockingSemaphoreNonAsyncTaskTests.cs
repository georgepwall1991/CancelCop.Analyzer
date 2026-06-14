using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    CancelCop.Analyzer.BlockingSemaphoreAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace CancelCop.Analyzer.Tests;

public class BlockingSemaphoreNonAsyncTaskTests
{
    [Fact]
    public async Task Wait_InNonAsyncTaskReturningMethod_ShouldNotReportDiagnostic()
    {
        // CC026 fires only inside an async function. A Task-returning method WITHOUT the async keyword
        // is not an async context, so a blocking SemaphoreSlim.Wait() there is not flagged.
        var test = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    private static readonly SemaphoreSlim gate = new SemaphoreSlim(1, 1);

    public Task RunAsync()
    {
        gate.Wait();
        return Task.CompletedTask;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
