using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    CancelCop.Analyzer.BlockingOnAsyncAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace CancelCop.Analyzer.Tests;

public class BlockingOnAsyncAwaitedCleanTests
{
    [Fact]
    public async Task AwaitingTheTask_ShouldNotReportDiagnostic()
    {
        // CC015 flags blocking on a task (.Result/.Wait()/GetResult). Awaiting it instead is exactly the
        // correct shape and must not be flagged.
        var test = @"
using System.Threading.Tasks;

public class TestClass
{
    public async Task<int> RunAsync()
    {
        return await GetValueAsync();
    }

    private static Task<int> GetValueAsync() => Task.FromResult(1);
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
