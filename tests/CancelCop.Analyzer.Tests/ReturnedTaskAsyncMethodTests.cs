using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    CancelCop.Analyzer.ReturnedTaskUsingDisposedAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace CancelCop.Analyzer.Tests;

public class ReturnedTaskAsyncMethodTests
{
    [Fact]
    public async Task AsyncMethodAwaitingUsingResource_ShouldNotReportDiagnostic()
    {
        // CC027 flags a NON-async Task-returning method that returns a call on a using-declared local
        // (the resource disposes before the task completes). An async method that awaits the call keeps
        // the resource alive until completion, so it must not be flagged.
        var test = @"
using System;
using System.Threading.Tasks;

public class TestClass
{
    private sealed class Resource : IDisposable
    {
        public Task<int> GetValueAsync() => Task.FromResult(1);
        public void Dispose() { }
    }

    public async Task<int> RunAsync()
    {
        using var r = new Resource();
        return await r.GetValueAsync();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
