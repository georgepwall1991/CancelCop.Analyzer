using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace CancelCop.Analyzer.Tests;

public class AsyncEnumerableCleanTests
{
    [Fact]
    public async Task AwaitForeachWithCancellation_ShouldNotReportDiagnostic()
    {
        // CC010 flags an `await foreach` that does not flow a token. When the source already passes one
        // via .WithCancellation(token), nothing is flagged.
        var test = @"
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(IAsyncEnumerable<int> source, CancellationToken cancellationToken)
    {
        await foreach (var item in source.WithCancellation(cancellationToken))
        {
        }
    }
}";

        var t = new CSharpAnalyzerTest<AsyncEnumerableCancellationAnalyzer, DefaultVerifier>
        {
            TestCode = test,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        await t.RunAsync();
    }

    [Fact]
    public async Task AwaitForeachWithCancellationThenConfigureAwait_ShouldNotReportDiagnostic()
    {
        // The token is already flowed via .WithCancellation(token) before .ConfigureAwait(false), so the
        // configured-awaitable chain must stay quiet (complements the ConfigureAwait-without-token
        // positive).
        var test = @"
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(IAsyncEnumerable<int> source, CancellationToken cancellationToken)
    {
        await foreach (var item in source.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
        }
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
