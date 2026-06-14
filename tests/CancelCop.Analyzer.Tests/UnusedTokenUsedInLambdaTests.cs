using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    CancelCop.Analyzer.UnusedTokenParameterAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace CancelCop.Analyzer.Tests;

public class UnusedTokenUsedInLambdaTests
{
    [Fact]
    public async Task TokenReferencedInsideLambda_ShouldNotReportDiagnostic()
    {
        // CC016 counts a token referenced anywhere in the method body, including a nested lambda or local
        // function, as used. Such a method must not be flagged.
        var test = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await Task.Run(() => cancellationToken.ThrowIfCancellationRequested());
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
