using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace CancelCop.Analyzer.Tests;

public class AsyncEnumerableProducerHasTokenTests
{
    [Fact]
    public async Task AwaitForeachOverProducerThatTakesToken_ShouldNotReportDiagnostic()
    {
        // CC010 is quiet when the enumerated source already receives the token (here the producer call
        // passes it directly), so re-adding .WithCancellation would be redundant.
        var test = @"
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await foreach (var item in Produce(cancellationToken))
        {
        }
    }

    private static async IAsyncEnumerable<int> Produce([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        yield return 1;
        await Task.Yield();
    }
}";

        var t = new CSharpAnalyzerTest<AsyncEnumerableCancellationAnalyzer, DefaultVerifier>
        {
            TestCode = test,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        await t.RunAsync();
    }
}
