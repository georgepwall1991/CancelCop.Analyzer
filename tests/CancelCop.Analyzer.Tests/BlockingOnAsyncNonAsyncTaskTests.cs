using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    CancelCop.Analyzer.BlockingOnAsyncAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace CancelCop.Analyzer.Tests;

public class BlockingOnAsyncNonAsyncTaskTests
{
    [Fact]
    public async Task Result_InNonAsyncTaskReturningMethod_ShouldNotReportDiagnostic()
    {
        // CC015 fires only inside an async function. A Task-returning method WITHOUT the async keyword
        // is not an async context, so blocking on .Result there is not flagged.
        var test = @"
using System.Threading.Tasks;

public class TestClass
{
    public Task<int> RunAsync()
    {
        return Task.FromResult(GetValueAsync().Result);
    }

    private static Task<int> GetValueAsync() => Task.FromResult(1);
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
