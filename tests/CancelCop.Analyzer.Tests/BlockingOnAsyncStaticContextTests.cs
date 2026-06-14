using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    CancelCop.Analyzer.BlockingOnAsyncAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace CancelCop.Analyzer.Tests;

public class BlockingOnAsyncStaticContextTests
{
    [Fact]
    public async Task Result_InStaticAsyncMethod_ShouldReportDiagnostic()
    {
        // CC015 keys off the async context via IsInAsyncFunction, not token capture, so blocking on a
        // task with .Result inside a static async method is flagged exactly like an instance one.
        var test = @"
using System.Threading.Tasks;

public class TestClass
{
    public static async Task<int> RunAsync()
    {
        await Task.Yield();
        return GetValueAsync().{|#0:Result|};
    }

    private static Task<int> GetValueAsync() => Task.FromResult(1);
}";

        var expected = VerifyCS.Diagnostic("CC015").WithLocation(0).WithArguments(".Result");
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }
}
