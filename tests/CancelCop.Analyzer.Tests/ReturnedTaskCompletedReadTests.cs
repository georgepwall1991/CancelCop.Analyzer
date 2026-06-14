using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    CancelCop.Analyzer.ReturnedTaskUsingDisposedAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace CancelCop.Analyzer.Tests;

public class ReturnedTaskCompletedReadTests
{
    [Fact]
    public async Task CompletedTaskFromUsingResourceValue_ShouldNotReportDiagnostic()
    {
        // CC027 only flags a returned task whose receiver is the using-declared resource (so the task is
        // still running when the resource disposes). A synchronous read wrapped in Task.FromResult is
        // already complete at return, so it is safe and must not be flagged.
        var test = @"
using System;
using System.Threading.Tasks;

public class TestClass
{
    private sealed class Resource : IDisposable
    {
        public int Value => 42;
        public void Dispose() { }
    }

    public Task<int> RunAsync()
    {
        using var r = new Resource();
        return Task.FromResult(r.Value);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
