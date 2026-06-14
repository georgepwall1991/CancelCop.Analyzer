using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    CancelCop.Analyzer.ExplicitNoneTokenAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace CancelCop.Analyzer.Tests;

public class ExplicitNoneNoTokenInScopeTests
{
    [Fact]
    public async Task DefaultTokenWithNoTokenInScope_ShouldNotReportDiagnostic()
    {
        // CC012 only fires when an in-scope token exists to use instead. With no token in scope, passing
        // `default` is the only option and must not be flagged.
        var test = @"
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync()
    {
        await Task.Delay(100, default);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
