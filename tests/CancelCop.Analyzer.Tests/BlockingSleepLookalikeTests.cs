using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    CancelCop.Analyzer.BlockingSleepAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace CancelCop.Analyzer.Tests;

public class BlockingSleepLookalikeTests
{
    [Fact]
    public async Task UserDefinedThreadSleep_ShouldNotReportDiagnostic()
    {
        // CC013 is symbol-resolved to System.Threading.Thread.Sleep. A user-defined 'Thread' type with
        // a Sleep method (no 'using System.Threading;') is a different symbol and must stay clean.
        var test = @"
using System.Threading.Tasks;

public class TestClass
{
    private static class Thread
    {
        public static void Sleep(int ms) { }
    }

    public async Task RunAsync()
    {
        Thread.Sleep(100);
        await Task.Yield();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
