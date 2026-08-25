using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixVerifier<
    CancelCop.Analyzer.BlockingSemaphoreAnalyzer,
    CancelCop.Analyzer.BlockingSemaphoreCodeFixProvider,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier
>;

namespace CancelCop.Analyzer.Tests;

public class BlockingSemaphoreCodeFixTests
{
    [Fact]
    public async Task Wait_OnFieldSemaphore_CarriesReceiverAndAddsToken()
    {
        var test =
            @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    private readonly SemaphoreSlim _gate = new SemaphoreSlim(1);

    public async Task RunAsync(CancellationToken ct)
    {
        _gate.{|#0:Wait|}();
        await Task.Yield();
    }
}";

        var fixedCode =
            @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    private readonly SemaphoreSlim _gate = new SemaphoreSlim(1);

    public async Task RunAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        await Task.Yield();
    }
}";

        var expected = VerifyCS.Diagnostic("CC026").WithLocation(0);
        await VerifyCS.VerifyCodeFixAsync(test, expected, fixedCode);
    }

    [Fact]
    public async Task FixAll_TwoWaits_BothBecomeAwaitWaitAsync()
    {
        var test =
            @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(SemaphoreSlim gate, CancellationToken ct)
    {
        gate.{|#0:Wait|}();
        gate.{|#1:Wait|}();
        await Task.Yield();
    }
}";

        var fixedCode =
            @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(SemaphoreSlim gate, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        await gate.WaitAsync(ct);
        await Task.Yield();
    }
}";

        await VerifyCS.VerifyCodeFixAsync(
            test,
            new[]
            {
                VerifyCS.Diagnostic("CC026").WithLocation(0),
                VerifyCS.Diagnostic("CC026").WithLocation(1),
            },
            fixedCode
        );
    }

    [Fact]
    public async Task Wait_WithToken_BecomesAwaitWaitAsyncWithToken()
    {
        var test =
            @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(SemaphoreSlim gate, CancellationToken ct)
    {
        gate.{|#0:Wait|}();
        await Task.Yield();
    }
}";

        var fixedCode =
            @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(SemaphoreSlim gate, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        await Task.Yield();
    }
}";

        var expected = VerifyCS.Diagnostic("CC026").WithLocation(0);
        await VerifyCS.VerifyCodeFixAsync(test, expected, fixedCode);
    }

    [Fact]
    public async Task WaitWithTokenArg_CarriesArgumentThrough()
    {
        var test =
            @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(SemaphoreSlim gate, CancellationToken ct)
    {
        gate.{|#0:Wait|}(ct);
        await Task.Yield();
    }
}";

        var fixedCode =
            @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(SemaphoreSlim gate, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        await Task.Yield();
    }
}";

        var expected = VerifyCS.Diagnostic("CC026").WithLocation(0);
        await VerifyCS.VerifyCodeFixAsync(test, expected, fixedCode);
    }

    [Fact]
    public async Task Wait_WithoutToken_BecomesAwaitWaitAsync()
    {
        var test =
            @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(SemaphoreSlim gate)
    {
        gate.{|#0:Wait|}();
        await Task.Yield();
    }
}";

        var fixedCode =
            @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(SemaphoreSlim gate)
    {
        await gate.WaitAsync();
        await Task.Yield();
    }
}";

        var expected = VerifyCS.Diagnostic("CC026").WithLocation(0);
        await VerifyCS.VerifyCodeFixAsync(test, expected, fixedCode);
    }

    [Fact]
    public async Task ResultUsedAsReceiver_ParenthesizesAwait()
    {
        var test =
            @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task<string> RunAsync(SemaphoreSlim gate, CancellationToken ct)
    {
        return gate.{|#0:Wait|}(1000, ct).ToString();
    }
}";

        var fixedCode =
            @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task<string> RunAsync(SemaphoreSlim gate, CancellationToken ct)
    {
        return (await gate.WaitAsync(1000, ct)).ToString();
    }
}";

        var expected = VerifyCS.Diagnostic("CC026").WithLocation(0);
        await VerifyCS.VerifyCodeFixAsync(test, expected, fixedCode);
    }

    [Fact]
    public async Task ResultUsedAsReceiver_ThroughNullForgiving_ParenthesizesAwait()
    {
        var test =
            @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task<string> RunAsync(SemaphoreSlim gate, CancellationToken ct)
    {
        return gate.{|#0:Wait|}(1000, ct)!.ToString();
    }
}";

        var fixedCode =
            @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task<string> RunAsync(SemaphoreSlim gate, CancellationToken ct)
    {
        return (await gate.WaitAsync(1000, ct))!.ToString();
    }
}";

        var expected = VerifyCS.Diagnostic("CC026").WithLocation(0);
        await VerifyCS.VerifyCodeFixAsync(test, expected, fixedCode);
    }

    [Fact]
    public async Task ChainedConditionalAccess_WithTimeout_HoistsToIfNotNullWaitAsync()
    {
        // `holder?.Gate.Wait(1000)` cannot take an in-place rewrite, but as a whole statement
        // it hoists to an `is not null` check carrying the timeout through to WaitAsync.
        var source =
            @"
using System.Threading;
using System.Threading.Tasks;

public class Holder
{
    public SemaphoreSlim Gate { get; } = new SemaphoreSlim(1);
}

public class TestClass
{
    public async Task RunAsync(Holder? holder)
    {
        holder?.Gate.{|#0:Wait|}(1000);
        await Task.Yield();
    }
}";

        var fixedCode =
            @"
using System.Threading;
using System.Threading.Tasks;

public class Holder
{
    public SemaphoreSlim Gate { get; } = new SemaphoreSlim(1);
}

public class TestClass
{
    public async Task RunAsync(Holder? holder)
    {
        if (holder is not null)
        {
            await holder.Gate.WaitAsync(1000);
        }
        await Task.Yield();
    }
}";

        var expected = VerifyCS.Diagnostic("CC026").WithLocation(0);
        await VerifyCS.VerifyCodeFixAsync(source, expected, fixedCode);
    }

    [Fact]
    public async Task ChainedConditionalAccess_ResultUsedAsReceiver_ReportsWithoutOfferingAFix()
    {
        // The WhenNotNull is `.Wait(1000).ToString()`, not the Wait call
        // itself. Replacing only Wait yields `holder?(await .Gate.WaitAsync(1000)).ToString()`.
        var source =
            @"
using System.Threading;
using System.Threading.Tasks;

public class Holder
{
    public SemaphoreSlim Gate { get; } = new SemaphoreSlim(1);
}

public class TestClass
{
    public async Task<string> RunAsync(Holder? holder)
    {
        return holder?.Gate.{|#0:Wait|}(1000).ToString() ?? string.Empty;
    }
}";

        var expected = VerifyCS.Diagnostic("CC026").WithLocation(0);
        await VerifyCS.VerifyCodeFixAsync(source, expected, source);
    }

    [Fact]
    public async Task ChainedConditionalAccess_ComparedToBool_ReportsWithoutOfferingAFix()
    {
        var source =
            @"
using System.Threading;
using System.Threading.Tasks;

public class Holder
{
    public SemaphoreSlim Gate { get; } = new SemaphoreSlim(1);
}

public class TestClass
{
    public async Task<bool> RunAsync(Holder? holder)
    {
        return holder?.Gate.{|#0:Wait|}(1000) == true;
    }
}";

        var expected = VerifyCS.Diagnostic("CC026").WithLocation(0);
        await VerifyCS.VerifyCodeFixAsync(source, expected, source);
    }

    [Fact]
    public async Task ChainedConditionalAccess_AsObject_ReportsWithoutOfferingAFix()
    {
        var source =
            @"
using System.Threading;
using System.Threading.Tasks;

public class Holder
{
    public SemaphoreSlim Gate { get; } = new SemaphoreSlim(1);
}

public class TestClass
{
    public async Task<object?> RunAsync(Holder? holder)
    {
        return holder?.Gate.{|#0:Wait|}(1000) as object;
    }
}";

        var expected = VerifyCS.Diagnostic("CC026").WithLocation(0);
        await VerifyCS.VerifyCodeFixAsync(source, expected, source);
    }

    [Fact]
    public async Task WaitAsArgumentInsideUnrelatedConditionalAccess_IsFixed()
    {
        // The Wait call is an argument, not the WhenNotNull branch, so await
        // is legal: holder?.Consume(await gate.WaitAsync(...)).
        var test =
            @"
using System.Threading;
using System.Threading.Tasks;

public class Holder
{
    public void Consume(bool acquired) { }
}

public class TestClass
{
    public async Task RunAsync(Holder? holder, SemaphoreSlim gate, CancellationToken ct)
    {
        holder?.Consume(gate.{|#0:Wait|}(1000, ct));
        await Task.Yield();
    }
}";

        var fixedCode =
            @"
using System.Threading;
using System.Threading.Tasks;

public class Holder
{
    public void Consume(bool acquired) { }
}

public class TestClass
{
    public async Task RunAsync(Holder? holder, SemaphoreSlim gate, CancellationToken ct)
    {
        holder?.Consume(await gate.WaitAsync(1000, ct));
        await Task.Yield();
    }
}";

        var expected = VerifyCS.Diagnostic("CC026").WithLocation(0);
        await VerifyCS.VerifyCodeFixAsync(test, expected, fixedCode);
    }

    [Fact]
    public async Task DirectConditionalWait_HoistsToIfNotNullWaitAsyncWithToken()
    {
        var test =
            @"
using System;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(SemaphoreSlim? gate, CancellationToken cancellationToken)
    {
        await Task.Yield();
        gate?.{|#0:Wait|}();
        await Task.Yield();
    }
}";

        var fixedCode =
            @"
using System;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(SemaphoreSlim? gate, CancellationToken cancellationToken)
    {
        await Task.Yield();
        if (gate is not null)
        {
            await gate.WaitAsync(cancellationToken);
        }
        await Task.Yield();
    }
}";

        var expected = VerifyCS.Diagnostic("CC026").WithLocation(0);
        await VerifyCS.VerifyCodeFixAsync(test, expected, fixedCode);
    }

    [Fact]
    public async Task ConditionalWaitWithNestedConditionalArgument_HoistsCarryingArguments()
    {
        // A nested `?.` inside the ARGUMENT list is carried through verbatim — its semantics
        // are untouched; only the outer spine is rewritten.
        var test =
            @"
using System.Threading;
using System.Threading.Tasks;

public class SemaphoreHolder
{
    public string Name { get; } = """";
}

public class TestClass
{
    public async Task RunAsync(SemaphoreSlim? gate, SemaphoreHolder? other)
    {
        await Task.Yield();
        gate?.{|#0:Wait|}(other?.Name.Length ?? 0);
        await Task.Yield();
    }
}";

        var fixedCode =
            @"
using System.Threading;
using System.Threading.Tasks;

public class SemaphoreHolder
{
    public string Name { get; } = """";
}

public class TestClass
{
    public async Task RunAsync(SemaphoreSlim? gate, SemaphoreHolder? other)
    {
        await Task.Yield();
        if (gate is not null)
        {
            await gate.WaitAsync(other?.Name.Length ?? 0);
        }
        await Task.Yield();
    }
}";

        var expected = VerifyCS.Diagnostic("CC026").WithLocation(0);
        await VerifyCS.VerifyCodeFixAsync(test, expected, fixedCode);
    }
}
