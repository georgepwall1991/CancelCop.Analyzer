using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    CancelCop.Analyzer.SwallowedCancellationAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace CancelCop.Analyzer.Tests;

public class SwallowedCancellationSpecificTypeTests
{
    [Fact]
    public async Task CatchSpecificException_ShouldNotReportDiagnostic()
    {
        // CC019 only flags a catch-all / catch (Exception). A catch for a specific exception type cannot
        // swallow OperationCanceledException and must not be flagged.
        var test = @"
using System;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync()
    {
        try
        {
            await Task.Yield();
        }
        catch (InvalidOperationException)
        {
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
