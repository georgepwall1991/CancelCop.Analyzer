using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace CancelCop.Analyzer.Tests;

/// <summary>
/// CC001 covers async iterators, but its fix used to add a bare <c>CancellationToken</c>. On an
/// iterator that token is ignored by the compiler-generated <c>GetAsyncEnumerator</c>, so a
/// consumer's <c>.WithCancellation(token)</c> silently fails to reach it — which is exactly what
/// CC011 reports. The fix therefore traded one diagnostic for another and left the stream
/// uncancellable; it now adds <c>[EnumeratorCancellation]</c> as well.
/// </summary>
public class MissingCancellationTokenAsyncIteratorFixTests
{
    private static CSharpCodeFixTest<
        MissingCancellationTokenAnalyzer,
        MissingCancellationTokenCodeFixProvider,
        DefaultVerifier
    > CreateTest(string testCode, string fixedCode, params DiagnosticResult[] expected)
    {
        var test = new CSharpCodeFixTest<
            MissingCancellationTokenAnalyzer,
            MissingCancellationTokenCodeFixProvider,
            DefaultVerifier
        >
        {
            TestCode = testCode,
            FixedCode = fixedCode,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        test.ExpectedDiagnostics.AddRange(expected);
        return test;
    }

    [Fact]
    public async Task AsyncIteratorFix_AddsEnumeratorCancellationAndImport()
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

        var fixedCode =
            @"
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

public class Reader
{
    public async IAsyncEnumerable<int> ReadAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        yield return 1;
    }
}";

        var expected = new DiagnosticResult("CC001", DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("ReadAsync");
        await CreateTest(test, fixedCode, expected).RunAsync();
    }

    [Fact]
    public async Task FixedAsyncIterator_NoLongerTripsCC011()
    {
        // The point of the change: the output of CC001's fix must satisfy CC011 rather than trip it.
        // Running CC011 over the fixed source is what pins that the two rules now agree.
        var fixedSource =
            @"
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

public class Reader
{
    public async IAsyncEnumerable<int> ReadAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        yield return 1;
    }
}";

        var test = new CSharpAnalyzerTest<EnumeratorCancellationAnalyzer, DefaultVerifier>
        {
            TestCode = fixedSource,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        await test.RunAsync();
    }

    [Fact]
    public async Task NonIteratorAsyncMethod_StillGetsAPlainToken()
    {
        // The attribute is only meaningful on an iterator; adding it elsewhere would be an error
        // (CS8205), so an ordinary async method must be untouched by this change.
        var test =
            @"
using System.Threading.Tasks;

public class Reader
{
    public async Task {|#0:SaveAsync|}()
    {
        await Task.Yield();
    }
}";

        var fixedCode =
            @"
using System.Threading;
using System.Threading.Tasks;

public class Reader
{
    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        await Task.Yield();
    }
}";

        var expected = new DiagnosticResult("CC001", DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("SaveAsync");
        await CreateTest(test, fixedCode, expected).RunAsync();
    }

    [Fact]
    public async Task AsyncMethodReturningEnumerableWithoutYield_GetsAPlainToken()
    {
        // A method that returns an async enumerable without yielding is not an iterator, so the
        // attribute would not apply to it.
        var test =
            @"
using System.Collections.Generic;
using System.Threading.Tasks;

public class Reader
{
    private IAsyncEnumerable<int> _source = null!;

    public async Task<IAsyncEnumerable<int>> {|#0:LoadAsync|}()
    {
        await Task.Yield();
        return _source;
    }
}";

        var fixedCode =
            @"
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

public class Reader
{
    private IAsyncEnumerable<int> _source = null!;

    public async Task<IAsyncEnumerable<int>> LoadAsync(CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        return _source;
    }
}";

        var expected = new DiagnosticResult("CC001", DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("LoadAsync");
        await CreateTest(test, fixedCode, expected).RunAsync();
    }

    [Fact]
    public async Task YieldInsideANestedLocalFunction_GetsAPlainToken()
    {
        // The yield belongs to the local function's iterator, so the enclosing method is not one.
        var test =
            @"
using System.Collections.Generic;
using System.Threading.Tasks;

public class Reader
{
    public async Task<IAsyncEnumerable<int>> {|#0:LoadAsync|}()
    {
        await Task.Yield();
        return Inner();

        async IAsyncEnumerable<int> Inner()
        {
            await Task.Yield();
            yield return 1;
        }
    }
}";

        var fixedCode =
            @"
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

public class Reader
{
    public async Task<IAsyncEnumerable<int>> LoadAsync(CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        return Inner();

        async IAsyncEnumerable<int> Inner()
        {
            await Task.Yield();
            yield return 1;
        }
    }
}";

        var expected = new DiagnosticResult("CC001", DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("LoadAsync");
        await CreateTest(test, fixedCode, expected).RunAsync();
    }
}
