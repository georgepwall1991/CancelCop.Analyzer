using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace CancelCop.Analyzer.Tests;

/// <summary>
/// CC039 fixer: rewritten code is compiled by the harness.
/// Statement <c>Receive(ref endpoint)</c> → <c>await ReceiveAsync</c>.
/// The TAP returns <c>UdpReceiveResult</c> and does not take the
/// <c>ref</c> endpoint, so a value-use of the <c>byte[]</c> is reported
/// without a rewrite.
/// </summary>
public class BlockingUdpClientCodeFixTests
{
    private static CSharpCodeFixTest<
        BlockingUdpClientAnalyzer,
        BlockingUdpClientCodeFixProvider,
        DefaultVerifier
    > CreateTest(string testCode, string fixedCode, params DiagnosticResult[] expected)
    {
        var test = new CSharpCodeFixTest<
            BlockingUdpClientAnalyzer,
            BlockingUdpClientCodeFixProvider,
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
        new DiagnosticResult("CC039", DiagnosticSeverity.Warning)
            .WithLocation(location)
            .WithArguments("Receive");

    [Fact]
    public async Task ReceiveStatement_WithTokenInScope_FlowsTheToken()
    {
        var test =
            @"
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(UdpClient client, CancellationToken cancellationToken)
    {
        IPEndPoint? remote = null;
        client.{|#0:Receive|}(ref remote);
        await Task.Yield();
    }
}";

        var fixedCode =
            @"
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(UdpClient client, CancellationToken cancellationToken)
    {
        IPEndPoint? remote = null;
        var received = await client.ReceiveAsync(cancellationToken);
        remote = received.RemoteEndPoint;
        await Task.Yield();
    }
}";

        await CreateTest(test, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task ReceiveStatement_WithoutTokenInScope_StillCompiles()
    {
        var test =
            @"
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(UdpClient client)
    {
        IPEndPoint? remote = null;
        client.{|#0:Receive|}(ref remote);
        await Task.Yield();
    }
}";

        var fixedCode =
            @"
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(UdpClient client)
    {
        IPEndPoint? remote = null;
        var received = await client.ReceiveAsync();
        remote = received.RemoteEndPoint;
        await Task.Yield();
    }
}";

        await CreateTest(test, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task ReceiveStatement_ThenUsesEndpoint_AssignsRemoteEndPoint()
    {
        var test =
            @"
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task<int> RunAsync(UdpClient client, CancellationToken cancellationToken)
    {
        IPEndPoint? remote = null;
        client.{|#0:Receive|}(ref remote);
        return remote!.Port;
    }
}";

        var fixedCode =
            @"
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task<int> RunAsync(UdpClient client, CancellationToken cancellationToken)
    {
        IPEndPoint? remote = null;
        var received = await client.ReceiveAsync(cancellationToken);
        remote = received.RemoteEndPoint;
        return remote!.Port;
    }
}";

        await CreateTest(test, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task ReceiveStatement_BracelessIf_ReportsWithoutOfferingAFix()
    {
        var source =
            @"
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(UdpClient client, CancellationToken cancellationToken, bool ready)
    {
        IPEndPoint? remote = null;
        if (ready)
            client.{|#0:Receive|}(ref remote);
        await Task.Yield();
    }
}";

        await CreateTest(source, source, Expected()).RunAsync();
    }

    [Fact]
    public async Task ReceiveStatement_UnusedParameterNamedReceived_UsesReceived1()
    {
        var test =
            @"
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(UdpClient client, CancellationToken cancellationToken, int received)
    {
        IPEndPoint? remote = null;
        client.{|#0:Receive|}(ref remote);
        await Task.Yield();
    }
}";

        var fixedCode =
            @"
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(UdpClient client, CancellationToken cancellationToken, int received)
    {
        IPEndPoint? remote = null;
        var received1 = await client.ReceiveAsync(cancellationToken);
        remote = received1.RemoteEndPoint;
        await Task.Yield();
    }
}";

        await CreateTest(test, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task ReceiveAssignedToByteArray_ReportsWithoutOfferingAFix()
    {
        var source =
            @"
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task<int> RunAsync(UdpClient client, CancellationToken cancellationToken)
    {
        IPEndPoint? remote = null;
        byte[] data = client.{|#0:Receive|}(ref remote);
        return data.Length;
    }
}";

        await CreateTest(source, source, Expected()).RunAsync();
    }

    [Fact]
    public async Task Receive_NullConditional_ReportsWithoutOfferingAFix()
    {
        var source =
            @"
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(UdpClient? client, CancellationToken cancellationToken)
    {
        IPEndPoint? remote = null;
        client?.{|#0:Receive|}(ref remote);
        await Task.Yield();
    }
}";

        await CreateTest(source, source, Expected()).RunAsync();
    }

    [Fact]
    public async Task Receive_InsideLock_ReportsWithoutOfferingAFix()
    {
        var source =
            @"
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    private readonly object _gate = new object();

    public async Task RunAsync(UdpClient client, CancellationToken cancellationToken)
    {
        IPEndPoint? remote = null;
        lock (_gate)
        {
            client.{|#0:Receive|}(ref remote);
        }

        await Task.Yield();
    }
}";

        await CreateTest(source, source, Expected()).RunAsync();
    }

    [Fact]
    public async Task CallFromReceiveAsync_ReportsWithoutOfferingAFix()
    {
        var source =
            @"
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class ProviderClient : UdpClient
{
    public new async Task<UdpReceiveResult> ReceiveAsync(CancellationToken cancellationToken)
    {
        IPEndPoint? remote = null;
        {|#0:Receive|}(ref remote);
        return default;
    }
}";

        await CreateTest(source, source, Expected()).RunAsync();
    }

    [Fact]
    public async Task CastOfThisInsideReceiveAsync_ReportsWithoutOfferingAFix()
    {
        var source =
            @"
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class ProviderClient : UdpClient
{
    public new async Task<UdpReceiveResult> ReceiveAsync(CancellationToken cancellationToken)
    {
        IPEndPoint? remote = null;
        ((ProviderClient)this).{|#0:Receive|}(ref remote);
        return default;
    }
}";

        await CreateTest(source, source, Expected()).RunAsync();
    }

    [Fact]
    public async Task PropertyGetterReturningThis_ReportsWithoutOfferingAFix()
    {
        var source =
            @"
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class ProviderClient : UdpClient
{
    private UdpClient Self
    {
        get { return this; }
    }

    public new async Task<UdpReceiveResult> ReceiveAsync(CancellationToken cancellationToken)
    {
        IPEndPoint? remote = null;
        Self.{|#0:Receive|}(ref remote);
        return default;
    }
}";

        await CreateTest(source, source, Expected()).RunAsync();
    }

    [Fact]
    public async Task ComposedFieldOutsideReceiveAsync_StillFixes()
    {
        var test =
            @"
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class Server
{
    private readonly UdpClient _client;

    public Server(UdpClient client) => _client = client;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        IPEndPoint? remote = null;
        _client.{|#0:Receive|}(ref remote);
        await Task.Yield();
    }
}";

        var fixedCode =
            @"
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class Server
{
    private readonly UdpClient _client;

    public Server(UdpClient client) => _client = client;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        IPEndPoint? remote = null;
        var received = await _client.ReceiveAsync(cancellationToken);
        remote = received.RemoteEndPoint;
        await Task.Yield();
    }
}";

        await CreateTest(test, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task SubclassHidingReceiveAsync_ShouldNotReport()
    {
        var test =
            @"
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class HiddenClient : UdpClient
{
    public new int ReceiveAsync() => 0;
    public new int ReceiveAsync(CancellationToken cancellationToken) => 0;
}

public class TestClass
{
    public async Task RunAsync(HiddenClient client, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IPEndPoint? remote = null;
        client.Receive(ref remote);
        await Task.Yield();
    }
}";

        var t = new CSharpAnalyzerTest<BlockingUdpClientAnalyzer, DefaultVerifier>
        {
            TestCode = test,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        await t.RunAsync();
    }

    [Fact]
    public async Task FixAll_TwoReceiveStatements_BothBecomeAwaitReceiveAsync()
    {
        var test =
            @"
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(UdpClient a, UdpClient b, CancellationToken cancellationToken)
    {
        IPEndPoint? remote = null;
        a.{|#0:Receive|}(ref remote);
        b.{|#1:Receive|}(ref remote);
        await Task.Yield();
    }
}";

        var fixedCode =
            @"
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(UdpClient a, UdpClient b, CancellationToken cancellationToken)
    {
        IPEndPoint? remote = null;
        var received = await a.ReceiveAsync(cancellationToken);
        remote = received.RemoteEndPoint;
        var received1 = await b.ReceiveAsync(cancellationToken);
        remote = received1.RemoteEndPoint;
        await Task.Yield();
    }
}";

        await CreateTest(test, fixedCode, Expected(0), Expected(1)).RunAsync();
    }
}
