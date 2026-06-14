using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    CancelCop.Analyzer.ExplicitNoneTokenAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace CancelCop.Analyzer.Tests;

public class ExplicitNoneNoneNoTokenTests
{
    [Fact]
    public async Task NoneWithNoTokenInScope_ShouldNotReportDiagnostic()
    {
        // CC012 only fires when an in-scope token exists to use instead. Passing CancellationToken.None
        // when no token is available is the only option and must not be flagged.
        var test = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync()
    {
        await Task.Delay(100, CancellationToken.None);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
