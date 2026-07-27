using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace CancelCop.Analyzer.Tests;

/// <summary>
/// CC034: an externally reachable <c>IAsyncEnumerable&lt;T&gt;</c> iterator with no
/// <c>CancellationToken</c> parameter. CC001 only sees <c>Task</c>/<c>ValueTask</c> returns and
/// CC011 only fires once a token exists, so this shape slips past both.
/// </summary>
public class AsyncStreamMissingTokenTests
{
    private static CSharpAnalyzerTest<AsyncStreamMissingTokenAnalyzer, DefaultVerifier> Test(
        string source
    ) => new() { TestCode = source, ReferenceAssemblies = ReferenceAssemblies.Net.Net90 };

    private static DiagnosticResult Expected(string method) =>
        new DiagnosticResult("CC034", DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments(method);

    [Fact]
    public async Task PublicIteratorWithoutToken_ShouldReportDiagnostic()
    {
        var test =
            @"
using System.Collections.Generic;
using System.Threading.Tasks;

public class Reader
{
    public async IAsyncEnumerable<int> {|#0:ReadAsync|}()
    {
        await Task.Yield();
        yield return 1;
    }
}";

        var t = Test(test);
        t.ExpectedDiagnostics.Add(Expected("ReadAsync"));
        await t.RunAsync();
    }

    [Fact]
    public async Task ProtectedIteratorWithoutToken_ShouldReportDiagnostic()
    {
        var test =
            @"
using System.Collections.Generic;
using System.Threading.Tasks;

public class Reader
{
    protected async IAsyncEnumerable<int> {|#0:ReadAsync|}()
    {
        await Task.Yield();
        yield return 1;
    }
}";

        var t = Test(test);
        t.ExpectedDiagnostics.Add(Expected("ReadAsync"));
        await t.RunAsync();
    }

    [Fact]
    public async Task IteratorWithToken_ShouldNotReportDiagnostic()
    {
        var test =
            @"
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

public class Reader
{
    public async IAsyncEnumerable<int> ReadAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        yield return 1;
    }
}";

        await Test(test).RunAsync();
    }

    [Fact]
    public async Task PrivateIterator_ShouldNotReportDiagnostic()
    {
        // Same reach test as CC001: a private stream's callers are all in view, so the omission is
        // a local decision rather than an API defect.
        var test =
            @"
using System.Collections.Generic;
using System.Threading.Tasks;

public class Reader
{
    private async IAsyncEnumerable<int> ReadAsync()
    {
        await Task.Yield();
        yield return 1;
    }
}";

        await Test(test).RunAsync();
    }

    [Fact]
    public async Task NonIteratorReturningAsyncEnumerable_ShouldNotReportDiagnostic()
    {
        // A pass-through does not produce the items, so its own signature is not what stops the
        // enumeration — the underlying producer's is.
        var test =
            @"
using System.Collections.Generic;

public class Reader
{
    private IAsyncEnumerable<int> _source = null!;

    public IAsyncEnumerable<int> ReadAsync() => _source;
}";

        await Test(test).RunAsync();
    }

    [Fact]
    public async Task OverrideIterator_ShouldNotReportDiagnostic()
    {
        // Adding a parameter to a signature the base type fixed would break the override.
        var test =
            @"
using System.Collections.Generic;
using System.Threading.Tasks;

public abstract class ReaderBase
{
    public abstract IAsyncEnumerable<int> ReadAsync();
}

public class Reader : ReaderBase
{
    public override async IAsyncEnumerable<int> ReadAsync()
    {
        await Task.Yield();
        yield return 1;
    }
}";

        await Test(test).RunAsync();
    }

    [Fact]
    public async Task InterfaceImplementation_ShouldNotReportDiagnostic()
    {
        var test =
            @"
using System.Collections.Generic;
using System.Threading.Tasks;

public interface IReader
{
    IAsyncEnumerable<int> ReadAsync();
}

public class Reader : IReader
{
    public async IAsyncEnumerable<int> ReadAsync()
    {
        await Task.Yield();
        yield return 1;
    }
}";

        await Test(test).RunAsync();
    }

    [Fact]
    public async Task SyncIterator_ShouldNotReportDiagnostic()
    {
        var test =
            @"
using System.Collections.Generic;

public class Reader
{
    public IEnumerable<int> Read()
    {
        yield return 1;
    }
}";

        await Test(test).RunAsync();
    }

    [Fact]
    public async Task YieldOnlyInsideALocalFunction_ShouldNotReportDiagnostic()
    {
        // The yield belongs to the local function's iterator, not to the enclosing method — which is
        // a pass-through and so not the producer whose signature matters.
        var test =
            @"
using System.Collections.Generic;

public class Reader
{
    public IAsyncEnumerable<int> ReadAsync()
    {
        return Inner();

        async IAsyncEnumerable<int> Inner()
        {
            await System.Threading.Tasks.Task.Yield();
            yield return 1;
        }
    }
}";

        await Test(test).RunAsync();
    }

    [Fact]
    public async Task LookalikeAsyncEnumerable_ShouldNotReportDiagnostic()
    {
        // Same name, different namespace. CC034 is symbol-gated.
        var test =
            @"
namespace Custom
{
    public interface IAsyncEnumerable<T> { }

    public class Reader
    {
        public IAsyncEnumerable<int> ReadAsync() => null!;
    }
}";

        await Test(test).RunAsync();
    }
}
