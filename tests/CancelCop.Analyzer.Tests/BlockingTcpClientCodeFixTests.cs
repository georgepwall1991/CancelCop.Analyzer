using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace CancelCop.Analyzer.Tests;

/// <summary>
/// CC037 fixer: rewritten code is compiled by the harness.
/// <c>Connect</c> → <c>await ConnectAsync</c>.
/// </summary>
public class BlockingTcpClientCodeFixTests
{
    private static CSharpCodeFixTest<
        BlockingTcpClientAnalyzer,
        BlockingTcpClientCodeFixProvider,
        DefaultVerifier
    > CreateTest(string testCode, string fixedCode, params DiagnosticResult[] expected)
    {
        var test = new CSharpCodeFixTest<
            BlockingTcpClientAnalyzer,
            BlockingTcpClientCodeFixProvider,
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
        new DiagnosticResult("CC037", DiagnosticSeverity.Warning)
            .WithLocation(location)
            .WithArguments("Connect");

    [Fact]
    public async Task ConnectHostname_WithTokenInScope_FlowsTheToken()
    {
        var test =
            @"
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(TcpClient client, CancellationToken cancellationToken)
    {
        client.{|#0:Connect|}(""localhost"", 80);
        await Task.Yield();
    }
}";

        var fixedCode =
            @"
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(TcpClient client, CancellationToken cancellationToken)
    {
        await client.ConnectAsync(""localhost"", 80, cancellationToken);
        await Task.Yield();
    }
}";

        await CreateTest(test, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task ConnectHostname_WithoutTokenInScope_StillCompiles()
    {
        var test =
            @"
using System.Net.Sockets;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(TcpClient client)
    {
        client.{|#0:Connect|}(""localhost"", 80);
        await Task.Yield();
    }
}";

        var fixedCode =
            @"
using System.Net.Sockets;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(TcpClient client)
    {
        await client.ConnectAsync(""localhost"", 80);
        await Task.Yield();
    }
}";

        await CreateTest(test, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task ConnectEndPoint_WithTokenInScope_FlowsTheToken()
    {
        var test =
            @"
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(TcpClient client, IPEndPoint endpoint, CancellationToken cancellationToken)
    {
        client.{|#0:Connect|}(endpoint);
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
    public async Task RunAsync(TcpClient client, IPEndPoint endpoint, CancellationToken cancellationToken)
    {
        await client.ConnectAsync(endpoint, cancellationToken);
        await Task.Yield();
    }
}";

        await CreateTest(test, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task ConnectEndPoint_NamedArgument_NamesTheToken()
    {
        var test =
            @"
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(TcpClient client, IPEndPoint remoteEP, CancellationToken cancellationToken)
    {
        client.{|#0:Connect|}(remoteEP: remoteEP);
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
    public async Task RunAsync(TcpClient client, IPEndPoint remoteEP, CancellationToken cancellationToken)
    {
        await client.ConnectAsync(remoteEP: remoteEP, cancellationToken: cancellationToken);
        await Task.Yield();
    }
}";

        await CreateTest(test, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task ConnectHostname_NamedHostname_ReportsWithoutOfferingAFix()
    {
        // Connect(string hostname, int port) vs ConnectAsync(string host, int port).
        // Keeping hostname: would be CS1739.
        var source =
            @"
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(TcpClient client, CancellationToken cancellationToken)
    {
        client.{|#0:Connect|}(hostname: ""localhost"", port: 80);
        await Task.Yield();
    }
}";

        await CreateTest(source, source, Expected()).RunAsync();
    }

    [Fact]
    public async Task Connect_NullConditional_ReportsWithoutOfferingAFix()
    {
        var source =
            @"
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(TcpClient? client, CancellationToken cancellationToken)
    {
        client?.{|#0:Connect|}(""localhost"", 80);
        await Task.Yield();
    }
}";

        await CreateTest(source, source, Expected()).RunAsync();
    }

    [Fact]
    public async Task Connect_InsideLock_ReportsWithoutOfferingAFix()
    {
        var source =
            @"
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    private readonly object _gate = new object();

    public async Task RunAsync(TcpClient client, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            client.{|#0:Connect|}(""localhost"", 80);
        }

        await Task.Yield();
    }
}";

        await CreateTest(source, source, Expected()).RunAsync();
    }

    [Fact]
    public async Task PropertyGetterReturningThis_ReportsWithoutOfferingAFix()
    {
        var source =
            @"
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class ProviderClient : TcpClient
{
    private TcpClient Self
    {
        get { return this; }
    }

    public new async Task ConnectAsync(string hostname, int port, CancellationToken cancellationToken)
    {
        Self.{|#0:Connect|}(hostname, port);
    }
}";

        await CreateTest(source, source, Expected()).RunAsync();
    }

    [Fact]
    public async Task ComposedFieldInsideConnectAsync_ReportsWithoutOfferingAFix()
    {
        // A field on this type may alias this from another partial part.
        var source =
            @"
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class ProviderClient : TcpClient
{
    private TcpClient _other = null!;

    public new async Task ConnectAsync(string hostname, int port, CancellationToken cancellationToken)
    {
        _other.{|#0:Connect|}(hostname, port);
    }
}";

        await CreateTest(source, source, Expected()).RunAsync();
    }

    [Fact]
    public async Task SubclassHidingConnectAsync_ShouldNotReport()
    {
        var test =
            @"
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class HiddenClient : TcpClient
{
    public new int ConnectAsync(string hostname, int port) => 0;
    public new int ConnectAsync(string hostname, int port, CancellationToken cancellationToken) => 0;
}

public class TestClass
{
    public async Task RunAsync(HiddenClient client, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        client.Connect(""localhost"", 80);
        await Task.Yield();
    }
}";

        var t = new CSharpAnalyzerTest<BlockingTcpClientAnalyzer, DefaultVerifier>
        {
            TestCode = test,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        await t.RunAsync();
    }

    [Fact]
    public async Task CallFromConnectAsync_ReportsWithoutOfferingAFix()
    {
        var source =
            @"
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class ProviderClient : TcpClient
{
    public new async Task ConnectAsync(string hostname, int port, CancellationToken cancellationToken)
    {
        {|#0:Connect|}(hostname, port);
    }
}";

        await CreateTest(source, source, Expected()).RunAsync();
    }

    [Fact]
    public async Task FixAll_TwoConnects_BothBecomeAwaitConnectAsync()
    {
        var test =
            @"
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(TcpClient a, TcpClient b, CancellationToken cancellationToken)
    {
        a.{|#0:Connect|}(""localhost"", 80);
        b.{|#1:Connect|}(""localhost"", 443);
        await Task.Yield();
    }
}";

        var fixedCode =
            @"
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(TcpClient a, TcpClient b, CancellationToken cancellationToken)
    {
        await a.ConnectAsync(""localhost"", 80, cancellationToken);
        await b.ConnectAsync(""localhost"", 443, cancellationToken);
        await Task.Yield();
    }
}";

        await CreateTest(test, fixedCode, Expected(0), Expected(1)).RunAsync();
    }
}
