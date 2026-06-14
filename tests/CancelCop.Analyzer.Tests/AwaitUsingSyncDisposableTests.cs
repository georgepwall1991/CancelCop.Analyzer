using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace CancelCop.Analyzer.Tests;

public class AwaitUsingSyncDisposableTests
{
    [Fact]
    public async Task UsingOverSyncDisposable_ShouldNotReportDiagnostic()
    {
        // CC025 only suggests `await using` for an IAsyncDisposable. A plain `using` over a purely
        // synchronous IDisposable is correct and must not be flagged.
        var test = @"
using System;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(IDisposable resource)
    {
        using (resource)
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
