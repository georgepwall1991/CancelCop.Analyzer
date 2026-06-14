using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    CancelCop.Analyzer.TokenPropagationAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace CancelCop.Analyzer.Tests;

public class TokenPropagationWhenAllTests
{
    [Fact]
    public async Task TaskWhenAll_ShouldNotReportDiagnostic()
    {
        // Task.WhenAll has no CancellationToken overload, so CC002 has nothing to propagate and must not
        // flag it.
        var test = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(CancellationToken cancellationToken, Task first, Task second)
    {
        await Task.WhenAll(first, second);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
