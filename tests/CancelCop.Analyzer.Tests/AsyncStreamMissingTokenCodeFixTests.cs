using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace CancelCop.Analyzer.Tests;

/// <summary>
/// The fix adds the token <i>and</i> <c>[EnumeratorCancellation]</c>. A bare token parameter on an
/// iterator is silently ignored by <c>.WithCancellation(token)</c> — CC011's whole point — so
/// adding only the parameter would trade this diagnostic for that one and leave the stream just as
/// uncancellable. The harness compiles the fixed code, so a missing import fails here.
/// </summary>
public class AsyncStreamMissingTokenCodeFixTests
{
    private static CSharpCodeFixTest<
        AsyncStreamMissingTokenAnalyzer,
        AsyncStreamMissingTokenCodeFixProvider,
        DefaultVerifier
    > CreateTest(string testCode, string fixedCode)
    {
        var test = new CSharpCodeFixTest<
            AsyncStreamMissingTokenAnalyzer,
            AsyncStreamMissingTokenCodeFixProvider,
            DefaultVerifier
        >
        {
            TestCode = testCode,
            FixedCode = fixedCode,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        test.ExpectedDiagnostics.Add(
            new DiagnosticResult("CC034", DiagnosticSeverity.Warning)
                .WithLocation(0)
                .WithArguments("ReadAsync")
        );
        return test;
    }

    [Fact]
    public async Task AddsAnnotatedTokenParameterAndImports()
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

        await CreateTest(test, fixedCode).RunAsync();
    }

    [Fact]
    public async Task KeepsExistingParametersAndPlacesTokenLast()
    {
        var test =
            @"
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

public class Reader
{
    public async IAsyncEnumerable<int> {|#0:ReadAsync|}(string path, int take)
    {
        await Task.Yield();
        yield return take;
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
    public async IAsyncEnumerable<int> ReadAsync(string path, int take, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        yield return take;
    }
}";

        await CreateTest(test, fixedCode).RunAsync();
    }

    [Fact]
    public async Task InsertsBeforeATrailingParamsParameter()
    {
        // A `params` parameter must remain last (CS0231).
        var test =
            @"
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

public class Reader
{
    public async IAsyncEnumerable<int> {|#0:ReadAsync|}(string path, params int[] items)
    {
        await Task.Yield();
        yield return items.Length;
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
    public async IAsyncEnumerable<int> ReadAsync(string path, [EnumeratorCancellation] CancellationToken cancellationToken = default, params int[] items)
    {
        await Task.Yield();
        yield return items.Length;
    }
}";

        await CreateTest(test, fixedCode).RunAsync();
    }

    [Fact]
    public async Task AvoidsCollidingWithAnExistingName()
    {
        // CS0100 / CS0136 guard: the chosen name must not clash with a parameter or a body local,
        // so the shared helper falls back to 'ct'.
        var test =
            @"
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

public class Reader
{
    public async IAsyncEnumerable<int> {|#0:ReadAsync|}(string cancellationToken)
    {
        await Task.Yield();
        yield return cancellationToken.Length;
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
    public async IAsyncEnumerable<int> ReadAsync(string cancellationToken, [EnumeratorCancellation] CancellationToken ct = default)
    {
        await Task.Yield();
        yield return cancellationToken.Length;
    }
}";

        await CreateTest(test, fixedCode).RunAsync();
    }
}
