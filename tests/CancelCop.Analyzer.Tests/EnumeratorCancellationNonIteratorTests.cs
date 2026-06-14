using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace CancelCop.Analyzer.Tests;

public class EnumeratorCancellationNonIteratorTests
{
    [Fact]
    public async Task NonIteratorReturningAsyncEnumerable_ShouldNotReportDiagnostic()
    {
        // CC011 targets async iterators (methods with `yield`). A plain method that returns an
        // IAsyncEnumerable without yielding is not an iterator, so [EnumeratorCancellation] does not apply
        // and it must not be flagged.
        var test = @"
using System.Collections.Generic;
using System.Threading;

public class TestClass
{
    public IAsyncEnumerable<int> RunAsync(IAsyncEnumerable<int> source, CancellationToken cancellationToken)
    {
        return source;
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
