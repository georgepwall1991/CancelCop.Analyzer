using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    CancelCop.Analyzer.BlockingSleepAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace CancelCop.Analyzer.Tests;

public class BlockingSleepNonAsyncTaskTests
{
    [Fact]
    public async Task ThreadSleep_InNonAsyncTaskReturningMethod_ShouldNotReportDiagnostic()
    {
        // CC013 fires only inside an async function. A Task-returning method WITHOUT the async keyword
        // is not an async context, so a blocking Thread.Sleep there is not flagged.
        var test = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public Task RunAsync()
    {
        Thread.Sleep(100);
        return Task.CompletedTask;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
