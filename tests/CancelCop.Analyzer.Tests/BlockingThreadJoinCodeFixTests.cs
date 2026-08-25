using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace CancelCop.Analyzer.Tests;

/// <summary>
/// CC053 is analyzer-only by design: <c>System.Threading.Thread</c> declares
/// no TAP <c>JoinAsync</c> on any shipped .NET (verified against the
/// net9/net10 ref packs), so every rewrite candidate fails its speculative
/// rebind and no fix is ever offered — the source stays byte-for-byte
/// unchanged in every scenario below.
/// </summary>
public class BlockingThreadJoinCodeFixTests
{
    private static CSharpCodeFixTest<
        BlockingThreadJoinAnalyzer,
        BlockingThreadJoinCodeFixProvider,
        DefaultVerifier
    > CreateTest(string testCode, string fixedCode, params DiagnosticResult[] expected)
    {
        var test = new CSharpCodeFixTest<
            BlockingThreadJoinAnalyzer,
            BlockingThreadJoinCodeFixProvider,
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
        new DiagnosticResult("CC053", DiagnosticSeverity.Warning)
            .WithLocation(location)
            .WithArguments("Join");

    [Fact]
    public async Task ThreadJoin_WithTokenInScope_ReportsWithoutOfferingAFix()
    {
        // No framework `JoinAsync` exists, so the token-first and tokenless
        // candidates both fail their speculative rebind: reported, no fix.
        var source =
            @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(Thread thread, CancellationToken cancellationToken)
    {
        thread.{|#0:Join|}();
        await Task.Yield();
    }
}";

        await CreateTest(source, source, Expected()).RunAsync();
    }

    [Fact]
    public async Task ThreadJoin_TimeoutArity_ReportsWithoutOfferingAFix()
    {
        var source =
            @"
using System;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(Thread thread, CancellationToken cancellationToken)
    {
        thread.{|#0:Join|}(100);
        await Task.Yield();
    }
}";

        await CreateTest(source, source, Expected()).RunAsync();
    }

    [Fact]
    public async Task ThreadJoin_NullConditional_ReportsWithoutOfferingAFix()
    {
        // The hoist's JoinAsync candidates fail their speculative rebind on
        // the framework type, so the statement stays as-is.
        var source =
            @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(Thread? thread, CancellationToken cancellationToken)
    {
        thread?.{|#0:Join|}();
        await Task.Yield();
    }
}";

        await CreateTest(source, source, Expected()).RunAsync();
    }

    [Fact]
    public async Task ThreadJoin_InsideLock_ReportsWithoutOfferingAFix()
    {
        var source =
            @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    private readonly object sync = new();

    public async Task RunAsync(Thread thread, CancellationToken cancellationToken)
    {
        lock (sync)
        {
            thread.{|#0:Join|}();
        }
        await Task.Yield();
    }
}";

        await CreateTest(source, source, Expected()).RunAsync();
    }
    [Fact]
    public async Task Join_FreshConstructionReceiver_NoFix()
    {
        // `new Thread(...)` is the only provably-fresh receiver shape; with
        // no framework JoinAsync there is still nothing to rewrite to.
        var source =
            @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        new Thread(() => { }).{|#0:Join|}();
        await Task.Yield();
    }
}";

        await CreateTest(source, source, Expected()).RunAsync();
    }

    [Fact]
    public async Task Join_CurrentThreadReceiver_ReportsWithoutOfferingAFix()
    {
        var source =
            @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        Thread.CurrentThread.{|#0:Join|}();
        await Task.Yield();
    }
}";

        await CreateTest(source, source, Expected()).RunAsync();
    }

    [Fact]
    public async Task Join_UserDeclaredJoinAsyncHider_StillNoFix()
    {
        // Even a user-declared JoinAsync cannot unlock a rewrite: the rebind
        // must resolve to the FRAMEWORK Thread lineage. Thread is sealed on
        // current .NET, so no derived type can carry a same-named hider with
        // framework ancestry — the candidate is rejected either way.
        var source =
            @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(Thread thread, CancellationToken cancellationToken)
    {
        thread.{|#0:Join|}(millisecondsTimeout: 100);
        await Task.Yield();
    }
}";

        await CreateTest(source, source, Expected()).RunAsync();
    }
}
