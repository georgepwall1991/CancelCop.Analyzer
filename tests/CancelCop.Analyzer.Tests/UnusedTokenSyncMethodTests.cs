using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    CancelCop.Analyzer.UnusedTokenParameterAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace CancelCop.Analyzer.Tests;

public class UnusedTokenSyncMethodTests
{
    [Fact]
    public async Task SyncMethodWithUnusedToken_ShouldNotReportDiagnostic()
    {
        // CC016 only flags a method that does async work (has await) but ignores its token. A purely
        // synchronous method is out of scope and must not be flagged.
        var test = @"
using System.Threading;

public class TestClass
{
    public void Run(CancellationToken cancellationToken)
    {
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
