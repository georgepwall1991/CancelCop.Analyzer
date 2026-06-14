using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    CancelCop.Analyzer.AsyncVoidLambdaAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace CancelCop.Analyzer.Tests;

public class AsyncVoidLambdaFuncTaskTests
{
    [Fact]
    public async Task AsyncLambdaAsFuncTask_ShouldNotReportDiagnostic()
    {
        // CC024 flags an async lambda whose converted delegate type is Action (async void). A lambda
        // converted to Func<Task> is awaitable and must not be flagged.
        var test = @"
using System;
using System.Threading.Tasks;

public class TestClass
{
    public void Configure()
    {
        Func<Task> work = async () => { await Task.Yield(); };
        _ = work;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
