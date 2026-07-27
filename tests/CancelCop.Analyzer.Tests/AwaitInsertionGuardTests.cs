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

    [Fact]
    public async Task CC013_TopLevelStatementsAcrossALiveSpan_ReportsWithoutOfferingAFix()
    {
        // A top-level program has no declared method body, so a body-only search finds nothing and
        // the guard silently passed. Its global statements are the synthesized entry point and
        // locals declared there have the same lifetimes.
        var source =
            @"
using System;
using System.Threading;
using System.Threading.Tasks;

await Task.Yield();
int[] data = new int[1];
Span<int> span = data.AsSpan();
{|#0:Thread.Sleep(100)|};
Console.WriteLine(span[0]);
";

        var test = new CSharpCodeFixTest<
            BlockingSleepAnalyzer,
            BlockingSleepCodeFixProvider,
            DefaultVerifier
        >
        {
            TestCode = source,
            FixedCode = source,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
            TestState = { OutputKind = Microsoft.CodeAnalysis.OutputKind.ConsoleApplication },
        };
        test.ExpectedDiagnostics.Add(
            new DiagnosticResult("CC013", DiagnosticSeverity.Warning).WithLocation(0)
        );
        await test.RunAsync();
    }

    [Fact]
    public async Task CC025_SpanReadBeforeScopeExit_IsStillFixed()
    {
        // `await using` awaits at disposal — scope exit — not at the `using` keyword. The span is
        // read before then, so its lifetime never crosses the await and the fix is valid. Checking
        // at the keyword would withhold it.
        var test =
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
    public async Task<int> RunAsync(int[] data)
    {
        await Task.Yield();
        Span<int> span = data.AsSpan();
        {|#0:using|} var resource = new Resource();
        return span[0];
    }
}";

        var fixedCode =
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
    public async Task<int> RunAsync(int[] data)
    {
        await Task.Yield();
        Span<int> span = data.AsSpan();
        await using var resource = new Resource();
        return span[0];
    }
}";

        var t = new CSharpCodeFixTest<
            AwaitUsingAnalyzer,
            AwaitUsingCodeFixProvider,
            DefaultVerifier
        >
        {
            TestCode = test,
            FixedCode = fixedCode,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        t.ExpectedDiagnostics.Add(
            new DiagnosticResult("CC025", DiagnosticSeverity.Info).WithLocation(0)
        );
        await t.RunAsync();
    }

    [Fact]
    public async Task CC025_EarlierRefStructUsing_ReportsWithoutOfferingAFix()
    {
        // Resources dispose in reverse order, so the ref-struct lease declared first is still live
        // when the async disposal awaits — CS4007. The await lands at scope exit, exactly on the
        // scope's end offset, which an exclusive containment check would treat as outside the scope
        // and skip.
        var source =
            @"
using System;
using System.Threading;
using System.Threading.Tasks;

public ref struct Lease
{
    public void Dispose() { }
}

public class Resource : IDisposable, IAsyncDisposable
{
    public void Dispose() { }
    public ValueTask DisposeAsync() => default;
}

public class TestClass
{
    public async Task RunAsync()
    {
        await Task.Yield();
        using var lease = new Lease();
        {|#0:using|} var resource = new Resource();
    }
}";

        await NoFix<AwaitUsingAnalyzer, AwaitUsingCodeFixProvider>(
            source,
            new DiagnosticResult("CC025", DiagnosticSeverity.Info).WithLocation(0)
        ).RunAsync();
    }

    [Fact]
    public async Task CC015_RefLikeTemporaryInTheSameCall_ReportsWithoutOfferingAFix()
    {
        // The stackalloc'd Span is a temporary with no name, so scanning declared locals never sees
        // it — but it is on the stack when the inserted await runs (CS4007).
        var source =
            @"
using System;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    private static void Consume(Span<int> buffer, int value) { }

    public async Task RunAsync(Task<int> work)
    {
        await Task.Yield();
        Consume(stackalloc int[1], work.{|#0:Result|});
    }
}";

        await NoFix<BlockingOnAsyncAnalyzer, BlockingOnAsyncCodeFixProvider>(
            source,
            new DiagnosticResult("CC015", DiagnosticSeverity.Warning)
                .WithLocation(0)
                .WithArguments(".Result")
        ).RunAsync();
    }

    [Fact]
    public async Task CC025_LaterRefStructUsing_IsStillFixed()
    {
        // Declarations dispose in reverse order, so the lease declared after the resource is already
        // disposed by the time the resource's disposal awaits. Treating every ref struct in the
        // scope as live would withhold a valid fix.
        var test =
            @"
using System;
using System.Threading;
using System.Threading.Tasks;

public ref struct Lease
{
    public void Dispose() { }
}

public class Resource : IDisposable, IAsyncDisposable
{
    public void Dispose() { }
    public ValueTask DisposeAsync() => default;
}

public class TestClass
{
    public async Task RunAsync()
    {
        await Task.Yield();
        {|#0:using|} var resource = new Resource();
        using var lease = new Lease();
    }
}";

        var fixedCode =
            @"
using System;
using System.Threading;
using System.Threading.Tasks;

public ref struct Lease
{
    public void Dispose() { }
}

public class Resource : IDisposable, IAsyncDisposable
{
    public void Dispose() { }
    public ValueTask DisposeAsync() => default;
}

public class TestClass
{
    public async Task RunAsync()
    {
        await Task.Yield();
        await using var resource = new Resource();
        using var lease = new Lease();
    }
}";

        var t = new CSharpCodeFixTest<
            AwaitUsingAnalyzer,
            AwaitUsingCodeFixProvider,
            DefaultVerifier
        >
        {
            TestCode = test,
            FixedCode = fixedCode,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        t.ExpectedDiagnostics.Add(
            new DiagnosticResult("CC025", DiagnosticSeverity.Info).WithLocation(0)
        );
        await t.RunAsync();
    }

    [Fact]
    public async Task CC015_NamedSpanArgumentBeforeTheCall_ReportsWithoutOfferingAFix()
    {
        // The span is a named local, but what matters is that its value is already on the stack as
        // an earlier argument when the await would run — the local scan only looks at uses *after*
        // the insertion point and never sees this.
        var source =
            @"
using System;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    private static void Consume(Span<int> buffer, int value) { }

    public async Task RunAsync(Task<int> work, int[] data)
    {
        await Task.Yield();
        Consume(data.AsSpan(), work.{|#0:Result|});
    }
}";

        await NoFix<BlockingOnAsyncAnalyzer, BlockingOnAsyncCodeFixProvider>(
            source,
            new DiagnosticResult("CC015", DiagnosticSeverity.Warning)
                .WithLocation(0)
                .WithArguments(".Result")
        ).RunAsync();
    }

    [Fact]
    public async Task CC015_ExpressionBodiedMember_ReportsWithoutOfferingAFix()
    {
        // An expression-bodied member has no enclosing statement, so a statement-anchored search
        // abandoned the analysis entirely and let the fix through.
        var source =
            @"
using System;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    private static int Consume(Span<int> buffer, int value) => value;

    public async Task<int> RunAsync(Task<int> work) =>
        Consume(stackalloc int[1], work.{|#0:Result|});
}";

        await NoFix<BlockingOnAsyncAnalyzer, BlockingOnAsyncCodeFixProvider>(
            source,
            new DiagnosticResult("CC015", DiagnosticSeverity.Warning)
                .WithLocation(0)
                .WithArguments(".Result")
        ).RunAsync();
    }

    [Fact]
    public async Task CC015_ConsumedRefLikeSubexpression_IsStillFixed()
    {
        // The stackalloc is consumed by Read, which returns an int — nothing ref-like is pending
        // when the await runs. Rejecting every ref-like descendant would withhold a valid fix.
        var test =
            @"
using System;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    private static int Read(Span<int> buffer) => 0;
    private static void Consume(int a, int b) { }

    public async Task RunAsync(Task<int> work)
    {
        await Task.Yield();
        Consume(Read(stackalloc int[1]), work.{|#0:Result|});
    }
}";

        var fixedCode =
            @"
using System;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    private static int Read(Span<int> buffer) => 0;
    private static void Consume(int a, int b) { }

    public async Task RunAsync(Task<int> work)
    {
        await Task.Yield();
        Consume(Read(stackalloc int[1]), (await work));
    }
}";

        var t = new CSharpCodeFixTest<
            BlockingOnAsyncAnalyzer,
            BlockingOnAsyncCodeFixProvider,
            DefaultVerifier
        >
        {
            TestCode = test,
            FixedCode = fixedCode,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        t.ExpectedDiagnostics.Add(
            new DiagnosticResult("CC015", DiagnosticSeverity.Warning)
                .WithLocation(0)
                .WithArguments(".Result")
        );
        await t.RunAsync();
    }

    [Fact]
    public async Task CC015_AssignmentThroughARefLikeTarget_ReportsWithoutOfferingAFix()
    {
        // `span[0]` yields an int, but the storage location it refers to keeps the span pending when
        // the await runs (CS4007). Reading only the operand's own type misses that.
        var source =
            @"
using System;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(Task<int> work, int[] data)
    {
        await Task.Yield();
        Span<int> span = data.AsSpan();
        span[0] = work.{|#0:Result|};
    }
}";

        await NoFix<BlockingOnAsyncAnalyzer, BlockingOnAsyncCodeFixProvider>(
            source,
            new DiagnosticResult("CC015", DiagnosticSeverity.Warning)
                .WithLocation(0)
                .WithArguments(".Result")
        ).RunAsync();
    }

    [Fact]
    public async Task CC013_RefLikeLocalConsumedByTheArgument_IsStillFixed()
    {
        // `await Task.Delay(span[0])` evaluates its argument before suspending, so the span is
        // already consumed. Treating the call's start as the await position would withhold this.
        var test =
            @"
using System;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(int[] data, CancellationToken cancellationToken)
    {
        await Task.Yield();
        Span<int> span = data.AsSpan();
        {|#0:Thread.Sleep(span[0])|};
    }
}";

        var fixedCode =
            @"
using System;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(int[] data, CancellationToken cancellationToken)
    {
        await Task.Yield();
        Span<int> span = data.AsSpan();
        await Task.Delay(span[0], cancellationToken);
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

    [Fact]
    public async Task CC015_RefReturningArgumentBeforeTheCall_ReportsWithoutOfferingAFix()
    {
        // A managed reference from a ref-returning member cannot cross an await either (CS8178),
        // and Roslyn reports the referent's value type, so a type-only check never sees it.
        var source =
            @"
using System;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    private int _field;

    private ref int GetRef() => ref _field;

    private static void Consume(ref int slot, int value) { }

    public async Task RunAsync(Task<int> work)
    {
        await Task.Yield();
        Consume(ref GetRef(), work.{|#0:Result|});
    }
}";

        await NoFix<BlockingOnAsyncAnalyzer, BlockingOnAsyncCodeFixProvider>(
            source,
            new DiagnosticResult("CC015", DiagnosticSeverity.Warning)
                .WithLocation(0)
                .WithArguments(".Result")
        ).RunAsync();
    }

    [Fact]
    public async Task CC015_ValueReadThroughARefLikeReceiver_IsStillFixed()
    {
        // `span[0]` as an ordinary by-value argument copies the element and is done with the span,
        // so the fix is valid. Only storage-preserving positions keep the receiver pending.
        var test =
            @"
using System;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    private static void Consume(int a, int b) { }

    public async Task RunAsync(Task<int> work, int[] data)
    {
        await Task.Yield();
        Span<int> span = data.AsSpan();
        Consume(span[0], work.{|#0:Result|});
    }
}";

        var fixedCode =
            @"
using System;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    private static void Consume(int a, int b) { }

    public async Task RunAsync(Task<int> work, int[] data)
    {
        await Task.Yield();
        Span<int> span = data.AsSpan();
        Consume(span[0], (await work));
    }
}";

        var t = new CSharpCodeFixTest<
            BlockingOnAsyncAnalyzer,
            BlockingOnAsyncCodeFixProvider,
            DefaultVerifier
        >
        {
            TestCode = test,
            FixedCode = fixedCode,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        t.ExpectedDiagnostics.Add(
            new DiagnosticResult("CC015", DiagnosticSeverity.Warning)
                .WithLocation(0)
                .WithArguments(".Result")
        );
        await t.RunAsync();
    }

    [Fact]
    public async Task CC015_RefLikeInvocationReceiver_ReportsWithoutOfferingAFix()
    {
        // The span is the receiver of the call whose argument is being awaited, so it stays pending
        // across the await (CS4007). It reaches the operand scan only as a method-group expression
        // with no type.
        var source =
            @"
using System;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(Task<int> work, int[] data)
    {
        await Task.Yield();
        Span<int> span = data.AsSpan();
        var slice = span.Slice(work.{|#0:Result|});
    }
}";

        await NoFix<BlockingOnAsyncAnalyzer, BlockingOnAsyncCodeFixProvider>(
            source,
            new DiagnosticResult("CC015", DiagnosticSeverity.Warning)
                .WithLocation(0)
                .WithArguments(".Result")
        ).RunAsync();
    }

    [Fact]
    public async Task CC013_BackwardGoto_ReportsWithoutOfferingAFix()
    {
        // A backward goto is a loop the syntax does not show: control returns to Use(span) after the
        // call, so the span crosses an await inserted here even though no reference follows it
        // lexically and there is no loop statement to find.
        var source =
            @"
using System;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    private static void Use(Span<int> buffer) { }

    public async Task RunAsync(int[] data, CancellationToken cancellationToken)
    {
        await Task.Yield();
        Span<int> span = data.AsSpan();
    retry:
        Use(span);
        {|#0:Thread.Sleep(100)|};
        if (data.Length > 0)
            goto retry;
    }
}";

        await NoFix<BlockingSleepAnalyzer, BlockingSleepCodeFixProvider>(
            source,
            new DiagnosticResult("CC013", DiagnosticSeverity.Warning).WithLocation(0)
        ).RunAsync();
    }
}
