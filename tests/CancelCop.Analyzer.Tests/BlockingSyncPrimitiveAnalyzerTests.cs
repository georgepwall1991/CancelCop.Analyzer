using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace CancelCop.Analyzer.Tests;

/// <summary>
/// CC031: blocking synchronization primitives inside async code. Unlike the rest of the
/// blocking-in-async family these have no <c>…Async</c> counterpart at all, so the rule is
/// analyzer-only — resolving it is a design change, not a rewrite.
/// </summary>
public class BlockingSyncPrimitiveAnalyzerTests
{
    private static CSharpAnalyzerTest<BlockingSyncPrimitiveAnalyzer, DefaultVerifier> Test(
        string source
    ) => new() { TestCode = source, ReferenceAssemblies = ReferenceAssemblies.Net.Net90 };

    private static DiagnosticResult Expected(string member) =>
        new DiagnosticResult("CC031", DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments(member);

    [Fact]
    public async Task ManualResetEventSlimWait_InAsyncMethod_ShouldReportDiagnostic()
    {
        var test =
            @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(ManualResetEventSlim gate)
    {
        gate.{|#0:Wait|}();
        await Task.Yield();
    }
}";

        var t = Test(test);
        t.ExpectedDiagnostics.Add(Expected("ManualResetEventSlim.Wait"));
        await t.RunAsync();
    }

    [Fact]
    public async Task CountdownEventWait_InAsyncMethod_ShouldReportDiagnostic()
    {
        var test =
            @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(CountdownEvent latch)
    {
        latch.{|#0:Wait|}();
        await Task.Yield();
    }
}";

        var t = Test(test);
        t.ExpectedDiagnostics.Add(Expected("CountdownEvent.Wait"));
        await t.RunAsync();
    }

    [Fact]
    public async Task WaitHandleWaitOne_InAsyncMethod_ShouldReportDiagnostic()
    {
        // WaitOne is declared on WaitHandle; ManualResetEvent inherits it, so the rule has to match
        // by inheritance rather than by the exact receiver type.
        var test =
            @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(ManualResetEvent gate)
    {
        gate.{|#0:WaitOne|}();
        await Task.Yield();
    }
}";

        var t = Test(test);
        t.ExpectedDiagnostics.Add(Expected("WaitHandle.WaitOne"));
        await t.RunAsync();
    }

    [Fact]
    public async Task MonitorWait_InAsyncMethod_ShouldReportDiagnostic()
    {
        var test =
            @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    private readonly object _gate = new object();

    public async Task RunAsync()
    {
        lock (_gate)
        {
            Monitor.{|#0:Wait|}(_gate);
        }

        await Task.Yield();
    }
}";

        var t = Test(test);
        t.ExpectedDiagnostics.Add(Expected("Monitor.Wait"));
        await t.RunAsync();
    }

    [Fact]
    public async Task ThreadJoin_InAsyncMethod_ShouldReportDiagnostic()
    {
        var test =
            @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(Thread worker)
    {
        worker.{|#0:Join|}();
        await Task.Yield();
    }
}";

        var t = Test(test);
        t.ExpectedDiagnostics.Add(Expected("Thread.Join"));
        await t.RunAsync();
    }

    [Fact]
    public async Task TokenWaitHandleWaitOne_InAsyncMethod_ShouldReportDiagnostic()
    {
        // The classic "wait for cancellation" anti-pattern: blocking a thread-pool thread on the
        // token's wait handle rather than awaiting a task.
        var test =
            @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        cancellationToken.WaitHandle.{|#0:WaitOne|}();
        await Task.Yield();
    }
}";

        var t = Test(test);
        t.ExpectedDiagnostics.Add(Expected("WaitHandle.WaitOne"));
        await t.RunAsync();
    }

    [Fact]
    public async Task ZeroTimeoutWait_ShouldNotReportDiagnostic()
    {
        // A zero timeout is an immediate probe, not a wait — the same exclusion CC013, CC015, and
        // CC026 make for their provably-zero forms.
        var test =
            @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(ManualResetEventSlim gate)
    {
        var entered = gate.Wait(0);
        await Task.Yield();
    }
}";

