using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace CancelCop.Analyzer.Tests;

/// <summary>
/// CC041 fixer: rewritten code is compiled by the harness.
/// <c>WaitForConnection</c> → <c>await WaitForConnectionAsync</c>.
/// <c>NamedPipeServerStream</c> is sealed, so subclass TAP-hider
/// and this-alias tests cannot compile (CS0509).
/// </summary>
public class BlockingNamedPipeCodeFixTests
{
    private static CSharpCodeFixTest<
        BlockingNamedPipeAnalyzer,
        BlockingNamedPipeCodeFixProvider,
        DefaultVerifier
    > CreateTest(string testCode, string fixedCode, params DiagnosticResult[] expected)
    {
        var test = new CSharpCodeFixTest<
            BlockingNamedPipeAnalyzer,
            BlockingNamedPipeCodeFixProvider,
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
        new DiagnosticResult("CC041", DiagnosticSeverity.Warning)
            .WithLocation(location)
            .WithArguments("WaitForConnection");

    [Fact]
    public async Task WaitForConnection_WithTokenInScope_FlowsTheToken()
    {
        var test =
            @"
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(NamedPipeServerStream server, CancellationToken cancellationToken)
    {
        server.{|#0:WaitForConnection|}();
        await Task.Yield();
    }
}";

        var fixedCode =
            @"
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(NamedPipeServerStream server, CancellationToken cancellationToken)
    {
        await server.WaitForConnectionAsync(cancellationToken);
        await Task.Yield();
    }
}";

        await CreateTest(test, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task WaitForConnection_WithoutTokenInScope_StillCompiles()
    {
        var test =
            @"
using System.IO.Pipes;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(NamedPipeServerStream server)
    {
        server.{|#0:WaitForConnection|}();
        await Task.Yield();
    }
}";

        var fixedCode =
            @"
using System.IO.Pipes;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(NamedPipeServerStream server)
    {
        await server.WaitForConnectionAsync();
        await Task.Yield();
    }
}";

        await CreateTest(test, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task WaitForConnection_NullConditional_HoistsToIfNotNullWaitForConnectionAsync()
    {
        var source =
            @"
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(NamedPipeServerStream? server, CancellationToken cancellationToken)
    {
        server?.{|#0:WaitForConnection|}();
        await Task.Yield();
    }
}";

        var fixedCode =
            @"
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(NamedPipeServerStream? server, CancellationToken cancellationToken)
    {
        if (server is not null)
        {
            await server.WaitForConnectionAsync(cancellationToken);
        }
        await Task.Yield();
    }
}";

        await CreateTest(source, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task WaitForConnection_InsideLock_ReportsWithoutOfferingAFix()
    {
        var source =
            @"
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    private readonly object _gate = new object();

    public async Task RunAsync(NamedPipeServerStream server, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            server.{|#0:WaitForConnection|}();
        }

        await Task.Yield();
    }
}";

        await CreateTest(source, source, Expected()).RunAsync();
    }

    [Fact]
    public async Task ComposedFieldOutsideWaitForConnectionAsync_StillFixes()
    {
        var test =
            @"
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

public class Server
{
    private readonly NamedPipeServerStream _server;

    public Server(NamedPipeServerStream server) => _server = server;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        _server.{|#0:WaitForConnection|}();
        await Task.Yield();
    }
}";

        var fixedCode =
            @"
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

public class Server
{
    private readonly NamedPipeServerStream _server;

    public Server(NamedPipeServerStream server) => _server = server;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await _server.WaitForConnectionAsync(cancellationToken);
        await Task.Yield();
    }
}";

        await CreateTest(test, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task FixAll_TwoWaitForConnectionCalls_BothBecomeAwaitAsync()
    {
        var test =
            @"
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(NamedPipeServerStream first, NamedPipeServerStream second, CancellationToken cancellationToken)
    {
        first.{|#0:WaitForConnection|}();
        second.{|#1:WaitForConnection|}();
        await Task.Yield();
    }
}";

        var fixedCode =
            @"
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(NamedPipeServerStream first, NamedPipeServerStream second, CancellationToken cancellationToken)
    {
        await first.WaitForConnectionAsync(cancellationToken);
        await second.WaitForConnectionAsync(cancellationToken);
        await Task.Yield();
    }
}";

        await CreateTest(test, fixedCode, Expected(0), Expected(1)).RunAsync();
    }

    [Fact]
    public async Task ConditionalWaitForConnection_HoistsToIfNotNullWaitForConnectionAsync()
    {
        var test =
            @"
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(NamedPipeServerStream? pipe, CancellationToken cancellationToken)
    {
        await Task.Yield();
        pipe?.{|#0:WaitForConnection|}();
        await Task.Yield();
    }
}";

        var fixedCode =
            @"
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(NamedPipeServerStream? pipe, CancellationToken cancellationToken)
    {
        await Task.Yield();
        if (pipe is not null)
        {
            await pipe.WaitForConnectionAsync(cancellationToken);
        }
        await Task.Yield();
    }
}";

        var expected = new DiagnosticResult("CC041", DiagnosticSeverity.Warning).WithLocation(0);
        await CreateTest(test, fixedCode, expected).RunAsync();
    }

    [Fact]
    public async Task ChainedConditionalWaitForConnection_HoistsWithSplicedPipe()
    {
        var test =
            @"
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

public class Host
{
    public NamedPipeServerStream Pipe { get; } = new NamedPipeServerStream(
        ""p"",
        PipeDirection.Out,
        1,
        PipeTransmissionMode.Byte,
        PipeOptions.Asynchronous
    );
}

public class TestClass
{
    public async Task RunAsync(Host? host, CancellationToken cancellationToken)
    {
        await Task.Yield();
        host?.Pipe.{|#0:WaitForConnection|}();
        await Task.Yield();
    }
}";

        var fixedCode =
            @"
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

public class Host
{
    public NamedPipeServerStream Pipe { get; } = new NamedPipeServerStream(
        ""p"",
        PipeDirection.Out,
        1,
        PipeTransmissionMode.Byte,
        PipeOptions.Asynchronous
    );
}

public class TestClass
{
    public async Task RunAsync(Host? host, CancellationToken cancellationToken)
    {
        await Task.Yield();
        if (host is not null)
        {
            await host.Pipe.WaitForConnectionAsync(cancellationToken);
        }
        await Task.Yield();
    }
}";

        var expected = new DiagnosticResult("CC041", DiagnosticSeverity.Warning).WithLocation(0);
        await CreateTest(test, fixedCode, expected).RunAsync();
    }
}
