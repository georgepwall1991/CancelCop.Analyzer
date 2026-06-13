using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace CancelCop.Analyzer.Tests;

public class AsyncEnumerableCancellationCodeFixTests
{
    private static CSharpCodeFixTest<AsyncEnumerableCancellationAnalyzer, AsyncEnumerableCancellationCodeFixProvider, DefaultVerifier> CreateTest(
        string testCode, string fixedCode, params DiagnosticResult[] expected)
    {
        var test = new CSharpCodeFixTest<AsyncEnumerableCancellationAnalyzer, AsyncEnumerableCancellationCodeFixProvider, DefaultVerifier>
        {
            TestCode = testCode,
            FixedCode = fixedCode,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        test.ExpectedDiagnostics.AddRange(expected);
        return test;
    }

    [Fact]
    public async Task AwaitForeach_WrapsSourceInWithCancellation()
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

        var fixedCode = @"
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

        var expected = new DiagnosticResult("CC010", DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("cancellationToken");

        await CreateTest(test, fixedCode, expected).RunAsync();
    }

    [Fact]
    public async Task AwaitForeach_OverInvocation_WrapsInWithCancellation()
    {
        var test = @"
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    private IAsyncEnumerable<int> Produce() => null;

    public async Task ConsumeAsync(CancellationToken ct)
    {
        await foreach (var item in {|#0:Produce()|})
        {
        }
    }
}";

        var fixedCode = @"
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    private IAsyncEnumerable<int> Produce() => null;

    public async Task ConsumeAsync(CancellationToken ct)
    {
        await foreach (var item in Produce().WithCancellation(ct))
        {
        }
    }
}";

        var expected = new DiagnosticResult("CC010", DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("ct");

        await CreateTest(test, fixedCode, expected).RunAsync();
    }
}
