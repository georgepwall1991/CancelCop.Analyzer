using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    CancelCop.Analyzer.BlockingSemaphoreAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace CancelCop.Analyzer.Tests;

public class BlockingSemaphoreStaticContextTests
{
    [Fact]
    public async Task Wait_InStaticAsyncMethod_ShouldReportDiagnostic()
    {
        // CC026 keys off the async context, not token capture, so a blocking SemaphoreSlim.Wait()
        // inside a static async method is flagged exactly like an instance one.
        var test = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    private static readonly SemaphoreSlim gate = new SemaphoreSlim(1, 1);

    public static async Task RunAsync()
    {
        gate.{|#0:Wait|}();
        await Task.Yield();
    }
}";

        var expected = VerifyCS.Diagnostic("CC026").WithLocation(0);
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }
}