        await Test(test).RunAsync();
    }

    [Fact]
    public async Task BlockingWait_InSyncMethod_ShouldNotReportDiagnostic()
    {
        var test =
            @"
using System.Threading;

public class TestClass
{
    public void Run(ManualResetEventSlim gate)
    {
        gate.Wait();
    }
}";

        await Test(test).RunAsync();
    }

    [Fact]
    public async Task BlockingWait_InSyncLambdaInsideAsyncMethod_ShouldNotReportDiagnostic()
    {
        var test =
            @"
using System;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(ManualResetEventSlim gate)
    {
        Action a = () => gate.Wait();
        a();
        await Task.Yield();
    }
}";

        await Test(test).RunAsync();
    }

    [Fact]
    public async Task LookalikeWait_ShouldNotReportDiagnostic()
    {
        // Same method name, unrelated type. CC031 is symbol-gated.
        var test =
            @"
using System.Threading.Tasks;

public class Gate
{
    public void Wait() { }
    public bool WaitOne() => true;
}

public class TestClass
{
    public async Task RunAsync(Gate gate)
    {
        gate.Wait();
        gate.WaitOne();
        await Task.Yield();
    }
}";

        await Test(test).RunAsync();
    }

    [Fact]
    public async Task SemaphoreSlimWait_ShouldNotReportDiagnostic()
    {
        // SemaphoreSlim.Wait is CC026's rule — it has a WaitAsync counterpart and a real fix, so
        // reporting it here too would double up on the same call.
        var test =
            @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(SemaphoreSlim gate)
    {
        gate.Wait();
        await Task.Yield();
    }
}";

        await Test(test).RunAsync();
    }

    [Fact]
    public async Task TimeSpanZeroWait_ShouldNotReportDiagnostic()
    {
        // TimeSpan.Zero is not a compiler constant, so a constant-value check alone would flag this
        // non-blocking probe. All three framework zero spellings are recognised.
        var test =
            @"
using System;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(ManualResetEventSlim gate, ManualResetEvent handle, Thread worker)
    {
        var a = gate.Wait(TimeSpan.Zero);
        var b = handle.WaitOne(default(TimeSpan));
        var c = worker.Join(new TimeSpan());
        await Task.Yield();
    }
}";

        await Test(test).RunAsync();
    }

    [Fact]
    public async Task NonZeroTimeSpanWait_ShouldReportDiagnostic()
    {
        // The exclusion is for *provably* zero waits only — a real timeout still blocks.
        var test =
            @"
using System;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(ManualResetEventSlim gate)
    {
        var entered = gate.{|#0:Wait|}(TimeSpan.FromSeconds(5));
        await Task.Yield();
    }
}";

        var t = Test(test);
        t.ExpectedDiagnostics.Add(Expected("ManualResetEventSlim.Wait"));
        await t.RunAsync();
    }

    [Fact]
    public async Task ZeroTimeoutMonitorWait_StillReportsDiagnostic()
    {
        // A zero timeout ends the condition wait, but Monitor.Wait cannot return until it reacquires
        // the monitor — which can block behind another thread. The probe exclusion that applies to
        // the other primitives would be a false negative here.
        var test =
            @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    private readonly object _gate = new object();

    public async Task RunAsync()
    {
        lock (_gate)
        {
            Monitor.{|#0:Wait|}(_gate, 0);
        }

        await Task.Yield();
    }
}";

        var t = Test(test);
        t.ExpectedDiagnostics.Add(Expected("Monitor.Wait"));
        await t.RunAsync();
    }

    [Fact]
    public async Task GenericLookalikeInTheSameNamespace_ShouldNotReportDiagnostic()
    {
        // A consumer may legally declare its own System.Threading.Thread<T>. It shares the name and
        // namespace of the framework primitive but is unrelated, so matching on those alone is a
        // false positive — the rule compares resolved symbols.
        var test =
            @"
using System.Threading.Tasks;

namespace System.Threading
{
    public class Thread<T>
    {
        public void Join() { }
    }
}

public class TestClass
{
    public async Task RunAsync(System.Threading.Thread<int> worker)
    {
        worker.Join();
        await Task.Yield();
    }
}";

        await Test(test).RunAsync();
    }

    [Fact]
    public async Task ZeroValuedTimeSpanConstructors_ShouldNotReportDiagnostic()
    {
        // `new TimeSpan(0)` (ticks) and `new TimeSpan(0, 0, 0)` are exactly as zero as the
        // parameterless form, so they are probes too.
        var test =
            @"
using System;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(ManualResetEventSlim gate, Thread worker)
    {
        var a = gate.Wait(new TimeSpan(0));
        var b = worker.Join(new TimeSpan(0, 0, 0));
        await Task.Yield();
    }
}";

        await Test(test).RunAsync();
    }

    [Fact]
    public async Task NonZeroTimeSpanConstructor_StillReportsDiagnostic()
    {
        var test =
            @"
using System;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(ManualResetEventSlim gate)
    {
        var entered = gate.{|#0:Wait|}(new TimeSpan(0, 0, 5));
        await Task.Yield();
    }
}";

        var t = Test(test);
        t.ExpectedDiagnostics.Add(Expected("ManualResetEventSlim.Wait"));
        await t.RunAsync();
    }

    [Fact]
    public async Task ReaderWriterLockSlimEnterReadLock_InAsyncMethod_ShouldReportDiagnostic()
    {
        // ReaderWriterLockSlim is not a WaitHandle and has no …Async counterpart, so it belongs
        // on CC031. The curated type map previously omitted it, which left a silent false
        // negative: EnterReadLock parks a pool thread until every writer exits.
        var test =
            @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(ReaderWriterLockSlim gate)
    {
        gate.{|#0:EnterReadLock|}();
        try
        {
            await Task.Yield();
        }
        finally
        {
            gate.ExitReadLock();
        }
    }
}";

        var t = Test(test);
        t.ExpectedDiagnostics.Add(Expected("ReaderWriterLockSlim.EnterReadLock"));
        await t.RunAsync();
    }

    [Fact]
    public async Task ReaderWriterLockSlimEnterWriteLock_InAsyncMethod_ShouldReportDiagnostic()
    {
        var test =
            @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(ReaderWriterLockSlim gate)
    {
        gate.{|#0:EnterWriteLock|}();
        try
        {
            await Task.Yield();
        }
        finally
        {
            gate.ExitWriteLock();
        }
    }
}";

        var t = Test(test);
        t.ExpectedDiagnostics.Add(Expected("ReaderWriterLockSlim.EnterWriteLock"));
        await t.RunAsync();
    }

    [Fact]
    public async Task ReaderWriterLockSlimEnterUpgradeableReadLock_InAsyncMethod_ShouldReportDiagnostic()
    {
        var test =
            @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(ReaderWriterLockSlim gate)
    {
        gate.{|#0:EnterUpgradeableReadLock|}();
        try
        {
            await Task.Yield();
        }
        finally
        {
            gate.ExitUpgradeableReadLock();
        }
    }
}";

        var t = Test(test);
        t.ExpectedDiagnostics.Add(Expected("ReaderWriterLockSlim.EnterUpgradeableReadLock"));
        await t.RunAsync();
    }

    [Fact]
    public async Task ReaderWriterLockSlimEnterReadLock_InSyncMethod_ShouldNotReportDiagnostic()
    {
        var test =
            @"
using System.Threading;

public class TestClass
{
    public void Run(ReaderWriterLockSlim gate)
    {
        gate.EnterReadLock();
        gate.ExitReadLock();
    }
}";

        await Test(test).RunAsync();
    }

    [Fact]
    public async Task ReaderWriterLockSlimTryEnterWriteLockInfinite_InAsyncMethod_ShouldReportDiagnostic()
    {
        // TryEnterWriteLock(Timeout.Infinite) is EnterWriteLock by another name: it parks the
        // thread with no deadline. Leaving TryEnter* off the map was a remaining false negative.
        var test =
            @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(ReaderWriterLockSlim gate)
    {
        if (gate.{|#0:TryEnterWriteLock|}(Timeout.Infinite))
        {
            try
            {
                await Task.Yield();
            }
            finally
            {
                gate.ExitWriteLock();
            }
        }
    }
}";

        var t = Test(test);
        t.ExpectedDiagnostics.Add(Expected("ReaderWriterLockSlim.TryEnterWriteLock"));
        await t.RunAsync();
    }

    [Fact]
    public async Task ReaderWriterLockSlimTryEnterReadLockNonZeroTimeout_ShouldReportDiagnostic()
    {
        var test =
            @"
using System;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(ReaderWriterLockSlim gate)
    {
        if (gate.{|#0:TryEnterReadLock|}(TimeSpan.FromSeconds(5)))
        {
            gate.ExitReadLock();
        }

        await Task.Yield();
    }
}";

        var t = Test(test);
        t.ExpectedDiagnostics.Add(Expected("ReaderWriterLockSlim.TryEnterReadLock"));
        await t.RunAsync();
    }

    [Fact]
    public async Task ReaderWriterLockSlimTryEnterReadLockZeroTimeout_ShouldNotReportDiagnostic()
    {
        // A zero timeout is an immediate probe, matching Wait(0) on the other primitives.
        var test =
            @"
using System;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(ReaderWriterLockSlim gate)
    {
        var a = gate.TryEnterReadLock(0);
        var b = gate.TryEnterWriteLock(TimeSpan.Zero);
        await Task.Yield();
    }
}";

        await Test(test).RunAsync();
    }

    [Fact]
    public async Task BarrierSignalAndWait_InAsyncMethod_ShouldReportDiagnostic()
    {
        // Barrier is not a WaitHandle and has no …Async counterpart. SignalAndWait parks every
        // participant until the last one arrives — the same class of problem as Join / WaitOne.
        var test =
            @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(Barrier barrier)
    {
        barrier.{|#0:SignalAndWait|}();
        await Task.Yield();
    }
}";

        var t = Test(test);
        t.ExpectedDiagnostics.Add(Expected("Barrier.SignalAndWait"));
        await t.RunAsync();
    }

    [Fact]
    public async Task BarrierSignalAndWaitInfinite_ShouldReportDiagnostic()
    {
        // Timeout.Infinite is SignalAndWait() by another name: unbounded park of every participant.
        var test =
            @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(Barrier barrier)
    {
        barrier.{|#0:SignalAndWait|}(Timeout.Infinite);
        await Task.Yield();
    }
}";

        var t = Test(test);
        t.ExpectedDiagnostics.Add(Expected("Barrier.SignalAndWait"));
        await t.RunAsync();
    }

    [Fact]
    public async Task BarrierSignalAndWaitNonZeroTimeout_ShouldReportDiagnostic()
    {
        var test =
            @"
using System;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(Barrier barrier)
    {
        barrier.{|#0:SignalAndWait|}(TimeSpan.FromSeconds(5));
        await Task.Yield();
    }
}";

        var t = Test(test);
        t.ExpectedDiagnostics.Add(Expected("Barrier.SignalAndWait"));
        await t.RunAsync();
    }

    [Fact]
    public async Task BarrierSignalAndWaitZeroTimeout_StillReportsDiagnostic()
    {
        // Unlike Wait(0) on ManualResetEventSlim, SignalAndWait(0) is not a pure probe: the last
        // arriver still runs the post-phase action synchronously before returning.
        var test =
            @"
using System;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(Barrier barrier)
    {
        var a = barrier.{|#0:SignalAndWait|}(0);
        var b = barrier.{|#1:SignalAndWait|}(TimeSpan.Zero);
        await Task.Yield();
    }
}";

        var t = Test(test);
        t.ExpectedDiagnostics.Add(Expected("Barrier.SignalAndWait"));
        t.ExpectedDiagnostics.Add(
            new DiagnosticResult("CC031", DiagnosticSeverity.Warning)
                .WithLocation(1)
                .WithArguments("Barrier.SignalAndWait")
        );
        await t.RunAsync();
    }

    [Fact]
    public async Task BarrierSignalAndWait_InSyncMethod_ShouldNotReportDiagnostic()
    {
        var test =
            @"
using System.Threading;

public class TestClass
{
    public void Run(Barrier barrier)
    {
        barrier.SignalAndWait();
    }
}";

        await Test(test).RunAsync();
    }

    [Fact]
    public async Task BarrierLookalike_ShouldNotReportDiagnostic()
    {
        var test =
            @"
using System.Threading.Tasks;

public class Barrier
{
    public void SignalAndWait() { }
}

public class TestClass
{
    public async Task RunAsync(Barrier barrier)
    {
        barrier.SignalAndWait();
        await Task.Yield();
    }
}";

        await Test(test).RunAsync();
    }

    [Fact]
    public async Task ReaderWriterLockSlimLookalike_ShouldNotReportDiagnostic()
    {
        var test =
            @"
using System.Threading.Tasks;

public class ReaderWriterLockSlim
{
    public void EnterReadLock() { }
    public void EnterWriteLock() { }
    public void ExitReadLock() { }
}

public class TestClass
{
    public async Task RunAsync(ReaderWriterLockSlim gate)
    {
        gate.EnterReadLock();
        gate.EnterWriteLock();
        await Task.Yield();
    }
}";

        await Test(test).RunAsync();
    }
}
