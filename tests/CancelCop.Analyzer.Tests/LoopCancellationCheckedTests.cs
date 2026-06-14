using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    CancelCop.Analyzer.LoopCancellationAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace CancelCop.Analyzer.Tests;

public class LoopCancellationCheckedTests
{
    [Fact]
    public async Task ForeachWithThrowIfCancellationRequested_ShouldNotReportDiagnostic()
    {
        // CC009 is satisfied when the loop body observes cancellation, so a foreach that calls
        // ThrowIfCancellationRequested() must not be flagged.
        var test = @"
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(IEnumerable<int> items, CancellationToken cancellationToken)
    {
        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
