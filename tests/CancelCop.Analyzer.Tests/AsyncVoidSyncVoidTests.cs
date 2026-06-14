using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    CancelCop.Analyzer.AsyncVoidAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace CancelCop.Analyzer.Tests;

public class AsyncVoidSyncVoidTests
{
    [Fact]
    public async Task SynchronousVoidMethod_ShouldNotReportDiagnostic()
    {
        // CC023 flags async void specifically. A plain synchronous void method is fine and must not be
        // flagged.
        var test = @"
public class TestClass
{
    public void Run()
    {
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
