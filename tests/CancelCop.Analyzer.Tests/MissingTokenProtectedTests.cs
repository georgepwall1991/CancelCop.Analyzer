using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    CancelCop.Analyzer.MissingCancellationTokenAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace CancelCop.Analyzer.Tests;

public class MissingTokenProtectedTests
{
    [Fact]
    public async Task ProtectedAsyncMethodWithToken_ShouldNotReportDiagnostic()
    {
        // CC001 guards the protected surface as well as public. A protected async method that already has
        // a CancellationToken satisfies the rule and must not be flagged.
        var test = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    protected async Task RunAsync(CancellationToken cancellationToken)
    {
        await Task.Yield();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
