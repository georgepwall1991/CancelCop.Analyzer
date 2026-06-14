using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    CancelCop.Analyzer.LoopCancellationAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace CancelCop.Analyzer.Tests;

public class LoopCancellationForCheckedTests
{
    [Fact]
    public async Task ForLoopWithCancellationCheck_ShouldNotReportDiagnostic()
    {
        // CC009 covers all four loop kinds. A for loop that observes cancellation in its body must not
        // be flagged.
        var test = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        for (var i = 0; i < 10; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
