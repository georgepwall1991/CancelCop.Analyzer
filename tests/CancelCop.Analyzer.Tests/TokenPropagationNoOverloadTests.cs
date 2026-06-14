using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    CancelCop.Analyzer.TokenPropagationAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace CancelCop.Analyzer.Tests;

public class TokenPropagationNoOverloadTests
{
    [Fact]
    public async Task CallWithNoTokenOverload_ShouldNotReportDiagnostic()
    {
        // CC002 only fires when the callee has an overload that accepts a CancellationToken. A method with
        // no such overload cannot receive the token, so nothing is flagged.
        var test = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await DoAsync();
    }

    private static Task DoAsync() => Task.CompletedTask;
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
