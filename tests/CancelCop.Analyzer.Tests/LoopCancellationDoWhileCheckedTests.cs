using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    CancelCop.Analyzer.LoopCancellationAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace CancelCop.Analyzer.Tests;

public class LoopCancellationDoWhileCheckedTests
{
    [Fact]
    public async Task DoWhileLoopWithCancellationCheck_ShouldNotReportDiagnostic()
    {
        // CC009 covers all four loop kinds. A do-while loop that observes cancellation in its body must
        // not be flagged.
        var test = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var i = 0;
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            i++;
            await Task.Yield();
        }
        while (i < 10);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
