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
}
