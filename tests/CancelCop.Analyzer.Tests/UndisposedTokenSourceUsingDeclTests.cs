using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    CancelCop.Analyzer.UndisposedTokenSourceAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace CancelCop.Analyzer.Tests;

public class UndisposedTokenSourceUsingDeclTests
{
    [Fact]
    public async Task CtsInUsingDeclaration_ShouldNotReportDiagnostic()
    {
        // CC014 flags a CancellationTokenSource that is never disposed. A `using` declaration disposes
        // it deterministically, so it must not be flagged.
        var test = @"
using System.Threading;

public class TestClass
{
    public void Run()
    {
        using var cts = new CancellationTokenSource();
        _ = cts.Token;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
