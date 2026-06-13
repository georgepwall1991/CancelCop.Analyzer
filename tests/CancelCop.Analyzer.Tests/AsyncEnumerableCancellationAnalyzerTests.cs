using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace CancelCop.Analyzer.Tests;

public class AsyncEnumerableCancellationAnalyzerTests
{
    private static CSharpAnalyzerTest<AsyncEnumerableCancellationAnalyzer, DefaultVerifier> CreateTest(
        string testCode, params DiagnosticResult[] expected)
    {
        var test = new CSharpAnalyzerTest<AsyncEnumerableCancellationAnalyzer, DefaultVerifier>
        {
            TestCode = testCode,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        test.ExpectedDiagnostics.AddRange(expected);
        return test;
    }

    [Fact]
    public async Task AwaitForeach_OverAsyncEnumerable_WithoutToken_ShouldReportDiagnostic()
    {
        var test = @"
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task ConsumeAsync(IAsyncEnumerable<int> source, CancellationToken cancellationToken)
    {
        await foreach (var item in {|#0:source|})
        {
        }
    }
}";

        var expected = new DiagnosticResult("CC010", DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("cancellationToken");

        await CreateTest(test, expected).RunAsync();
    }

    [Fact]
    public async Task AwaitForeach_WithWithCancellation_ShouldNotReportDiagnostic()
    {
        var test = @"
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task ConsumeAsync(IAsyncEnumerable<int> source, CancellationToken cancellationToken)
    {
        await foreach (var item in source.WithCancellation(cancellationToken))
        {
        }
    }
}";

        await CreateTest(test).RunAsync();
    }

    [Fact]
    public async Task AwaitForeach_NoTokenInScope_ShouldNotReportDiagnostic()
    {
        var test = @"
using System.Collections.Generic;
using System.Threading.Tasks;

public class TestClass
{
    public async Task ConsumeAsync(IAsyncEnumerable<int> source)
    {
        await foreach (var item in source)
        {
        }
    }
}";

        await CreateTest(test).RunAsync();
    }

    [Fact]
    public async Task SynchronousForeach_ShouldNotReportDiagnostic()
    {
        var test = @"
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task ConsumeAsync(List<int> source, CancellationToken cancellationToken)
    {
        foreach (var item in source)
        {
        }
        await Task.CompletedTask;
    }
}";

        await CreateTest(test).RunAsync();
    }

    [Fact]
    public async Task AwaitForeach_OverProducerCallPassingToken_ShouldNotReportDiagnostic()
    {
        var test = @"
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;

public class TestClass
{
    private async IAsyncEnumerable<int> ProduceAsync([EnumeratorCancellation] CancellationToken token = default)
    {
        yield return 1;
        await Task.CompletedTask;
    }

    public async Task ConsumeAsync(CancellationToken cancellationToken)
    {
        await foreach (var item in ProduceAsync(cancellationToken))
        {
        }
    }
}";

        await CreateTest(test).RunAsync();
    }

    [Fact]
    public async Task AwaitForeach_TokenFromLambda_ShouldReportDiagnostic()
    {
        var test = @"
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public void Register(IAsyncEnumerable<int> source)
    {
        Func<CancellationToken, Task> handler = async cancellationToken =>
        {
            await foreach (var item in {|#0:source|})
            {
            }
        };
    }
}";

        var expected = new DiagnosticResult("CC010", DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("cancellationToken");

        await CreateTest(test, expected).RunAsync();
    }

    [Fact]
    public async Task AwaitForeach_ConfigureAwaitWithoutWithCancellation_ShouldReportDiagnostic()
    {
        var test = @"
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task ConsumeAsync(IAsyncEnumerable<int> source, CancellationToken cancellationToken)
    {
        await foreach (var item in {|#0:source|}.ConfigureAwait(false))
        {
        }
    }
}";

        var expected = new DiagnosticResult("CC010", DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("cancellationToken");

        await CreateTest(test, expected).RunAsync();
    }

    [Fact]
    public async Task AwaitForeach_ConfiguredCancelable_ShouldNotReportDiagnostic()
    {
        var test = @"
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task ConsumeAsync(IAsyncEnumerable<int> source, CancellationToken cancellationToken)
    {
        await foreach (var item in source.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
        }
    }
}";

        await CreateTest(test).RunAsync();
    }
}
