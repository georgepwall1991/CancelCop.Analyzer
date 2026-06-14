using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    CancelCop.Analyzer.SwallowedCancellationAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace CancelCop.Analyzer.Tests;

public class SwallowedCancellationRethrowTests
{
    [Fact]
    public async Task CatchThatRethrows_ShouldNotReportDiagnostic()
    {
        // CC019 flags a broad catch over awaited code that swallows OperationCanceledException. A catch
        // that rethrows does not swallow anything and must not be flagged.
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
        catch (Exception)
        {
            throw;
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
