using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace CancelCop.Analyzer.Tests;

/// <summary>
/// CC038: blocking <c>TcpListener.AcceptTcpClient</c> / <c>AcceptSocket</c> in async code.
/// CC036 is <c>Socket</c>-only; CC037 is <c>TcpClient.Connect</c>. The listener accept
/// path is a third type, and none of the shipped rules see it.
/// </summary>
public class BlockingTcpListenerTests
{
    private sealed class AllAnalyzersTest
        : CSharpAnalyzerTest<MissingCancellationTokenAnalyzer, DefaultVerifier>
    {
        protected override IEnumerable<DiagnosticAnalyzer> GetDiagnosticAnalyzers() =>
            typeof(MissingCancellationTokenAnalyzer)
                .Assembly.GetTypes()
                .Where(t => !t.IsAbstract && typeof(DiagnosticAnalyzer).IsAssignableFrom(t))
                .Select(t => (DiagnosticAnalyzer)Activator.CreateInstance(t)!);
    }

    private static DiagnosticResult Expected(string name) =>
        new DiagnosticResult("CC038", DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments(name);

    [Fact]
    public async Task AcceptTcpClient_InAsyncMethod_IsMissedByEveryShippedRule()
    {
        // Empirical gap: AcceptTcpClient parks a pool thread until a client connects.
        // CC036 is Socket-only; CC037 is TcpClient.Connect; CC002 requires a token
        // overload of the invoked method, and AcceptTcpClient has none.
        var test =
            @"
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class Server
{
    public async Task RunAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        listener.{|#0:AcceptTcpClient|}();
        await Task.Yield();
    }
}";

        var t = new AllAnalyzersTest
        {
            TestCode = test,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        t.ExpectedDiagnostics.Add(Expected("AcceptTcpClient"));
        await t.RunAsync();
    }

    [Fact]
    public async Task AcceptSocket_InAsyncMethod_ShouldReportDiagnostic()
    {
        var test =
            @"
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class Server
{
    public async Task RunAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        listener.{|#0:AcceptSocket|}();
        await Task.Yield();
    }
}";

        var t = new AllAnalyzersTest
        {
            TestCode = test,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        t.ExpectedDiagnostics.Add(Expected("AcceptSocket"));
        await t.RunAsync();
    }

    [Fact]
    public async Task AcceptTcpClient_AfterPendingGuard_ShouldNotReportDiagnostic()
    {
        // Pending() is the documented non-blocking probe: AcceptTcpClient then returns
        // immediately because a client is already queued.
        var test =
            @"
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class Server
{
    public async Task RunAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (listener.Pending())
            listener.AcceptTcpClient();
        await Task.Yield();
    }
}";

        var t = new AllAnalyzersTest
        {
            TestCode = test,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        await t.RunAsync();
    }

    [Fact]
    public async Task AcceptTcpClient_AfterNegatedPendingGuard_ShouldReportDiagnostic()
    {
        // if (!Pending()) is the blocking path: no client is queued, so Accept parks.
        var test =
            @"
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class Server
{
    public async Task RunAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!listener.Pending())
            listener.{|#0:AcceptTcpClient|}();
        await Task.Yield();
    }
}";

        var t = new AllAnalyzersTest
        {
            TestCode = test,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        t.ExpectedDiagnostics.Add(Expected("AcceptTcpClient"));
        await t.RunAsync();
    }

    [Fact]
    public async Task AcceptTcpClient_AfterPendingEqualsFalse_ShouldReportDiagnostic()
    {
        var test =
            @"
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class Server
{
    public async Task RunAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (listener.Pending() == false)
            listener.{|#0:AcceptTcpClient|}();
        await Task.Yield();
    }
}";

        var t = new AllAnalyzersTest
        {
            TestCode = test,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        t.ExpectedDiagnostics.Add(Expected("AcceptTcpClient"));
        await t.RunAsync();
    }

    [Fact]
    public async Task AcceptTcpClient_WhilePending_ShouldNotReportDiagnostic()
    {
        var test =
            @"
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class Server
{
    public async Task RunAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        while (listener.Pending())
        {
            cancellationToken.ThrowIfCancellationRequested();
            listener.AcceptTcpClient();
        }
        await Task.Yield();
    }
}";

        var t = new AllAnalyzersTest
        {
            TestCode = test,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        await t.RunAsync();
    }

    [Fact]
    public async Task AcceptTcpClient_WhileNotPending_ShouldReportDiagnostic()
    {
        var test =
            @"
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class Server
{
    public async Task RunAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        while (!listener.Pending())
        {
            cancellationToken.ThrowIfCancellationRequested();
            listener.{|#0:AcceptTcpClient|}();
        }
        await Task.Yield();
    }
}";

        var t = new AllAnalyzersTest
        {
            TestCode = test,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        t.ExpectedDiagnostics.Add(Expected("AcceptTcpClient"));
        await t.RunAsync();
    }

    [Fact]
    public async Task AcceptTcpClient_AfterNegatedPendingContinue_ShouldNotReportDiagnostic()
    {
        // Canonical poll: skip the accept when nothing is queued.
        var test =
            @"
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class Server
{
    public async Task RunAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            if (!listener.Pending())
            {
                await Task.Delay(10, cancellationToken);
                continue;
            }
            listener.AcceptTcpClient();
        }
    }
}";

        var t = new AllAnalyzersTest
        {
            TestCode = test,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        await t.RunAsync();
    }

    [Fact]
    public async Task AcceptTcpClient_WhilePendingAndNotCancelled_ShouldNotReportDiagnostic()
    {
        var test =
            @"
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class Server
{
    public async Task RunAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && listener.Pending())
            listener.AcceptTcpClient();
        await Task.Yield();
    }
}";

        var t = new AllAnalyzersTest
        {
            TestCode = test,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        await t.RunAsync();
    }

    [Fact]
    public async Task AcceptTcpClient_AfterNegatedPendingReturn_ShouldNotReportDiagnostic()
    {
        // Method-level inverted poll: leave when nothing is queued.
        var test =
            @"
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class Server
{
    public async Task RunAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!listener.Pending())
            return;
        listener.AcceptTcpClient();
        await Task.Yield();
    }
}";

        var t = new AllAnalyzersTest
        {
            TestCode = test,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        await t.RunAsync();
    }

    [Fact]
    public async Task AcceptSocket_AfterNegatedPendingGuard_ShouldReportDiagnostic()
    {
        var test =
            @"
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class Server
{
    public async Task RunAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!listener.Pending())
            listener.{|#0:AcceptSocket|}();
        await Task.Yield();
    }
}";

        var t = new AllAnalyzersTest
        {
            TestCode = test,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        t.ExpectedDiagnostics.Add(Expected("AcceptSocket"));
        await t.RunAsync();
    }

    [Fact]
    public async Task AcceptTcpClient_AfterNegatedPendingWithoutExit_ShouldReportDiagnostic()
    {
        // Fall-through after a negated probe is still the blocking path.
        var test =
            @"
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class Server
{
    public async Task RunAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!listener.Pending())
            await Task.Delay(10, cancellationToken);
        listener.{|#0:AcceptTcpClient|}();
        await Task.Yield();
    }
}";

        var t = new AllAnalyzersTest
        {
            TestCode = test,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        t.ExpectedDiagnostics.Add(Expected("AcceptTcpClient"));
        await t.RunAsync();
    }

    [Fact]
    public async Task AcceptTcpClient_AfterPositivePendingContinue_ShouldReportDiagnostic()
    {
        // if (Pending()) continue; then Accept runs when nothing is queued.
        var test =
            @"
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class Server
{
    public async Task RunAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            if (listener.Pending())
                continue;
            listener.{|#0:AcceptTcpClient|}();
        }
    }
}";

        var t = new AllAnalyzersTest
        {
            TestCode = test,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        t.ExpectedDiagnostics.Add(Expected("AcceptTcpClient"));
        await t.RunAsync();
    }

    [Fact]
    public async Task AcceptTcpClient_AfterPendingIsTrue_ShouldNotReportDiagnostic()
    {
        var test =
            @"
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class Server
{
    public async Task RunAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (listener.Pending() is true)
            listener.AcceptTcpClient();
        await Task.Yield();
    }
}";

        var t = new AllAnalyzersTest
        {
            TestCode = test,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        await t.RunAsync();
    }

    [Fact]
    public async Task AcceptTcpClient_InElseOfNegatedPending_ShouldNotReportDiagnostic()
    {
        // if (!Pending()) { } else Accept  is the same as if (Pending()) Accept.
        var test =
            @"
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class Server
{
    public async Task RunAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!listener.Pending())
        {
        }
        else
        {
            listener.AcceptTcpClient();
        }
        await Task.Yield();
    }
}";

        var t = new AllAnalyzersTest
        {
            TestCode = test,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        await t.RunAsync();
    }

    [Fact]
    public async Task AcceptTcpClient_AfterOtherListenerPending_ShouldReportDiagnostic()
    {
        var test =
            @"
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class Server
{
    public async Task RunAsync(TcpListener listener, TcpListener other, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (other.Pending())
            listener.{|#0:AcceptTcpClient|}();
        await Task.Yield();
    }
}";

        var t = new AllAnalyzersTest
        {
            TestCode = test,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        t.ExpectedDiagnostics.Add(Expected("AcceptTcpClient"));
        await t.RunAsync();
    }

    [Fact]
    public async Task AcceptTcpClient_AfterServerBlockingFalse_ShouldNotReportDiagnostic()
    {
        var test =
            @"
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class Server
{
    public async Task RunAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        listener.Server.Blocking = false;
        listener.AcceptTcpClient();
        await Task.Yield();
    }
}";

        var t = new AllAnalyzersTest
        {
            TestCode = test,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        await t.RunAsync();
    }

    [Fact]
    public async Task AcceptTcpClient_InAsyncLambda_ShouldReportDiagnostic()
    {
        var test =
            @"
using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class Server
{
    public void Run(TcpListener listener, CancellationToken cancellationToken)
    {
        Func<Task> work = async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            listener.{|#0:AcceptTcpClient|}();
            await Task.Yield();
        };
    }
}";

        var t = new AllAnalyzersTest
        {
            TestCode = test,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        t.ExpectedDiagnostics.Add(Expected("AcceptTcpClient"));
        await t.RunAsync();
    }

    [Fact]
    public async Task AcceptTcpClient_InSyncLambdaInsideAsyncMethod_ShouldNotReportDiagnostic()
    {
        var test =
            @"
using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class Server
{
    public async Task RunAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Action accept = () => listener.AcceptTcpClient();
        await Task.Yield();
    }
}";

        var t = new AllAnalyzersTest
        {
            TestCode = test,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        await t.RunAsync();
    }

    [Fact]
    public async Task AcceptTcpClient_NullConditional_ShouldReportDiagnostic()
    {
        var test =
            @"
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class Server
{
    public async Task RunAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        listener?.{|#0:AcceptTcpClient|}();
        await Task.Yield();
    }
}";

        var t = new AllAnalyzersTest
        {
            TestCode = test,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        t.ExpectedDiagnostics.Add(Expected("AcceptTcpClient"));
        await t.RunAsync();
    }

    [Fact]
    public async Task AcceptTcpClient_AfterPendingThenReassigned_ShouldReportDiagnostic()
    {
        var test =
            @"
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class Server
{
    public async Task RunAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (listener.Pending())
            listener = new TcpListener(IPAddress.Any, 0);
        listener.{|#0:AcceptTcpClient|}();
        await Task.Yield();
    }
}";

        var t = new AllAnalyzersTest
        {
            TestCode = test,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        t.ExpectedDiagnostics.Add(Expected("AcceptTcpClient"));
        await t.RunAsync();
    }

    [Fact]
    public async Task AcceptTcpClient_AfterOtherListenerServerBlockingFalse_ShouldReportDiagnostic()
    {
        var test =
            @"
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class Server
{
    public async Task RunAsync(TcpListener listener, TcpListener other, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        other.Server.Blocking = false;
        listener.{|#0:AcceptTcpClient|}();
        await Task.Yield();
    }
}";

        var t = new AllAnalyzersTest
        {
            TestCode = test,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        t.ExpectedDiagnostics.Add(Expected("AcceptTcpClient"));
        await t.RunAsync();
    }

    [Fact]
    public async Task AcceptTcpClient_NullForgivingAfterPending_ShouldNotReportDiagnostic()
    {
        var test =
            @"
#nullable enable
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class Server
{
    public async Task RunAsync(TcpListener? listener, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (listener!.Pending())
            listener.AcceptTcpClient();
        await Task.Yield();
    }
}";

        var t = new AllAnalyzersTest
        {
            TestCode = test,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        await t.RunAsync();
    }

    [Fact]
    public async Task AcceptTcpClient_AfterPendingThenRefReplacement_ShouldReportDiagnostic()
    {
        var test =
            @"
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class Server
{
    public async Task RunAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (listener.Pending())
            Replace(out listener);
        listener.{|#0:AcceptTcpClient|}();
        await Task.Yield();
    }

    private static void Replace(out TcpListener listener) =>
        listener = new TcpListener(IPAddress.Any, 0);
}";

        var t = new AllAnalyzersTest
        {
            TestCode = test,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        t.ExpectedDiagnostics.Add(Expected("AcceptTcpClient"));
        await t.RunAsync();
    }

    [Fact]
    public async Task AcceptTcpClient_ImplicitThisAfterPending_ShouldNotReportDiagnostic()
    {
        var test =
            @"
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class Server : TcpListener
{
    public Server() : base(System.Net.IPAddress.Any, 0) { }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Pending())
            AcceptTcpClient();
        await Task.Yield();
    }
}";

        var t = new AllAnalyzersTest
        {
            TestCode = test,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        await t.RunAsync();
    }

    [Fact]
    public async Task AcceptTcpClient_InSyncMethod_ShouldNotReportDiagnostic()
    {
        var test =
            @"
using System.Net.Sockets;

public class Server
{
    public void Run(TcpListener listener)
    {
        listener.AcceptTcpClient();
    }
}";

        var t = new AllAnalyzersTest
        {
            TestCode = test,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        await t.RunAsync();
    }

    [Fact]
    public async Task LookalikeAcceptTcpClient_ShouldNotReportDiagnostic()
    {
        var test =
            @"
using System.Threading.Tasks;

public class TcpListener
{
    public object AcceptTcpClient() => new object();
}

public class Server
{
    public async Task RunAsync(TcpListener listener, System.Threading.CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        listener.AcceptTcpClient();
        await Task.Yield();
    }
}";

        var t = new AllAnalyzersTest
        {
            TestCode = test,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        await t.RunAsync();
    }
}
