using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    CancelCop.Analyzer.BlockingOnAsyncAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace CancelCop.Analyzer.Tests;

public class BlockingOnAsyncConfigureAwaitCleanTests
{
    [Fact]
    public async Task AwaitWithConfigureAwait_ShouldNotReportDiagnostic()
    {
        // CC015 flags blocking (.Result/.Wait()/GetResult). Awaiting with ConfigureAwait(false) is still
        // a proper await, not a blocking call, and must not be flagged.
        var test = @"
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync()
    {
        await GetValueAsync().ConfigureAwait(false);
    }

    private static Task GetValueAsync() => Task.CompletedTask;
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
