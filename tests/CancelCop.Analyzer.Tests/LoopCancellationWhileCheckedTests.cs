using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    CancelCop.Analyzer.LoopCancellationAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace CancelCop.Analyzer.Tests;

public class LoopCancellationWhileCheckedTests
{
    [Fact]
    public async Task WhileLoopWithCancellationCheck_ShouldNotReportDiagnostic()
    {
        // CC009 is satisfied when the loop observes cancellation. A while loop that calls
        // ThrowIfCancellationRequested() in its body must not be flagged.
        var test = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var i = 0;
        while (i < 10)
        {
            cancellationToken.ThrowIfCancellationRequested();
            i++;
            await Task.Yield();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
