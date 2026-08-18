using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace CancelCop.Analyzer.Tests;

/// <summary>
/// CC039: blocking <c>UdpClient.Receive</c> in async code.
/// CC036 is <c>Socket</c>-only; CC037 is <c>TcpClient.Connect</c>; CC038 is
/// <c>TcpListener</c> accept. The UDP wrapper is a fourth type, and none of the
/// shipped rules see it.
/// </summary>
public class BlockingUdpClientTests
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

    private static DiagnosticResult Expected() =>
        new DiagnosticResult("CC039", DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("Receive");

    [Fact]
    public async Task Receive_InAsyncMethod_IsMissedByEveryShippedRule()
    {
        // Empirical gap: UdpClient.Receive parks a pool thread until a datagram
        // arrives. CC036 is Socket-only; CC002 requires a token overload of the
        // invoked method, and Receive has none.
        var test =
            @"
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class Client
{
    public async Task RunAsync(UdpClient client, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IPEndPoint? remote = null;
        client.{|#0:Receive|}(ref remote);
        await Task.Yield();
    }
}";

        var t = new AllAnalyzersTest
        {
            TestCode = test,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        t.ExpectedDiagnostics.Add(Expected());
        await t.RunAsync();
    }

    [Fact]
    public async Task Receive_AfterAvailableGreaterThanZero_ShouldNotReportDiagnostic()
    {
        // Available > 0 is the documented non-blocking probe: a datagram is already queued.
        var test =
            @"
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class Client
{
    public async Task RunAsync(UdpClient client, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IPEndPoint? remote = null;
        if (client.Available > 0)
            client.Receive(ref remote);
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
    public async Task Receive_WhileAvailableGreaterThanZero_ShouldNotReportDiagnostic()
    {
        var test =
            @"
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class Client
{
    public async Task RunAsync(UdpClient client, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IPEndPoint? remote = null;
        while (client.Available > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            client.Receive(ref remote);
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
    public async Task Receive_AfterAvailableZeroContinue_ShouldNotReportDiagnostic()
    {
        var test =
            @"
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class Client
{
    public async Task RunAsync(UdpClient client, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            if (client.Available == 0)
            {
                await Task.Delay(10, cancellationToken);
                continue;
            }
            IPEndPoint? remote = null;
            client.Receive(ref remote);
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
    public async Task Receive_AfterAvailableGreaterThanFour_ShouldNotReportDiagnostic()
    {
        var test =
            @"
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class Client
{
    public async Task RunAsync(UdpClient client, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IPEndPoint? remote = null;
        if (client.Available > 4)
            client.Receive(ref remote);
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
    public async Task Receive_AfterNotAvailableGreaterThanFour_ShouldReportDiagnostic()
    {
        var test =
            @"
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class Client
{
    public async Task RunAsync(UdpClient client, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IPEndPoint? remote = null;
        if (!(client.Available > 4))
            client.{|#0:Receive|}(ref remote);
        await Task.Yield();
    }
}";

        var t = new AllAnalyzersTest
        {
            TestCode = test,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        t.ExpectedDiagnostics.Add(Expected());
        await t.RunAsync();
    }

    [Fact]
    public async Task Receive_AfterAvailableGreaterThanNegativeOne_ShouldReportDiagnostic()
    {
        // Available > -1 is always true and does not prove a datagram is queued.
        var test =
            @"
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class Client
{
    public async Task RunAsync(UdpClient client, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IPEndPoint? remote = null;
        if (client.Available > -1)
            client.{|#0:Receive|}(ref remote);
        await Task.Yield();
    }
}";

        var t = new AllAnalyzersTest
        {
            TestCode = test,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        t.ExpectedDiagnostics.Add(Expected());
        await t.RunAsync();
    }

    [Fact]
    public async Task Receive_InElseOfAvailableGreaterThanFour_ShouldReportDiagnostic()
    {
        var test =
            @"
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class Client
{
    public async Task RunAsync(UdpClient client, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IPEndPoint? remote = null;
        if (client.Available > 4)
            await Task.Yield();
        else
            client.{|#0:Receive|}(ref remote);
    }
}";

        var t = new AllAnalyzersTest
        {
            TestCode = test,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        t.ExpectedDiagnostics.Add(Expected());
        await t.RunAsync();
    }

    [Fact]
    public async Task Receive_AfterZeroLessThanAvailable_ShouldNotReportDiagnostic()
    {
        var test =
            @"
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class Client
{
    public async Task RunAsync(UdpClient client, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IPEndPoint? remote = null;
        if (0 < client.Available)
            client.Receive(ref remote);
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
    public async Task Receive_WhileAvailableAndNotCancelled_ShouldNotReportDiagnostic()
    {
        var test =
            @"
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class Client
{
    public async Task RunAsync(UdpClient client, CancellationToken cancellationToken)
    {
        IPEndPoint? remote = null;
        while (!cancellationToken.IsCancellationRequested && client.Available > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            client.Receive(ref remote);
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
    public async Task Receive_AfterAvailableEqualsZero_ShouldReportDiagnostic()
    {
        var test =
            @"
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class Client
{
    public async Task RunAsync(UdpClient client, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IPEndPoint? remote = null;
        if (client.Available == 0)
            client.{|#0:Receive|}(ref remote);
        await Task.Yield();
    }
}";

        var t = new AllAnalyzersTest
        {
            TestCode = test,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        t.ExpectedDiagnostics.Add(Expected());
        await t.RunAsync();
    }

    [Fact]
    public async Task Receive_AfterClientBlockingFalse_ShouldNotReportDiagnostic()
    {
        var test =
            @"
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class Client
{
    public async Task RunAsync(UdpClient client, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IPEndPoint? remote = null;
        client.Client.Blocking = false;
        client.Receive(ref remote);
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
    public async Task Receive_AfterOtherClientAvailable_ShouldReportDiagnostic()
    {
        var test =
            @"
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class Client
{
    public async Task RunAsync(UdpClient client, UdpClient other, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IPEndPoint? remote = null;
        if (other.Available > 0)
            client.{|#0:Receive|}(ref remote);
        await Task.Yield();
    }
}";

        var t = new AllAnalyzersTest
        {
            TestCode = test,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        t.ExpectedDiagnostics.Add(Expected());
        await t.RunAsync();
    }

    [Fact]
    public async Task Receive_AfterOtherClientBlockingFalse_ShouldReportDiagnostic()
    {
        var test =
            @"
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class Client
{
    public async Task RunAsync(UdpClient client, UdpClient other, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IPEndPoint? remote = null;
        other.Client.Blocking = false;
        client.{|#0:Receive|}(ref remote);
        await Task.Yield();
    }
}";

        var t = new AllAnalyzersTest
        {
            TestCode = test,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        t.ExpectedDiagnostics.Add(Expected());
        await t.RunAsync();
    }

    [Fact]
    public async Task Receive_InAsyncLambda_ShouldReportDiagnostic()
    {
        var test =
            @"
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class Client
{
    public void Run(UdpClient client, CancellationToken cancellationToken)
    {
        Func<Task> work = async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            IPEndPoint? remote = null;
            client.{|#0:Receive|}(ref remote);
            await Task.Yield();
        };
    }
}";

        var t = new AllAnalyzersTest
        {
            TestCode = test,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        t.ExpectedDiagnostics.Add(Expected());
        await t.RunAsync();
    }

    [Fact]
    public async Task Receive_InSyncLambdaInsideAsyncMethod_ShouldNotReportDiagnostic()
    {
        var test =
            @"
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class Client
{
    public async Task RunAsync(UdpClient client, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Action receive = () =>
        {
            IPEndPoint? remote = null;
            client.Receive(ref remote);
        };
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
    public async Task Receive_NullConditional_ShouldReportDiagnostic()
    {
        var test =
            @"
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class Client
{
    public async Task RunAsync(UdpClient client, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IPEndPoint? remote = null;
        client?.{|#0:Receive|}(ref remote);
        await Task.Yield();
    }
}";

        var t = new AllAnalyzersTest
        {
            TestCode = test,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        t.ExpectedDiagnostics.Add(Expected());
        await t.RunAsync();
    }

    [Fact]
    public async Task Receive_ImplicitThisAfterAvailable_ShouldNotReportDiagnostic()
    {
        var test =
            @"
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class Client : UdpClient
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IPEndPoint? remote = null;
        if (Available > 0)
            Receive(ref remote);
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
    public async Task Receive_AfterAvailableThenReassigned_ShouldReportDiagnostic()
    {
        var test =
            @"
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class Client
{
    public async Task RunAsync(UdpClient client, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IPEndPoint? remote = null;
        if (client.Available > 0)
            client = new UdpClient();
        client.{|#0:Receive|}(ref remote);
        await Task.Yield();
    }
}";

        var t = new AllAnalyzersTest
        {
            TestCode = test,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        t.ExpectedDiagnostics.Add(Expected());
        await t.RunAsync();
    }

    [Fact]
    public async Task Receive_InSyncMethod_ShouldNotReportDiagnostic()
    {
        var test =
            @"
using System.Net;
using System.Net.Sockets;

public class Client
{
    public void Run(UdpClient client)
    {
        IPEndPoint? remote = null;
        client.Receive(ref remote);
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
    public async Task LookalikeReceive_ShouldNotReportDiagnostic()
    {
        var test =
            @"
using System.Net;
using System.Threading.Tasks;

public class UdpClient
{
    public byte[] Receive(ref IPEndPoint? remote) => [];
}

public class Client
{
    public async Task RunAsync(UdpClient client, System.Threading.CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IPEndPoint? remote = null;
        client.Receive(ref remote);
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
