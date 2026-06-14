using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    CancelCop.Analyzer.AsyncEnumerableCancellationAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace CancelCop.Analyzer.Tests;

public class AsyncEnumerableSyncForeachTests
{
    [Fact]
    public async Task SynchronousForeach_ShouldNotReportDiagnostic()
    {
        // CC010 only concerns `await foreach` over an IAsyncEnumerable. A plain synchronous foreach over
        // an IEnumerable has no token to flow and must not be flagged.
        var test = @"
using System.Collections.Generic;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(IEnumerable<int> items)
    {
        foreach (var item in items)
        {
        }

        await Task.Yield();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
