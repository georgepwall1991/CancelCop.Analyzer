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
    public async Task WaitForExit_NullConditional_ReportsWithoutOfferingAFix()
    {
        // Preserving the null semantics needs control flow, not an expression rewrite.
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

        await CreateTest(source, source, Expected()).RunAsync();
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
    public async Task SubclassHidingWaitForExitAsync_ShouldNotReportDiagnostic()
    {
        // Finding WaitForExitAsync on Process proves the API exists, not that the rewritten call
        // reaches it. This subclass hides it with a non-awaitable member, so `await` would fail with
        // CS1061 — there is no async alternative here and the rule stays quiet.
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
        process.WaitForExit();
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
    public async Task NullConditionalOnHidingSubclass_ShouldNotReportDiagnostic()
    {
        // A null-conditional call gets no fix, but it still claims an async alternative exists. Here
        // the subclass hides WaitForExitAsync with a non-awaitable member, so that claim is false and
        // the rule must stay quiet — exactly as it does for the equivalent direct call.
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
    public async Task RunAsync(FakeProcess? process, CancellationToken cancellationToken)
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
}
