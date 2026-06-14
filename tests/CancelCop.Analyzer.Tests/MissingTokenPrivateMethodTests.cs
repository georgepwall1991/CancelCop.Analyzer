using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    CancelCop.Analyzer.MissingCancellationTokenAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace CancelCop.Analyzer.Tests;

public class MissingTokenPrivateMethodTests
{
    [Fact]
    public async Task PrivateAsyncMethodWithoutToken_ShouldNotReportDiagnostic()
    {
        // CC001 only guards the public/protected surface (entry points). A private async method without
        // a CancellationToken is an internal detail and must not be flagged.
        var test = @"
using System.Threading.Tasks;

public class TestClass
{
    private async Task RunAsync()
    {
        await Task.Yield();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
