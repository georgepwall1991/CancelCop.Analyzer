using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    CancelCop.Analyzer.AsyncVoidAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace CancelCop.Analyzer.Tests;

public class AsyncVoidCleanTests
{
    [Fact]
    public async Task AsyncTaskMethod_ShouldNotReportDiagnostic()
    {
        // CC023 flags async void (non-event-handler). An async method that returns Task is the correct
        // shape and must not be flagged.
        var test = @"
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync()
    {
        await Task.Yield();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
