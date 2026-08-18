using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace CancelCop.Analyzer.Tests;

/// <summary>
/// CC037: blocking <c>TcpClient.Connect</c> in async code. CC036 covers <c>Socket</c> only;
/// <c>TcpClient</c> is the wrapper most application code uses, and none of the shipped rules
/// see it.
/// </summary>
public class BlockingTcpClientTests
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
        new DiagnosticResult("CC037", DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("Connect");

    [Fact]
    public async Task Connect_InAsyncMethod_IsMissedByEveryShippedRule()
    {
        // Empirical gap: TcpClient.Connect parks a pool thread until the handshake finishes
        // (or TCP times out). CC036 is Socket-only; CC028 is System.IO; CC002 requires a
        // token-taking overload of the invoked method, and Connect has none.
        var test =
            @"
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class Client
{
    public async Task RunAsync(TcpClient client, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        client.{|#0:Connect|}(""localhost"", 80);
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
    public async Task Connect_IpAddressAfterClientBlockingFalse_ShouldNotReportDiagnostic()
    {
        // TcpClient.Connect(IPAddress, int) on a non-blocking underlying socket returns
        // WouldBlock instead of parking. Exempt only when this client's Client.Blocking
        // is set false — hostname overloads still do synchronous DNS.
        var test =
            @"
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class Client
{
    public async Task RunAsync(TcpClient client, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        client.Client.Blocking = false;
        client.Connect(IPAddress.Loopback, 80);
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
    public async Task Connect_IpAddressAfterClientBlockingFalse_InTopLevelProgram_ShouldNotReportDiagnostic()
    {
        var test =
            @"
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

var client = new TcpClient();
client.Client.Blocking = false;
client.Connect(IPAddress.Loopback, 80);
await Task.Yield();
";

        var t = new AllAnalyzersTest
        {
            TestCode = test,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        t.TestState.OutputKind = OutputKind.ConsoleApplication;
        await t.RunAsync();
    }

    [Fact]
    public async Task Connect_HostnameAfterClientBlockingFalse_ShouldReportDiagnostic()
    {
        // TcpClient.Connect(string, int) still calls Dns.GetHostAddresses synchronously.
        // Non-blocking mode does not make hostname Connect a probe.
        var test =
            @"
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class Client
{
    public async Task RunAsync(TcpClient client, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        client.Client.Blocking = false;
        client.{|#0:Connect|}(""localhost"", 80);
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
    public async Task Connect_AfterUnrelatedSocketBlockingFalse_ShouldReportDiagnostic()
    {
        // An unrelated socket's Blocking = false must not silence this TcpClient.
        var test =
            @"
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class Client
{
    public async Task RunAsync(TcpClient client, Socket other, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        other.Blocking = false;
        client.{|#0:Connect|}(IPAddress.Loopback, 80);
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
    public async Task Connect_AfterOtherTcpClientBlockingFalse_ShouldReportDiagnostic()
    {
        var test =
            @"
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class Client
{
    public async Task RunAsync(TcpClient client, TcpClient other, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        other.Client.Blocking = false;
        client.{|#0:Connect|}(IPAddress.Loopback, 80);
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
    public async Task Connect_AfterDifferentPropertyOwnerBlockingFalse_ShouldReportDiagnostic()
    {
        // first.Tcp and second.Tcp share the property symbol Holder.Tcp; the exemption
        // must not treat them as the same instance.
        var test =
            @"
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class Holder
{
    public TcpClient Tcp { get; } = new TcpClient();
}

public class Client
{
    public async Task RunAsync(Holder first, Holder second, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        first.Tcp.Client.Blocking = false;
        second.Tcp.{|#0:Connect|}(IPAddress.Loopback, 80);
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
    public async Task Connect_IpAddressAfterPropertyAccessBlockingFalse_ShouldReportDiagnostic()
    {
        // Property access cannot prove instance identity — a getter may return a new
        // TcpClient on each read. Decline the exemption unless the receiver is a
        // local, parameter, or field.
        var test =
            @"
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class Holder
{
    public TcpClient Tcp { get; } = new TcpClient();
}

public class Client
{
    public async Task RunAsync(Holder first, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        first.Tcp.Client.Blocking = false;
        first.Tcp.{|#0:Connect|}(IPAddress.Loopback, 80);
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
    public async Task Connect_AfterBareFactoryPropertyBlockingFalse_ShouldReportDiagnostic()
    {
        var test =
            @"
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class Client
{
    public TcpClient Tcp => new TcpClient();

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Tcp.Client.Blocking = false;
        Tcp.{|#0:Connect|}(IPAddress.Loopback, 80);
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
    public async Task Connect_AfterFactoryPropertyBlockingFalse_ShouldReportDiagnostic()
    {
        var test =
            @"
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class Holder
{
    public TcpClient Tcp => new TcpClient();
}

public class Client
{
    public async Task RunAsync(Holder holder, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        holder.Tcp.Client.Blocking = false;
        holder.Tcp.{|#0:Connect|}(IPAddress.Loopback, 80);
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
    public async Task Connect_ImplicitThisAfterClientBlockingFalse_ShouldNotReportDiagnostic()
    {
        var test =
            @"
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class Client : TcpClient
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Client.Blocking = false;
        Connect(IPAddress.Loopback, 80);
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
    public async Task Connect_ThisQualifiedFieldAndBareField_ShouldNotReportDiagnostic()
    {
        var test =
            @"
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class Client
{
    private TcpClient client = new TcpClient();

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        this.client.Client.Blocking = false;
        client.Connect(IPAddress.Loopback, 80);
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
    public async Task Connect_AfterBareFieldReassigned_ThisQualifiedConnect_ShouldReportDiagnostic()
    {
        var test =
            @"
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class Client
{
    private TcpClient client = new TcpClient();

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        this.client.Client.Blocking = false;
        client = new TcpClient();
        this.client.{|#0:Connect|}(IPAddress.Loopback, 80);
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
    public async Task Connect_AfterRefReplacement_ShouldReportDiagnostic()
    {
        var test =
            @"
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class Client
{
    public async Task RunAsync(TcpClient client, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        client.Client.Blocking = false;
        Replace(ref client);
        client.{|#0:Connect|}(IPAddress.Loopback, 80);
        await Task.Yield();
    }

    private static void Replace(ref TcpClient client) => client = new TcpClient();
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
    public async Task Connect_BaseCallAfterClientBlockingFalse_ShouldNotReportDiagnostic()
    {
        var test =
            @"
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class Client : TcpClient
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Client.Blocking = false;
        base.Connect(IPAddress.Loopback, 80);
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
    public async Task Connect_AfterDerivedNestedClientInitializerBlockingFalse_ShouldNotReportDiagnostic()
    {
        var test =
            @"
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class DerivedClient : TcpClient { }

public class Client
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var client = new DerivedClient { Client = { Blocking = false } };
        client.Connect(IPAddress.Loopback, 80);
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
    public async Task Connect_AfterNestedClientInitializerBlockingFalse_ShouldNotReportDiagnostic()
    {
        var test =
            @"
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class Client
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var client = new TcpClient { Client = { Blocking = false } };
        client.Connect(IPAddress.Loopback, 80);
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
    public async Task Connect_AfterNestedDeconstructionReplacement_ShouldReportDiagnostic()
    {
        var test =
            @"
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class Client
{
    public async Task RunAsync(TcpClient client, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        client.Client.Blocking = false;
        ((client, _), _) = ((new TcpClient(), 0), 0);
        client.{|#0:Connect|}(IPAddress.Loopback, 80);
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
    public async Task Connect_AfterBlockingResetToTrue_ShouldReportDiagnostic()
    {
        var test =
            @"
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class Client
{
    public async Task RunAsync(TcpClient client, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        client.Client.Blocking = false;
        client.Client.Blocking = true;
        client.{|#0:Connect|}(IPAddress.Loopback, 80);
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
    public async Task Connect_AfterNonConstantBlockingAssignment_ShouldReportDiagnostic()
    {
        var test =
            @"
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class Client
{
    public async Task RunAsync(TcpClient client, bool flag, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        client.Client.Blocking = flag;
        client.{|#0:Connect|}(IPAddress.Loopback, 80);
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
    public async Task Connect_AfterDeconstructionReplacement_ShouldReportDiagnostic()
    {
        var test =
            @"
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class Client
{
    public async Task RunAsync(TcpClient client, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        client.Client.Blocking = false;
        (client, _) = (new TcpClient(), 0);
        client.{|#0:Connect|}(IPAddress.Loopback, 80);
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
    public async Task Connect_NullForgivingLocalAfterBlockingFalse_ShouldNotReportDiagnostic()
    {
        var test =
            @"
#nullable enable
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class Client
{
    public async Task RunAsync(TcpClient? client, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        client!.Client.Blocking = false;
        client!.Connect(IPAddress.Loopback, 80);
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
    public async Task Connect_AfterClientReassigned_ShouldReportDiagnostic()
    {
        // Blocking = false applied to the previous instance. The replacement client's
        // socket is still blocking, so the exemption must not follow the variable.
        var test =
            @"
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class Client
{
    public async Task RunAsync(TcpClient client, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        client.Client.Blocking = false;
        client = new TcpClient();
        client.{|#0:Connect|}(IPAddress.Loopback, 80);
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
    public async Task Connect_InSyncMethod_ShouldNotReportDiagnostic()
    {
        var test =
            @"
using System.Net.Sockets;

public class Client
{
    public void Run(TcpClient client)
    {
        client.Connect(""localhost"", 80);
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
    public async Task LookalikeConnect_ShouldNotReportDiagnostic()
    {
        var test =
            @"
using System.Threading.Tasks;

public class TcpClient
{
    public void Connect(string host, int port) { }
}

public class Client
{
    public async Task RunAsync(TcpClient client, System.Threading.CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        client.Connect(""localhost"", 80);
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
