using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace CancelCop.Analyzer.Tests;

public class PreferCancelAsyncCodeFixTests
{
    private static CSharpCodeFixTest<
        PreferCancelAsyncAnalyzer,
        PreferCancelAsyncCodeFixProvider,
        DefaultVerifier
    > CreateTest(string testCode, string fixedCode, params DiagnosticResult[] expected)
    {
        var test = new CSharpCodeFixTest<
            PreferCancelAsyncAnalyzer,
            PreferCancelAsyncCodeFixProvider,
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
    public async Task FixAll_TwoCancels_BothBecomeAwaitCancelAsync()
    {
        var test =
            @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task StopAsync(CancellationTokenSource cts)
    {
        cts.{|#0:Cancel|}();
        cts.{|#1:Cancel|}();
        await Task.Yield();
    }
}";

        var fixedCode =
            @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task StopAsync(CancellationTokenSource cts)
    {
        await cts.CancelAsync();
        await cts.CancelAsync();
        await Task.Yield();
    }
}";

        await CreateTest(
                test,
                fixedCode,
                new DiagnosticResult("CC022", DiagnosticSeverity.Info).WithLocation(0),
                new DiagnosticResult("CC022", DiagnosticSeverity.Info).WithLocation(1)
            )
            .RunAsync();
    }

    [Fact]
    public async Task Cancel_BecomesAwaitCancelAsync()
    {
        var test =
            @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task StopAsync(CancellationTokenSource cts)
    {
        cts.{|#0:Cancel|}();
        await Task.Yield();
    }
}";

        var fixedCode =
            @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task StopAsync(CancellationTokenSource cts)
    {
        await cts.CancelAsync();
        await Task.Yield();
    }
}";

        var expected = new DiagnosticResult("CC022", DiagnosticSeverity.Info).WithLocation(0);
        await CreateTest(test, fixedCode, expected).RunAsync();
    }

    [Fact]
    public async Task ChainedConditionalAccess_IsHoistedToIfNotNullAwait()
    {
        // `holder?.Cts.Cancel()` cannot take an in-place rewrite (`holder? await.Cts.CancelAsync()`
        // does not parse), but as a statement it can be hoisted to a null check with the
        // operation spliced back into the awaited chain.
        var test =
            @"
#nullable enable
using System.Threading;
using System.Threading.Tasks;

public class Holder
{
    public CancellationTokenSource Cts { get; } = new CancellationTokenSource();
}

public class TestClass
{
    public async Task StopAsync(Holder? holder)
    {
        holder?.Cts.{|#0:Cancel|}();
        await Task.Yield();
    }
}";

        var fixedCode =
            @"
#nullable enable
using System.Threading;
using System.Threading.Tasks;

public class Holder
{
    public CancellationTokenSource Cts { get; } = new CancellationTokenSource();
}

public class TestClass
{
    public async Task StopAsync(Holder? holder)
    {
        if (holder is not null)
        {
            await holder.Cts.CancelAsync();
        }
        await Task.Yield();
    }
}";

        var expected = new DiagnosticResult("CC022", DiagnosticSeverity.Info).WithLocation(0);
        await CreateTest(test, fixedCode, expected).RunAsync();
    }

    [Fact]
    public async Task DirectConditionalCancel_IsHoistedToIfNotNullAwait()
    {
        var test =
            @"
#nullable enable
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task StopAsync(CancellationTokenSource? cts)
    {
        cts?.{|#0:Cancel|}();
        await Task.Yield();
    }
}";

        var fixedCode =
            @"
#nullable enable
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task StopAsync(CancellationTokenSource? cts)
    {
        if (cts is not null)
        {
            await cts.CancelAsync();
        }
        await Task.Yield();
    }
}";

        var expected = new DiagnosticResult("CC022", DiagnosticSeverity.Info).WithLocation(0);
        await CreateTest(test, fixedCode, expected).RunAsync();
    }

    [Fact]
    public async Task ConditionalCancelWithComplexReceiver_ReportsWithoutOfferingAFix()
    {
        // Hoisting would evaluate the receiver twice; an invocation receiver may have side
        // effects, so the diagnostic stands without a rewrite.
        var test =
            @"
#nullable enable
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task StopAsync()
    {
        CreateCts()?.{|#0:Cancel|}();
        await Task.Yield();
    }

    private static CancellationTokenSource? CreateCts() => new();
}";

        var expected = new DiagnosticResult("CC022", DiagnosticSeverity.Info).WithLocation(0);
        await CreateTest(test, test, expected).RunAsync();
    }

    [Fact]
    public async Task NestedConditionalSpine_ReportsWithoutOfferingAFix()
    {
        // `a?.b?.Cancel()` — hoisting only the outer condition would leave `await a.b?.CancelAsync()`,
        // which throws NRE when b is null instead of silently skipping. Withheld.
        var test =
            @"
#nullable enable
using System.Threading;
using System.Threading.Tasks;

public class Holder
{
    public CancellationTokenSource? Cts { get; set; }
}

public class TestClass
{
    public async Task StopAsync(Holder? holder)
    {
        holder?.Cts?.{|#0:Cancel|}();
        await Task.Yield();
    }
}";

        var expected = new DiagnosticResult("CC022", DiagnosticSeverity.Info).WithLocation(0);
        await CreateTest(test, test, expected).RunAsync();
    }

    [Fact]
    public async Task FixAll_MixedDirectAndConditional_BothFixed()
    {
        var test =
            @"
#nullable enable
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task StopAsync(CancellationTokenSource? cts, CancellationTokenSource other)
    {
        cts?.{|#0:Cancel|}();
        other.{|#1:Cancel|}();
        await Task.Yield();
    }
}";

        var fixedCode =
            @"
#nullable enable
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task StopAsync(CancellationTokenSource? cts, CancellationTokenSource other)
    {
        if (cts is not null)
        {
            await cts.CancelAsync();
        }
        await other.CancelAsync();
        await Task.Yield();
    }
}";

        await CreateTest(
                test,
                fixedCode,
                new DiagnosticResult("CC022", DiagnosticSeverity.Info).WithLocation(0),
                new DiagnosticResult("CC022", DiagnosticSeverity.Info).WithLocation(1)
            )
            .RunAsync();
    }

    [Fact]
    public async Task CancelInsideAsyncLambdaArgumentOfUnrelatedConditionalAccess_IsFixed()
    {
        // Cancel() is void, so it cannot sit as a value argument. Nested inside an
        // async lambda that is an argument of an unrelated `?.` is still not on the
        // WhenNotNull spine, so await CancelAsync is legal.
        var test =
            @"
#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;

public class Holder
{
    public void Run(Func<Task> work) { }
}

public class TestClass
{
    public async Task StopAsync(Holder? holder, CancellationTokenSource cts)
    {
        holder?.Run(async () =>
        {
            cts.{|#0:Cancel|}();
            await Task.Yield();
        });
        await Task.Yield();
    }
}";

        var fixedCode =
            @"
#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;

public class Holder
{
    public void Run(Func<Task> work) { }
}

public class TestClass
{
    public async Task StopAsync(Holder? holder, CancellationTokenSource cts)
    {
        holder?.Run(async () =>
        {
            await cts.CancelAsync();
            await Task.Yield();
        });
        await Task.Yield();
    }
}";

        var expected = new DiagnosticResult("CC022", DiagnosticSeverity.Info).WithLocation(0);
        await CreateTest(test, fixedCode, expected).RunAsync();
    }
}
