using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace CancelCop.Analyzer.Tests;

public class EnumeratorCancellationTokenlessTests
{
    [Fact]
    public async Task TokenlessAsyncIterator_ShouldNotReportDiagnostic()
    {
        // CC011 only fires when an async iterator HAS a CancellationToken parameter lacking the attribute.
        // An iterator with no token parameter has nothing to mark and must not be flagged.
        var test = @"
using System.Collections.Generic;
using System.Threading.Tasks;

public class TestClass
{
    public async IAsyncEnumerable<int> RunAsync()
    {
        yield return 1;
        await Task.Yield();
    }
}";

        var t = new CSharpAnalyzerTest<EnumeratorCancellationAnalyzer, DefaultVerifier>
        {
            TestCode = test,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        await t.RunAsync();
    }
}
