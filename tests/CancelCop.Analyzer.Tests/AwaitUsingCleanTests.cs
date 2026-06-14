using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace CancelCop.Analyzer.Tests;

public class AwaitUsingCleanTests
{
    [Fact]
    public async Task AwaitUsingOverAsyncDisposable_ShouldNotReportDiagnostic()
    {
        // CC025 flags a plain `using` over an IAsyncDisposable in async code. When the code already uses
        // `await using`, it is the recommended shape and must not be flagged.
        var test = @"
using System;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(IAsyncDisposable resource)
    {
        await using (resource)
        {
            await Task.Yield();
        }
    }
}";

        var t = new CSharpAnalyzerTest<AwaitUsingAnalyzer, DefaultVerifier>
        {
            TestCode = test,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        await t.RunAsync();
    }
}
