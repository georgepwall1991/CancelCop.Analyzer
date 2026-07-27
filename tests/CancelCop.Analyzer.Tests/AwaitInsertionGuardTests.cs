using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace CancelCop.Analyzer.Tests;

/// <summary>
/// Every rule whose fix inserts an <c>await</c> shares one contract: the rewrite must compile, and
/// where it cannot the diagnostic is still reported but no fix is offered. CC030 was built that way;
/// CC013, CC015, CC022, CC025, CC026, and CC028 predate the guard and are swept here.
/// </summary>
/// <remarks>
/// Each case asserts <c>FixedCode == TestCode</c>, so a fix that reappears fails the test. The two
/// representative unsafe positions are a <c>lock</c> body (CS1996) and a live <c>Span&lt;T&gt;</c>
/// (CS4007) — one syntactic, one semantic, matching the two halves of the shared guard.
/// </remarks>
public class AwaitInsertionGuardTests
{
    private static CSharpCodeFixTest<TAnalyzer, TCodeFix, DefaultVerifier> NoFix<
        TAnalyzer,
        TCodeFix
    >(string source, DiagnosticResult expected)
        where TAnalyzer : DiagnosticAnalyzer, new()
        where TCodeFix : CodeFixProvider, new()
    {
        var test = new CSharpCodeFixTest<TAnalyzer, TCodeFix, DefaultVerifier>
        {
            TestCode = source,
            FixedCode = source,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        test.ExpectedDiagnostics.Add(expected);
        return test;
    }

    [Fact]
    public async Task CC013_ThreadSleepInsideLock_ReportsWithoutOfferingAFix()
    {
        var source =
            @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    private readonly object _gate = new object();

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            {|#0:Thread.Sleep(100)|};
        }

        await Task.Yield();
    }
}";

        await NoFix<BlockingSleepAnalyzer, BlockingSleepCodeFixProvider>(
                source,
                new DiagnosticResult("CC013", DiagnosticSeverity.Warning).WithLocation(0)
            )
            .RunAsync();
    }

    [Fact]
    public async Task CC015_ResultInsideLock_ReportsWithoutOfferingAFix()
    {
        var source =
            @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    private readonly object _gate = new object();

    public async Task RunAsync(Task<int> work, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var value = work.{|#0:Result|};
        }

        await Task.Yield();
    }
}";

        await NoFix<BlockingOnAsyncAnalyzer, BlockingOnAsyncCodeFixProvider>(
                source,
                new DiagnosticResult("CC015", DiagnosticSeverity.Warning)
                    .WithLocation(0)
                    .WithArguments(".Result")
            )
            .RunAsync();
    }

    [Fact]
    public async Task CC026_SemaphoreWaitInsideLock_ReportsWithoutOfferingAFix()
    {
        var source =
            @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    private readonly object _gate = new object();

    public async Task RunAsync(SemaphoreSlim gate, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            gate.{|#0:Wait|}();
        }

        await Task.Yield();
    }
}";

        await NoFix<BlockingSemaphoreAnalyzer, BlockingSemaphoreCodeFixProvider>(
                source,
                new DiagnosticResult("CC026", DiagnosticSeverity.Warning).WithLocation(0)
            )
            .RunAsync();
    }

    [Fact]
    public async Task CC022_CancelInsideLock_ReportsWithoutOfferingAFix()
    {
        var source =
            @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    private readonly object _gate = new object();

    public async Task RunAsync(CancellationTokenSource cts)
    {
        lock (_gate)
        {
            cts.{|#0:Cancel|}();
        }

        await Task.Yield();
    }
}";

        await NoFix<PreferCancelAsyncAnalyzer, PreferCancelAsyncCodeFixProvider>(
                source,
                new DiagnosticResult("CC022", DiagnosticSeverity.Info).WithLocation(0)
            )
            .RunAsync();
    }

    [Fact]
    public async Task CC028_BlockingReadAcrossALiveSpan_ReportsWithoutOfferingAFix()
    {
        // CC028 already refused to fix inside a lock; the ref-like half of the guard is what this
        // adds. The span is live across the call, so an inserted await would be CS4007.
        var source =
            @"
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task<int> RunAsync(string path, int[] data, CancellationToken cancellationToken)
    {
        await Task.Yield();
        Span<int> span = data.AsSpan();
        var text = File.{|#0:ReadAllText|}(path);
        return span[0] + text.Length;
    }
}";

        await NoFix<BlockingFileIoAnalyzer, BlockingFileIoCodeFixProvider>(
                source,
                new DiagnosticResult("CC028", DiagnosticSeverity.Warning)
                    .WithLocation(0)
                    .WithArguments("ReadAllText")
            )
            .RunAsync();
    }

    [Fact]
    public async Task CC013_ThreadSleepAcrossALiveSpan_ReportsWithoutOfferingAFix()
    {
        var source =
            @"
using System;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task<int> RunAsync(int[] data, CancellationToken cancellationToken)
    {
        await Task.Yield();
        Span<int> span = data.AsSpan();
        {|#0:Thread.Sleep(100)|};
        return span[0];
    }
}";

        await NoFix<BlockingSleepAnalyzer, BlockingSleepCodeFixProvider>(
                source,
                new DiagnosticResult("CC013", DiagnosticSeverity.Warning).WithLocation(0)
            )
            .RunAsync();
    }

    [Fact]
    public async Task CC025_UsingInsideLock_ReportsWithoutOfferingAFix()
    {
        // CC025 turns `using` into `await using`, which is also an await insertion — and `await
        // using` is just as illegal in a lock body as any other await. The resource implements both
        // interfaces, which is exactly the case where plain `using` compiles but the async form is
        // preferable.
        var source =
            @"
using System;
using System.Threading;
using System.Threading.Tasks;

public class Resource : IDisposable, IAsyncDisposable
{
    public void Dispose() { }
    public ValueTask DisposeAsync() => default;
}

public class TestClass
{
    private readonly object _gate = new object();

    public async Task RunAsync()
    {
        lock (_gate)
        {
            {|#0:using|} var resource = new Resource();
        }

        await Task.Yield();
    }
}";

        await NoFix<AwaitUsingAnalyzer, AwaitUsingCodeFixProvider>(
                source,
                new DiagnosticResult("CC025", DiagnosticSeverity.Info).WithLocation(0)
            )
            .RunAsync();
    }

    [Fact]
    public async Task CC013_ThreadSleepInOrdinaryAsyncCode_IsStillFixed()
    {
        // The guard must only withhold fixes in the positions that need it — the ordinary case has
        // to keep working, or the sweep would quietly disable half the package.
        var test =
            @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        {|#0:Thread.Sleep(100)|};
        await Task.Yield();
    }
}";

        var fixedCode =
            @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(100, cancellationToken);
        await Task.Yield();
    }
}";

        var t = new CSharpCodeFixTest<
            BlockingSleepAnalyzer,
            BlockingSleepCodeFixProvider,
            DefaultVerifier
        >
        {
            TestCode = test,
            FixedCode = fixedCode,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        t.ExpectedDiagnostics.Add(
            new DiagnosticResult("CC013", DiagnosticSeverity.Warning).WithLocation(0)
        );
        await t.RunAsync();
    }
}
