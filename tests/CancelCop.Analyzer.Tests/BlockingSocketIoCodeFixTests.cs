using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace CancelCop.Analyzer.Tests;

/// <summary>
/// CC036 fixer: rewritten code is compiled by the harness.
/// <c>Receive(byte[])</c> → <c>await ReceiveAsync(buffer, ct)</c> — the implicit
/// <c>byte[]</c> → Memory&lt;byte&gt; conversion makes the TAP arity bind, which
/// the fixer proves by speculative binding before offering the rewrite. Arities
/// without a compiling counterpart (flag-bearing forms, endpoint connects) stay
/// unfixed by design.
/// </summary>
public class BlockingSocketIoCodeFixTests
{
    private static CSharpCodeFixTest<
        BlockingSocketIoAnalyzer,
        BlockingSocketIoCodeFixProvider,
        DefaultVerifier
    > CreateTest(string testCode, string fixedCode, params DiagnosticResult[] expected)
    {
        var test = new CSharpCodeFixTest<
            BlockingSocketIoAnalyzer,
            BlockingSocketIoCodeFixProvider,
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
        new DiagnosticResult("CC036", DiagnosticSeverity.Warning)
            .WithLocation(location)
            .WithArguments("Receive");

    [Fact]
    public async Task Receive_WithToken_FlowsIntoReceiveAsync()
    {
        var test =
            @"
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class Server
{
    public async Task RunAsync(Socket socket, byte[] buffer, CancellationToken cancellationToken)
    {
        socket.{|#0:Receive|}(buffer);
        await Task.Yield();
    }
}";

        var fixedCode =
            @"
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class Server
{
    public async Task RunAsync(Socket socket, byte[] buffer, CancellationToken cancellationToken)
    {
        await socket.ReceiveAsync(buffer, cancellationToken);
        await Task.Yield();
    }
}";

        await CreateTest(test, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task Receive_WithoutTokenInScope_StillRewritesTokenless()
    {
        // ReceiveAsync(Memory<byte>) exists without a token; the rewrite compiles.
        var test =
            @"
using System.Net.Sockets;
using System.Threading.Tasks;

public class Server
{
    public async Task RunAsync(Socket socket, byte[] buffer)
    {
        socket.{|#0:Receive|}(buffer);
        await Task.Yield();
    }
}";

        var fixedCode =
            @"
using System.Net.Sockets;
using System.Threading.Tasks;

public class Server
{
    public async Task RunAsync(Socket socket, byte[] buffer)
    {
        await socket.ReceiveAsync(buffer);
        await Task.Yield();
    }
}";

        await CreateTest(test, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task Send_SameShape_RewritesToSendAsync()
    {
        var test =
            @"
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class Client
{
    public async Task RunAsync(Socket socket, byte[] payload, CancellationToken ct)
    {
        socket.{|#0:Send|}(payload);
        await Task.Yield();
    }
}";

        var fixedCode =
            @"
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class Client
{
    public async Task RunAsync(Socket socket, byte[] payload, CancellationToken ct)
    {
        await socket.SendAsync(payload, ct);
        await Task.Yield();
    }
}";

        var expected = new DiagnosticResult("CC036", DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("Send");
        await CreateTest(test, fixedCode, expected).RunAsync();
    }

    [Fact]
    public async Task Receive_NullConditionalSpine_HoistsWithToken()
    {
        var test =
            @"
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class Server
{
    public async Task RunAsync(Socket? socket, byte[] buffer, CancellationToken cancellationToken)
    {
        socket?.{|#0:Receive|}(buffer);
        await Task.Yield();
    }
}";

        var fixedCode =
            @"
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class Server
{
    public async Task RunAsync(Socket? socket, byte[] buffer, CancellationToken cancellationToken)
    {
        if (socket is not null)
        {
            await socket.ReceiveAsync(buffer, cancellationToken);
        }
        await Task.Yield();
    }
}";

        await CreateTest(test, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task Receive_InsideLock_ReportsWithoutOfferingAFix()
    {
        // The hoist would land its if-statement inside the same lock body, where
        // await stays illegal — the rewrite is withheld entirely.
        var source =
            @"
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class Server
{
    private readonly object sync = new();

    public async Task RunAsync(Socket socket, byte[] buffer)
    {
        lock (sync)
        {
            socket.{|#0:Receive|}(buffer);
        }
        await Task.Yield();
    }
}";

        await CreateTest(source, source, Expected()).RunAsync();
    }

    [Fact]
    public async Task Receive_LookalikeClass_StaysQuiet()
    {
        // A same-named member on an unrelated type is not Socket's Receive.
        var source =
            @"
using System.Threading.Tasks;

public class MySocket
{
    public int Receive(byte[] buffer) => 0;
    public Task<int> ReceiveAsync(byte[] buffer) => Task.FromResult(0);
}

public class TestClass
{
    public async Task RunAsync(MySocket socket, byte[] buffer)
    {
        socket.Receive(buffer);
        await Task.Yield();
    }
}";

        await CreateTest(source, source).RunAsync();
    }

    [Fact]
    public async Task Send_WithSocketFlags_BindsFlagsArity()
    {
        var test =
            @"
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class Client
{
    public async Task RunAsync(Socket socket, byte[] payload, CancellationToken ct)
    {
        socket.{|#0:Send|}(payload, SocketFlags.None);
        await Task.Yield();
    }
}";

        var fixedCode =
            @"
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class Client
{
    public async Task RunAsync(Socket socket, byte[] payload, CancellationToken ct)
    {
        await socket.SendAsync(payload, SocketFlags.None, ct);
        await Task.Yield();
    }
}";

        var expected = new DiagnosticResult("CC036", DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("Send");
        await CreateTest(test, fixedCode, expected).RunAsync();
    }

    [Fact]
    public async Task Connect_EndPoint_BindsConnectAsyncEndPointCt()
    {
        var test =
            @"
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class Client
{
    public async Task RunAsync(Socket socket, EndPoint endpoint, CancellationToken ct)
    {
        socket.{|#0:Connect|}(endpoint);
        await Task.Yield();
    }
}";

        var fixedCode =
            @"
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class Client
{
    public async Task RunAsync(Socket socket, EndPoint endpoint, CancellationToken ct)
    {
        await socket.ConnectAsync(endpoint, ct);
        await Task.Yield();
    }
}";

        var expected = new DiagnosticResult("CC036", DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("Connect");
        await CreateTest(test, fixedCode, expected).RunAsync();
    }

    [Fact]
    public async Task Receive_InheritedUnqualifiedCall_InSocketSubclass_Fixes()
    {
        // An unqualified inherited call resolves through the subclass; the rewrite
        // must still bind to the framework's ReceiveAsync.
        var test =
            @"
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class Worker : Socket
{
    public Worker()
        : base(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp) { }

    public async Task Pump(byte[] buffer, CancellationToken ct)
    {
        {|#0:Receive|}(buffer);
        await Task.Yield();
    }
}";

        var fixedCode =
            @"
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class Worker : Socket
{
    public Worker()
        : base(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp) { }

    public async Task Pump(byte[] buffer, CancellationToken ct)
    {
        await ReceiveAsync(buffer, ct);
        await Task.Yield();
    }
}";

        await CreateTest(test, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task Accept_BindsAcceptAsyncCt()
    {
        var test =
            @"
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class Server
{
    public async Task RunAsync(Socket listener, CancellationToken ct)
    {
        listener.{|#0:Accept|}();
        await Task.Yield();
    }
}";

        var fixedCode =
            @"
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class Server
{
    public async Task RunAsync(Socket listener, CancellationToken ct)
    {
        await listener.AcceptAsync(ct);
        await Task.Yield();
    }
}";

        var expected = new DiagnosticResult("CC036", DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("Accept");
        await CreateTest(test, fixedCode, expected).RunAsync();
    }

    [Fact]
    public async Task Connect_HostPort_BindsConnectAsyncHostPortCt()
    {
        var test =
            @"
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class Client
{
    public async Task RunAsync(Socket socket, CancellationToken ct)
    {
        socket.{|#0:Connect|}(""host"", 443);
        await Task.Yield();
    }
}";

        var fixedCode =
            @"
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class Client
{
    public async Task RunAsync(Socket socket, CancellationToken ct)
    {
        await socket.ConnectAsync(""host"", 443, ct);
        await Task.Yield();
    }
}";

        var expected = new DiagnosticResult("CC036", DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("Connect");
        await CreateTest(test, fixedCode, expected).RunAsync();
    }
}
