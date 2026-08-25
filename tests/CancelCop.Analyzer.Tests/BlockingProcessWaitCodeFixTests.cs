using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace CancelCop.Analyzer.Tests;

/// <summary>
/// The fixed code is compiled by the harness, so these pin that the CC030 rewrite is valid — with a
/// token, without one, and that it is withheld where <c>await</c> would not compile.
/// </summary>
public class BlockingProcessWaitCodeFixTests
{
    private static CSharpCodeFixTest<
        BlockingProcessWaitAnalyzer,
        BlockingProcessWaitCodeFixProvider,
        DefaultVerifier
    > CreateTest(string testCode, string fixedCode, params DiagnosticResult[] expected)
    {
        var test = new CSharpCodeFixTest<
            BlockingProcessWaitAnalyzer,
            BlockingProcessWaitCodeFixProvider,
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

    private static DiagnosticResult Expected(int location = 0) =>
        new DiagnosticResult("CC030", DiagnosticSeverity.Warning).WithLocation(location);

    [Fact]
    public async Task WaitForExit_WithTokenInScope_FlowsTheToken()
    {
        var test =
            @"
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(Process process, CancellationToken cancellationToken)
    {
        process.{|#0:WaitForExit|}();
        await Task.Yield();
    }
}";

        var fixedCode =
            @"
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(Process process, CancellationToken cancellationToken)
    {
        await process.WaitForExitAsync(cancellationToken);
        await Task.Yield();
    }
}";

        await CreateTest(test, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task WaitForExit_WithoutTokenInScope_StillCompiles()
    {
        var test =
            @"
using System.Diagnostics;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(Process process)
    {
        process.{|#0:WaitForExit|}();
        await Task.Yield();
    }
}";

        var fixedCode =
            @"
using System.Diagnostics;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(Process process)
    {
        await process.WaitForExitAsync();
        await Task.Yield();
    }
}";

        await CreateTest(test, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task WaitForExit_OnPropertyReceiver_KeepsTheReceiver()
    {
        var test =
            @"
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

public class Host
{
    public Process Child { get; } = new Process();
}

public class TestClass
{
    public async Task RunAsync(Host host, CancellationToken cancellationToken)
    {
        host.Child.{|#0:WaitForExit|}();
        await Task.Yield();
    }
}";

        var fixedCode =
            @"
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

public class Host
{
    public Process Child { get; } = new Process();
}

public class TestClass
{
    public async Task RunAsync(Host host, CancellationToken cancellationToken)
    {
        await host.Child.WaitForExitAsync(cancellationToken);
        await Task.Yield();
    }
}";

        await CreateTest(test, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task WaitForExit_NullConditional_HoistsToIfNotNullWaitForExitAsync()
    {
        // Preserving the null semantics needs control flow; as a whole statement the call
        // hoists to an `is not null` check awaiting the async form.
        var source =
            @"
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(Process? process, CancellationToken cancellationToken)
    {
        process?.{|#0:WaitForExit|}();
        await Task.Yield();
    }
}";

        var fixedCode =
            @"
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(Process? process, CancellationToken cancellationToken)
    {
        if (process is not null)
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        await Task.Yield();
    }
}";

        await CreateTest(source, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task WaitForExit_InsideLock_ReportsWithoutOfferingAFix()
    {
        // await is illegal in a lock body (CS1996). Blocking on a child process while holding a lock
        // is worth reporting; resolving it means restructuring the lock.
        var source =
            @"
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    private readonly object _gate = new object();

    public async Task RunAsync(Process process, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            process.{|#0:WaitForExit|}();
        }

        await Task.Yield();
    }
}";

        await CreateTest(source, source, Expected()).RunAsync();
    }

    [Fact]
    public async Task SubclassHidingWaitForExitAsync_FallsBackToTheParameterlessForm()
    {
        // The subclass hides the token-taking overload with a non-awaitable member, so passing the
        // in-scope token would bind to that and await an int (CS1061). C# hides methods by signature,
        // not by name, so the inherited parameterless form is still reachable — the rule falls back
        // to it rather than dropping a real finding. The wait stops being cancellable, which is the
        // subclass's doing, but the fix compiles.
        var test =
            @"
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

public class FakeProcess : Process
{
    public new int WaitForExitAsync(CancellationToken cancellationToken) => 0;
}

public class TestClass
{
    public async Task RunAsync(FakeProcess process, CancellationToken cancellationToken)
    {
        process.{|#0:WaitForExit|}();
        await Task.Yield();
    }
}";

        var fixedCode =
            @"
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

public class FakeProcess : Process
{
    public new int WaitForExitAsync(CancellationToken cancellationToken) => 0;
}

public class TestClass
{
    public async Task RunAsync(FakeProcess process, CancellationToken cancellationToken)
    {
        await process.WaitForExitAsync();
        await Task.Yield();
    }
}";

        await CreateTest(test, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task WaitForExit_WithTriviaInsideMemberAccess_KeepsTheComment()
    {
        // Rebuilding the member access from a trivia-stripped receiver would silently delete the
        // comment. Renaming the existing node preserves it.
        var test =
            @"
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(Process process, CancellationToken cancellationToken)
    {
        process /* started above */ .{|#0:WaitForExit|}();
        await Task.Yield();
    }
}";

        var fixedCode =
            @"
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(Process process, CancellationToken cancellationToken)
    {
        await process /* started above */ .WaitForExitAsync(cancellationToken);
        await Task.Yield();
    }
}";

        await CreateTest(test, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task ImplicitReceiver_InProcessSubclass_IsReportedAndFixed()
    {
        // An inherited call written without `this.` reaches the analyzer as a bare identifier. It is
        // the same blocking framework method the rule flags as `this.WaitForExit()`, so rejecting
        // that syntax form was a silent false negative.
        var test =
            @"
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

public class Worker : Process
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        {|#0:WaitForExit|}();
        await Task.Yield();
    }
}";

        var fixedCode =
            @"
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

public class Worker : Process
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await WaitForExitAsync(cancellationToken);
        await Task.Yield();
    }
}";

        await CreateTest(test, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task NullConditionalOnHidingSubclass_HoistsWithParameterlessFallback()
    {
        // Same fallback as the direct call — the inherited parameterless overload is reachable,
        // so the hoist uses it (the hidden token overload would not be awaitable).
        var source =
            @"
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

public class FakeProcess : Process
{
    public new int WaitForExitAsync(CancellationToken cancellationToken) => 0;
}

public class TestClass
{
    public async Task RunAsync(FakeProcess? process, CancellationToken cancellationToken)
    {
        process?.{|#0:WaitForExit|}();
        await Task.Yield();
    }
}";

        var fixedCode =
            @"
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

public class FakeProcess : Process
{
    public new int WaitForExitAsync(CancellationToken cancellationToken) => 0;
}

public class TestClass
{
    public async Task RunAsync(FakeProcess? process, CancellationToken cancellationToken)
    {
        if (process is not null)
        {
            await process.WaitForExitAsync();
        }
        await Task.Yield();
    }
}";

        await CreateTest(source, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task KeywordEscapedTokenName_IsReEscapedInTheFix()
    {
        // A symbol's Name drops the escape, so `@event` is stored as `event`. Emitting it bare would
        // reparse as a keyword and fail to compile even though the synthesized tree binds.
        var test =
            @"
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(Process process, CancellationToken @event)
    {
        process.{|#0:WaitForExit|}();
        await Task.Yield();
    }
}";

        var fixedCode =
            @"
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(Process process, CancellationToken @event)
    {
        await process.WaitForExitAsync(@event);
        await Task.Yield();
    }
}";

        await CreateTest(test, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task CommentInsideEmptyArgumentList_IsPreserved()
    {
        // Replacing the argument list wholesale would silently delete the comment.
        var test =
            @"
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(Process process, CancellationToken cancellationToken)
    {
        process.{|#0:WaitForExit|}(/* the tool must finish first */);
        await Task.Yield();
    }
}";

        var fixedCode =
            @"
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(Process process, CancellationToken cancellationToken)
    {
        await process.WaitForExitAsync(/* the tool must finish first */cancellationToken);
        await Task.Yield();
    }
}";

        await CreateTest(test, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task ContextualKeywordTokenName_IsReEscapedInTheFix()
    {
        // `await` is a contextual keyword, so a keyword-only check treats it as a plain identifier —
        // but it is reserved inside an async body, which is exactly where this rewrite lands. Emitted
        // bare it produces `await process.WaitForExitAsync(await)`, which does not parse.
        var test =
            @"
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(Process process, CancellationToken @await)
    {
        process.{|#0:WaitForExit|}();
        await Task.Yield();
    }
}";

        var fixedCode =
            @"
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(Process process, CancellationToken @await)
    {
        await process.WaitForExitAsync(@await);
        await Task.Yield();
    }
}";

        await CreateTest(test, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task AwaitWouldSpanARefStructLocal_ReportsWithoutOfferingAFix()
    {
        // Since C# 13 an async method may hold a Span<T> as long as it does not cross an await. This
        // compiles today because the existing await comes first — inserting one at WaitForExit would
        // put the span's lifetime across it (CS4007). The call binds fine, so only a lifetime check
        // catches this.
        var source =
            @"
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task<int> RunAsync(Process process, int[] data, CancellationToken cancellationToken)
    {
        await Task.Yield();
        Span<int> span = data.AsSpan();
        process.{|#0:WaitForExit|}();
        return span[0];
    }
}";

        await CreateTest(source, source, Expected()).RunAsync();
    }

    [Fact]
    public async Task RefStructLocalUsedOnlyBeforeTheCall_IsFixedNormally()
    {
        // The span is dead by the time the await would be inserted, so its lifetime never crosses it.
        // The guard must not withhold every fix that merely shares a method with a Span.
        var test =
            @"
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task<int> RunAsync(Process process, int[] data, CancellationToken cancellationToken)
    {
        Span<int> span = data.AsSpan();
        var first = span[0];
        process.{|#0:WaitForExit|}();
        return first;
    }
}";

        var fixedCode =
            @"
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task<int> RunAsync(Process process, int[] data, CancellationToken cancellationToken)
    {
        Span<int> span = data.AsSpan();
        var first = span[0];
        await process.WaitForExitAsync(cancellationToken);
        return first;
    }
}";

        await CreateTest(test, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task InsideForeachOverASpan_ReportsWithoutOfferingAFix()
    {
        // The span identifier only appears in the loop header, but its enumerator stays live for the
        // whole body, so an await inserted here would span it (CS4007). That lifetime is implicit —
        // a scan for later uses of the identifier never sees it.
        var source =
            @"
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(Process process, int[] data, CancellationToken cancellationToken)
    {
        await Task.Yield();
        foreach (var item in data.AsSpan())
        {
            process.{|#0:WaitForExit|}();
        }
    }
}";

        await CreateTest(source, source, Expected()).RunAsync();
    }

    [Fact]
    public async Task ShadowedTokenName_FallsBackToTheParameterlessForm()
    {
        // The outer token is in scope by symbol, but a nested lambda binds that identifier to a
        // string. Emitting the name would reference the wrong thing, so the token is unusable in
        // generated source — which makes the blocking call no less blocking. The rule falls back to
        // the parameterless form instead of dropping the finding.
        var test =
            @"
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(Process process, CancellationToken cancellationToken)
    {
        Func<string, Task> f = async (cancellationToken) =>
        {
            process.{|#0:WaitForExit|}();
            await Task.Yield();
        };

        await f(""x"");
    }
}";

        var fixedCode =
            @"
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(Process process, CancellationToken cancellationToken)
    {
        Func<string, Task> f = async (cancellationToken) =>
        {
            await process.WaitForExitAsync();
            await Task.Yield();
        };

        await f(""x"");
    }
}";

        await CreateTest(test, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task ChainedConditionalAccess_HoistsToIfNotNullWaitForExitAsync()
    {
        // `host?.Child.WaitForExit();` cannot take an in-place rewrite, but as a whole statement
        // it hoists to an `is not null` check with the operation spliced into the awaited call.
        var source =
            @"
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

public class Host
{
    public Process Child { get; } = new Process();
}

public class TestClass
{
    public async Task RunAsync(Host? host, CancellationToken cancellationToken)
    {
        host?.Child.{|#0:WaitForExit|}();
        await Task.Yield();
    }
}";

        var fixedCode =
            @"
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

public class Host
{
    public Process Child { get; } = new Process();
}

public class TestClass
{
    public async Task RunAsync(Host? host, CancellationToken cancellationToken)
    {
        if (host is not null)
        {
            await host.Child.WaitForExitAsync(cancellationToken);
        }
        await Task.Yield();
    }
}";

        await CreateTest(source, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task RefLikeUsingVarLocal_ReportsWithoutOfferingAFix()
    {
        // The local is never named again, but a `using var` is disposed at scope exit — that implicit
        // Dispose is a later use, so the lifetime still spans an inserted await (CS4007).
        var source =
            @"
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

public ref struct Lease
{
    public void Dispose() { }
}

public class TestClass
{
    public async Task RunAsync(Process process, CancellationToken cancellationToken)
    {
        await Task.Yield();
        using var lease = new Lease();
        process.{|#0:WaitForExit|}();
    }
}";

        await CreateTest(source, source, Expected()).RunAsync();
    }

    [Fact]
    public async Task RefStructEnumeratorOverAReferenceType_ReportsWithoutOfferingAFix()
    {
        // The collection is an ordinary class, but its GetEnumerator returns a ref struct that stays
        // live for the body — so the enumerator's type, not the collection's, is what matters.
        var source =
            @"
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

public ref struct RefEnumerator
{
    public int Current => 0;
    public bool MoveNext() => false;
}

public class RefCollection
{
    public RefEnumerator GetEnumerator() => new RefEnumerator();
}

public class TestClass
{
    public async Task RunAsync(Process process, RefCollection items, CancellationToken cancellationToken)
    {
        await Task.Yield();
        foreach (var item in items)
        {
            process.{|#0:WaitForExit|}();
        }
    }
}";

        await CreateTest(source, source, Expected()).RunAsync();
    }

    [Fact]
    public async Task RefLikeLocalUsedInALoopHeader_ReportsWithoutOfferingAFix()
    {
        // The condition and incrementor are written before the body but run again after it, so the
        // span crosses an await inserted in the body on the next iteration. Position is not
        // execution order inside a loop.
        var source =
            @"
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(Process process, int[] data, CancellationToken cancellationToken)
    {
        await Task.Yield();
        for (Span<int> span = data.AsSpan(); span.Length > 0; span = span.Slice(1))
        {
            process.{|#0:WaitForExit|}();
        }
    }
}";

        await CreateTest(source, source, Expected()).RunAsync();
    }

    [Fact]
    public async Task RefLikeUsingVarInAnEarlierClosedBlock_IsFixedNormally()
    {
        // The lease is disposed and out of scope before the call, so it cannot span the inserted
        // await. Scanning the whole function without checking scope would withhold a valid fix.
        var test =
            @"
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

public ref struct Lease
{
    public void Dispose() { }
}

public class TestClass
{
    public async Task RunAsync(Process process, CancellationToken cancellationToken)
    {
        await Task.Yield();
        {
            using var lease = new Lease();
        }

        process.{|#0:WaitForExit|}();
    }
}";

        var fixedCode =
            @"
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

public ref struct Lease
{
    public void Dispose() { }
}

public class TestClass
{
    public async Task RunAsync(Process process, CancellationToken cancellationToken)
    {
        await Task.Yield();
        {
            using var lease = new Lease();
        }

        await process.WaitForExitAsync(cancellationToken);
    }
}";

        await CreateTest(test, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task RefForeachIterationVariable_ReportsWithoutOfferingAFix()
    {
        // `foreach (ref int item in …)` binds a ref local, which cannot survive an await (CS9217).
        // It is not a variable declarator, and neither the collection nor its enumerator is ref-like,
        // so nothing else in the lifetime check sees it.
        var source =
            @"
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(Process process, int[] data, CancellationToken cancellationToken)
    {
        await Task.Yield();
        foreach (ref int item in data.AsSpan())
        {
            process.{|#0:WaitForExit|}();
            item = 0;
        }
    }
}";

        await CreateTest(source, source, Expected()).RunAsync();
    }

    [Fact]
    public async Task LambdaInsideAnUnrelatedConditionalAccess_IsFixedNormally()
    {
        // The `?.` belongs to the surrounding call, not to the blocking invocation. The lambda is its
        // own expression context, so the rewrite is safe and must still be offered.
        var test =
            @"
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

public class Host
{
    public void Register(Func<Task> callback) { }
}

public class TestClass
{
    public async Task RunAsync(Host? host, Process process, CancellationToken cancellationToken)
    {
        host?.Register(async () => process.{|#0:WaitForExit|}());
        await Task.Yield();
    }
}";

        var fixedCode =
            @"
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

public class Host
{
    public void Register(Func<Task> callback) { }
}

public class TestClass
{
    public async Task RunAsync(Host? host, Process process, CancellationToken cancellationToken)
    {
        host?.Register(async () => await process.WaitForExitAsync(cancellationToken));
        await Task.Yield();
    }
}";

        await CreateTest(test, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task CommentAttachedToTheMemberName_IsPreserved()
    {
        var test =
            @"
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(Process process, CancellationToken cancellationToken)
    {
        process.{|#0:WaitForExit|}/* why */();
        await Task.Yield();
    }
}";

        var fixedCode =
            @"
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(Process process, CancellationToken cancellationToken)
    {
        await process.WaitForExitAsync/* why */(cancellationToken);
        await Task.Yield();
    }
}";

        await CreateTest(test, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task RefLikeOutDeclaration_ReportsWithoutOfferingAFix()
    {
        // An `out Span<int> span` introduces a local through a declaration expression rather than a
        // variable declarator, so a declarator-only scan misses it — but its lifetime still crosses
        // an inserted await (CS4007).
        var source =
            @"
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    private static void Get(int[] data, out Span<int> span) => span = data.AsSpan();

    public async Task<int> RunAsync(Process process, int[] data, CancellationToken cancellationToken)
    {
        await Task.Yield();
        Get(data, out Span<int> span);
        process.{|#0:WaitForExit|}();
        return span[0];
    }
}";

        await CreateTest(source, source, Expected()).RunAsync();
    }

    [Fact]
    public async Task NullConditionalOnSubclassWithItsOwnParameterlessAsync_ShouldNotReportDiagnostic()
    {
        // With no token in scope the rewrite is `process.WaitForExitAsync()`, which binds to the
        // subclass's own unrelated parameterless member rather than the framework method — so the
        // diagnostic's premise is false. Reading the receiver's static type is what sees this; the
        // resolved method's ReceiverType is Process. (With a token in scope the arity-1 call still
        // reaches the base overload, since C# hides by signature, and the rule reports as usual.)
        var test =
            @"
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

public class FakeProcess : Process
{
    public new int WaitForExitAsync() => 0;
}

public class TestClass
{
    public async Task RunAsync(FakeProcess? process)
    {
        process?.WaitForExit();
        await Task.Yield();
    }
}";

        var t = new CSharpAnalyzerTest<BlockingProcessWaitAnalyzer, DefaultVerifier>
        {
            TestCode = test,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        await t.RunAsync();
    }

    [Fact]
    public async Task DeconstructionForeachWithRefStructEnumerator_ReportsWithoutOfferingAFix()
    {
        // `foreach (var (a, b) in …)` is ForEachVariableStatementSyntax, a sibling of the ordinary
        // form, so a check written against ForEachStatementSyntax alone misses the live enumerator.
        var source =
            @"
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

public ref struct PairEnumerator
{
    public (int, int) Current => (0, 0);
    public bool MoveNext() => false;
}

public class PairCollection
{
    public PairEnumerator GetEnumerator() => new PairEnumerator();
}

public class TestClass
{
    public async Task RunAsync(Process process, PairCollection items, CancellationToken cancellationToken)
    {
        await Task.Yield();
        foreach (var (a, b) in items)
        {
            process.{|#0:WaitForExit|}();
        }
    }
}";

        await CreateTest(source, source, Expected()).RunAsync();
    }
}
