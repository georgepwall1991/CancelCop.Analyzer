using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace CancelCop.Analyzer.Tests;

public class EnumeratorCancellationCleanTests
{
    [Fact]
    public async Task MarkedIteratorToken_ShouldNotReportDiagnostic()
    {
        // CC011 flags an async-iterator CancellationToken that lacks [EnumeratorCancellation]. When the
        // attribute is present, the token flows correctly and nothing is flagged.
        var test = @"
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async IAsyncEnumerable<int> RunAsync([EnumeratorCancellation] CancellationToken cancellationToken)
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
