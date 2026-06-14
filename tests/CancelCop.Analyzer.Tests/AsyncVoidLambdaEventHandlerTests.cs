using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    CancelCop.Analyzer.AsyncVoidLambdaAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace CancelCop.Analyzer.Tests;

public class AsyncVoidLambdaEventHandlerTests
{
    [Fact]
    public async Task AsyncLambdaAsEventHandler_ShouldNotReportDiagnostic()
    {
        // CC024 flags async lambdas converted to Action. An async lambda converted to EventHandler is the
        // accepted event-handler pattern (not Action) and must not be flagged.
        var test = @"
using System;
using System.Threading.Tasks;

public class TestClass
{
    public void Configure()
    {
        EventHandler handler = async (sender, e) => { await Task.Yield(); };
        _ = handler;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
