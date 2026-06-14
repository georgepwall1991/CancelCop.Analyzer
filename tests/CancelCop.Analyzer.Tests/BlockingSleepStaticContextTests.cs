using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    CancelCop.Analyzer.BlockingSleepAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace CancelCop.Analyzer.Tests;

public class BlockingSleepStaticContextTests
{
    [Fact]
    public async Task ThreadSleep_InStaticAsyncMethod_ShouldReportDiagnostic()
    {
        // CC013 keys off the async context plus the Thread.Sleep symbol, not token capture, so a
        // static async method is flagged exactly like an instance one (unlike the propagation rules,
        // a static function does not suppress this blocking-in-async check).
        var test = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public static async Task RunAsync()
    {
        {|#0:Thread.Sleep(1000)|};
        await Task.Yield();
    }
}";

        var expected = VerifyCS.Diagnostic("CC013").WithLocation(0);
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }
}
