using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    CancelCop.Analyzer.SwallowedCancellationAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace CancelCop.Analyzer.Tests;

public class SwallowedCancellationWhenFilterTests
{
    [Fact]
    public async Task CatchWithWhenFilter_ShouldNotReportDiagnostic()
    {
        // CC019 only flags an unfiltered broad catch. A `when` filter that lets cancellation propagate
        // means the catch is intentional and must not be flagged.
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
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
