using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    CancelCop.Analyzer.AsyncVoidLambdaAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace CancelCop.Analyzer.Tests;

public class AsyncVoidLambdaTaskRunTests
{
    [Fact]
    public async Task AsyncLambdaInTaskRun_ShouldNotReportDiagnostic()
    {
        // CC024 flags async lambdas converted to Action. The lambda passed to Task.Run binds to
        // Func<Task> (it is awaited), so it must not be flagged.
        var test = @"
using System.Threading.Tasks;

public class TestClass
{
    public void Run()
    {
        _ = Task.Run(async () => { await Task.Yield(); });
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
