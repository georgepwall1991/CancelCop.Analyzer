using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace CancelCop.Analyzer.Tests;

/// <summary>
/// CC036: blocking <c>Socket</c> calls in async code. CC028 already covers every <c>Stream</c>, so a
/// <c>NetworkStream</c> is handled there; <c>Socket</c> itself is not, because its async
/// counterparts are not signature-compatible — <c>Receive(byte[])</c> pairs with
/// <c>ReceiveAsync(Memory&lt;byte&gt;, CancellationToken)</c> — and signature compatibility is what
/// makes CC028's rewrites safe.
/// </summary>
public class BlockingSocketIoTests
{
    private static CSharpAnalyzerTest<BlockingSocketIoAnalyzer, DefaultVerifier> Test(
        string source
    ) => new() { TestCode = source, ReferenceAssemblies = ReferenceAssemblies.Net.Net90 };

    private static DiagnosticResult Expected(string member) =>
        new DiagnosticResult("CC036", DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments(member);

    [Fact]
    public async Task Receive_InAsyncMethod_ShouldReportDiagnostic()
    {
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

        var t = Test(test);
        t.ExpectedDiagnostics.Add(Expected("Receive"));
        await t.RunAsync();
    }

    [Fact]
    public async Task Accept_InAsyncMethod_ShouldReportDiagnostic()
    {
        // Accept can block indefinitely: there is no data to wait for, only someone connecting.
        var test =
            @"
using System.Net.Sockets;
using System.Threading.Tasks;

public class Server
{
    public async Task RunAsync(Socket listener)
    {
        var client = listener.{|#0:Accept|}();
        await Task.Yield();
    }
}";

        var t = Test(test);
        t.ExpectedDiagnostics.Add(Expected("Accept"));
        await t.RunAsync();
    }

    [Fact]
    public async Task Send_InAsyncLambda_ShouldReportDiagnostic()
    {
        var test =
            @"
using System;
using System.Net.Sockets;
using System.Threading.Tasks;

public class Server
{
    public void Run(Socket socket, byte[] buffer)
    {
        Func<Task> f = async () =>
        {
            socket.{|#0:Send|}(buffer);
            await Task.Yield();
        };
    }
}";

        var t = Test(test);
        t.ExpectedDiagnostics.Add(Expected("Send"));
        await t.RunAsync();
    }

    [Fact]
    public async Task AsyncCounterpart_ShouldNotReportDiagnostic()
    {
        var test =
            @"
using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class Server
{
    public async Task RunAsync(Socket socket, byte[] buffer, CancellationToken cancellationToken)
    {
        await socket.ReceiveAsync(buffer.AsMemory(), cancellationToken);
    }
}";

        await Test(test).RunAsync();
    }

    [Fact]
    public async Task BlockingCall_InSyncMethod_ShouldNotReportDiagnostic()
    {
        var test =
            @"
using System.Net.Sockets;

public class Server
{
    public void Run(Socket socket, byte[] buffer)
    {
        socket.Receive(buffer);
    }
}";

        await Test(test).RunAsync();
    }

    [Fact]
    public async Task BlockingCall_InSyncLambdaInsideAsyncMethod_ShouldNotReportDiagnostic()
    {
        var test =
            @"
using System;
using System.Net.Sockets;
using System.Threading.Tasks;

public class Server
{
    public async Task RunAsync(Socket socket, byte[] buffer)
    {
        Action a = () => socket.Receive(buffer);
        a();
        await Task.Yield();
    }
}";

        await Test(test).RunAsync();
    }

    [Fact]
    public async Task NonSocketMemberWithTheSameName_ShouldNotReportDiagnostic()
    {
        // Same method names, unrelated type. CC036 is symbol-gated to Socket.
        var test =
            @"
using System.Threading.Tasks;

public class Radio
{
    public void Receive(byte[] buffer) { }
    public void Send(byte[] buffer) { }
}

public class Server
{
    public async Task RunAsync(Radio radio, byte[] buffer)
    {
        radio.Receive(buffer);
        radio.Send(buffer);
        await Task.Yield();
    }
}";

        await Test(test).RunAsync();
    }

    [Fact]
    public async Task NetworkStreamRead_ShouldNotReportDiagnostic()
    {
        // A NetworkStream is a Stream, so CC028 owns it — reporting here too would double up.
        var test =
            @"
using System.Net.Sockets;
using System.Threading.Tasks;

public class Server
{
    public async Task RunAsync(NetworkStream stream, byte[] buffer)
    {
        stream.Read(buffer, 0, buffer.Length);
        await Task.Yield();
    }
}";

        await Test(test).RunAsync();
    }

    [Fact]
    public async Task MemberWithoutAnAsyncCounterpartOnThisFramework_ShouldNotReportDiagnostic()
    {
        // The rule must only claim an alternative the target framework actually has. This stub
        // Socket exposes Receive but no ReceiveAsync, standing in for a framework surface where the
        // counterpart is absent — recommending it there would suggest a call that does not compile.
        var test =
            @"
using System.Threading.Tasks;

namespace System.Net.Sockets
{
    public class Socket
    {
        public int Receive(byte[] buffer) => 0;
    }
}

public class Server
{
    public async Task RunAsync(System.Net.Sockets.Socket socket, byte[] buffer)
    {
        socket.Receive(buffer);
        await Task.Yield();
    }
}";

        await Test(test).RunAsync();
    }
}
