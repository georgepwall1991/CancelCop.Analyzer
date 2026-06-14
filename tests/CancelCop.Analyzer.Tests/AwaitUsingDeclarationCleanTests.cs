using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace CancelCop.Analyzer.Tests;

public class AwaitUsingDeclarationCleanTests
{
    [Fact]
    public async Task AwaitUsingDeclaration_ShouldNotReportDiagnostic()
    {
        // CC025 flags a plain `using` over an IAsyncDisposable. The `await using` declaration form is the
        // recommended shape and must not be flagged (complements the statement-form clean test).
        var test = @"
using System;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(IAsyncDisposable resource)
    {
        await using var r = resource;
        await Task.Yield();
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
