using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    CancelCop.Analyzer.AsyncVoidLambdaAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace CancelCop.Analyzer.Tests;

public class AsyncVoidLambdaSyncActionTests
{
    [Fact]
    public async Task SyncLambdaAsAction_ShouldNotReportDiagnostic()
    {
        // CC024 flags only async lambdas converted to Action (async void). A synchronous lambda assigned
        // to an Action is fine and must not be flagged.
        var test = @"
using System;

public class TestClass
{
    public void Configure()
    {
        Action work = () => { };
        _ = work;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
