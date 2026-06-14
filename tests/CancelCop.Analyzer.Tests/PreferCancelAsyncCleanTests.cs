using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace CancelCop.Analyzer.Tests;

public class PreferCancelAsyncCleanTests
{
    [Fact]
    public async Task AwaitCancelAsync_ShouldNotReportDiagnostic()
    {
        // CC022 suggests CancelAsync() over the synchronous Cancel() in async code. Code that already
        // awaits CancelAsync() is the recommended shape and must not be flagged.
        var test = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(CancellationTokenSource cts)
    {
        await cts.CancelAsync();
    }
}";

        var t = new CSharpAnalyzerTest<PreferCancelAsyncAnalyzer, DefaultVerifier>
        {
            TestCode = test,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        await t.RunAsync();
    }
}
