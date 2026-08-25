using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    CancelCop.Analyzer.BlockingThreadJoinAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace CancelCop.Analyzer.Tests;

/// <remarks>
/// <c>Thread</c> declares only <c>Join()</c>, <c>Join(int)</c>, and
/// <c>Join(TimeSpan)</c> on current .NET (verified against the net9/net10
/// ref packs) — none virtual, none with a TAP counterpart — so every
/// diagnostic below is reported without a rewrite. <c>Thread</c> is also
/// sealed on current .NET, so no derived-type fixture can exist.
/// </remarks>
public class BlockingThreadJoinAnalyzerTests
{
    [Fact]
    public async Task ThreadJoin_InAsyncMethod_ShouldReportDiagnostic()
    {
        var test = @"
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

        var expected = VerifyCS.Diagnostic("CC053")
            .WithLocation(0)
            .WithArguments("Join");
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task NullConditionalJoin_InAsyncMethod_ShouldReportDiagnostic()
    {
        // A `?.` spine surfaces as a member-binding name; the call still
        // blocks and is still reported.
        var test = @"
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
        var expected = VerifyCS.Diagnostic("CC053")
            .WithLocation(0)
            .WithArguments("Join");
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task TimeoutArityOverloads_InAsyncMethod_ShouldReportDiagnostic()
    {
        // Both timeout arities exist on the framework type and block the same
        // way; neither has a JoinAsync arity to rewrite to.
        var test = @"
using System;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(Thread thread, CancellationToken cancellationToken)
    {
        thread.{|#0:Join|}(100);
        thread.{|#1:Join|}(TimeSpan.FromSeconds(1));
        await Task.Yield();
    }
}";

        var expected0 = VerifyCS.Diagnostic("CC053")
            .WithLocation(0)
            .WithArguments("Join");
        var expected1 = VerifyCS.Diagnostic("CC053")
            .WithLocation(1)
            .WithArguments("Join");
        await VerifyCS.VerifyAnalyzerAsync(test, expected0, expected1);
    }

    [Fact]
    public async Task ThreadJoin_InSyncMethod_ShouldNotReportDiagnostic()
    {
        var test = @"
using System.Threading;

public class TestClass
{
    public void Run(Thread thread)
    {
        thread.Join();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task LookalikeClass_WithOwnMembers_ShouldNotReportDiagnostic()
    {
        var test = @"
using System.Threading.Tasks;

public class FakeThread
{
    public void Join() { }
}

public static class TestClass
{
    public static async Task RunAsync(FakeThread thread)
    {
        thread.Join();
        await Task.Yield();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task OtherMemberName_ShouldNotReportDiagnostic()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(Thread thread)
    {
        thread.Start();
        await Task.Yield();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ThreadJoin_InsideLock_ShouldReportAwaitUnsafe()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    private readonly object gate = new();

    public async Task RunAsync(Thread thread, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            thread.{|#0:Join|}();
        }
        await Task.Yield();
    }
}";

        var expected = VerifyCS.Diagnostic("CC053")
            .WithLocation(0)
            .WithArguments("Join");
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task FreshConstructionReceiver_ShouldReportDiagnostic()
    {
        // `new Thread(...)` is the only provably-fresh receiver shape; the
        // call still blocks and is reported (no JoinAsync to rewrite to).
        var test = @"
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

        var expected = VerifyCS.Diagnostic("CC053")
            .WithLocation(0)
            .WithArguments("Join");
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task CurrentThreadReceiver_ShouldReportDiagnostic()
    {
        // A receiver that is a static property access is not provably fresh,
        // but the blocking call itself is still reported.
        var test = @"
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

        var expected = VerifyCS.Diagnostic("CC053")
            .WithLocation(0)
            .WithArguments("Join");
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task ThreadJoin_ProvablyZeroTimeout_ShouldStayQuiet()
    {
        var test = @"
using System;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(Thread worker)
    {
        worker.Join(0);
        worker.Join(TimeSpan.Zero);
        await Task.Yield();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ThreadJoin_NonZeroTimeout_ShouldReportDiagnostic()
    {
        var test = @"
using System;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(Thread worker)
    {
        worker.{|#0:Join|}(500);
        await Task.Yield();
    }
}";

        var expected = VerifyCS.Diagnostic("CC053")
            .WithLocation(0)
            .WithArguments("Join");
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }
}
