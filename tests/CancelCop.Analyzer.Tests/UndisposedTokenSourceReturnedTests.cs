using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    CancelCop.Analyzer.UndisposedTokenSourceAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace CancelCop.Analyzer.Tests;

public class UndisposedTokenSourceReturnedTests
{
    [Fact]
    public async Task CtsReturnedToCaller_ShouldNotReportDiagnostic()
    {
        // CC014 only flags a CTS that is owned locally and never disposed. When it is returned, ownership
        // transfers to the caller, so it must not be flagged (escape analysis).
        var test = @"
using System.Threading;

public class TestClass
{
    public CancellationTokenSource Create()
    {
        var cts = new CancellationTokenSource();
        return cts;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
