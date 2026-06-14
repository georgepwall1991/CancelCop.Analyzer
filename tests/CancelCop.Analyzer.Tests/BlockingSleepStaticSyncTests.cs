using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    CancelCop.Analyzer.BlockingSleepAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace CancelCop.Analyzer.Tests;

public class BlockingSleepStaticSyncTests
{
    [Fact]
    public async Task ThreadSleep_InStaticSyncMethod_ShouldNotReportDiagnostic()
    {
        // CC013 only fires inside an async function. A static synchronous method may legitimately call
        // Thread.Sleep and must not be flagged.
        var test = @"
using System.Threading;

public class TestClass
{
    public static void Run()
    {
        Thread.Sleep(100);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
