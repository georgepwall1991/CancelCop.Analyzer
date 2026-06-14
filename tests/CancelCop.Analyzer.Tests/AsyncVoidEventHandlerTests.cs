using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    CancelCop.Analyzer.AsyncVoidAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace CancelCop.Analyzer.Tests;

public class AsyncVoidEventHandlerTests
{
    [Fact]
    public async Task AsyncVoidEventHandler_ShouldNotReportDiagnostic()
    {
        // CC023 deliberately exempts event handlers: an async void method with the (object, EventArgs)
        // shape is the one legitimate use of async void and must not be flagged.
        var test = @"
using System;
using System.Threading.Tasks;

public class TestClass
{
    public async void OnDone(object sender, EventArgs e)
    {
        await Task.Yield();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
