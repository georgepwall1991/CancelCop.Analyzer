using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    CancelCop.Analyzer.UndisposedTokenSourceAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace CancelCop.Analyzer.Tests;

public class UndisposedTokenSourcePassedTests
{
    [Fact]
    public async Task CtsPassedAsArgument_ShouldNotReportDiagnostic()
    {
        // CC014's escape analysis suppresses the diagnostic when the CTS is handed to another method,
        // which may take ownership or dispose it.
        var test = @"
using System.Threading;

public class TestClass
{
    public void Run()
    {
        var cts = new CancellationTokenSource();
        Register(cts);
    }

    private static void Register(CancellationTokenSource source)
    {
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
