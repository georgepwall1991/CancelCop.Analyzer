using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace CancelCop.Analyzer.Tests;

/// <summary>
/// CC038 fixer: rewritten code is compiled by the harness.
/// <c>AcceptTcpClient</c> / <c>AcceptSocket</c> → <c>await Accept*Async</c>.
/// </summary>
public class BlockingTcpListenerCodeFixTests
{
    private static CSharpCodeFixTest<
        BlockingTcpListenerAnalyzer,
        BlockingTcpListenerCodeFixProvider,
        DefaultVerifier
    > CreateTest(string testCode, string fixedCode, params DiagnosticResult[] expected)
    {
        var test = new CSharpCodeFixTest<
            BlockingTcpListenerAnalyzer,
            BlockingTcpListenerCodeFixProvider,
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

    private static DiagnosticResult Expected(string name, int location = 0) =>
        new DiagnosticResult("CC038", DiagnosticSeverity.Warning)
            .WithLocation(location)
            .WithArguments(name);

    [Fact]
    public async Task AcceptTcpClient_WithTokenInScope_FlowsTheToken()
    {
        var test =
            @"
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        listener.{|#0:AcceptTcpClient|}();
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
    public async Task RunAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        await listener.AcceptTcpClientAsync(cancellationToken);
        await Task.Yield();
    }
}";

        await CreateTest(test, fixedCode, Expected("AcceptTcpClient")).RunAsync();
    }

    [Fact]
    public async Task AcceptTcpClient_WithoutTokenInScope_StillCompiles()
    {
        var test =
            @"
using System.Net.Sockets;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(TcpListener listener)
    {
        listener.{|#0:AcceptTcpClient|}();
        await Task.Yield();
    }
}";

        var fixedCode =
            @"
using System.Net.Sockets;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(TcpListener listener)
    {
        await listener.AcceptTcpClientAsync();
        await Task.Yield();
    }
}";

        await CreateTest(test, fixedCode, Expected("AcceptTcpClient")).RunAsync();
    }

    [Fact]
    public async Task AcceptSocket_WithTokenInScope_FlowsTheToken()
    {
        var test =
            @"
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        listener.{|#0:AcceptSocket|}();
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
    public async Task RunAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        await listener.AcceptSocketAsync(cancellationToken);
        await Task.Yield();
    }
}";

        await CreateTest(test, fixedCode, Expected("AcceptSocket")).RunAsync();
    }

    [Fact]
    public async Task AcceptSocket_WithoutTokenInScope_StillCompiles()
    {
        var test =
            @"
using System.Net.Sockets;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(TcpListener listener)
    {
        listener.{|#0:AcceptSocket|}();
        await Task.Yield();
    }
}";

        var fixedCode =
            @"
using System.Net.Sockets;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(TcpListener listener)
    {
        await listener.AcceptSocketAsync();
        await Task.Yield();
    }
}";

        await CreateTest(test, fixedCode, Expected("AcceptSocket")).RunAsync();
    }

    [Fact]
    public async Task AcceptTcpClient_UsedAsReceiver_ParenthesizesAwait()
    {
        var test =
            @"
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        listener.{|#0:AcceptTcpClient|}().Close();
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
    public async Task RunAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        (await listener.AcceptTcpClientAsync(cancellationToken)).Close();
        await Task.Yield();
    }
}";

        await CreateTest(test, fixedCode, Expected("AcceptTcpClient")).RunAsync();
    }

    [Fact]
    public async Task AcceptTcpClient_SuppressNullableReceiver_ParenthesizesAwait()
    {
        var test =
            @"
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        listener.{|#0:AcceptTcpClient|}()!.Close();
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
    public async Task RunAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        (await listener.AcceptTcpClientAsync(cancellationToken))!.Close();
        await Task.Yield();
    }
}";

        await CreateTest(test, fixedCode, Expected("AcceptTcpClient")).RunAsync();
    }

    [Fact]
    public async Task AcceptTcpClient_NullConditional_ReportsWithoutOfferingAFix()
    {
        var source =
            @"
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(TcpListener? listener, CancellationToken cancellationToken)
    {
        listener?.{|#0:AcceptTcpClient|}();
        await Task.Yield();
    }
}";

        await CreateTest(source, source, Expected("AcceptTcpClient")).RunAsync();
    }

    [Fact]
    public async Task AcceptTcpClient_InsideLock_ReportsWithoutOfferingAFix()
    {
        var source =
            @"
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    private readonly object _gate = new object();

    public async Task RunAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            listener.{|#0:AcceptTcpClient|}();
        }

        await Task.Yield();
    }
}";

        await CreateTest(source, source, Expected("AcceptTcpClient")).RunAsync();
    }

    [Fact]
    public async Task CallFromAcceptTcpClientAsync_ReportsWithoutOfferingAFix()
    {
        var source =
            @"
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class ProviderListener : TcpListener
{
    public ProviderListener() : base(IPAddress.Any, 0) { }

    public new async Task<TcpClient> AcceptTcpClientAsync(CancellationToken cancellationToken)
    {
        return {|#0:AcceptTcpClient|}();
    }
}";

        await CreateTest(source, source, Expected("AcceptTcpClient")).RunAsync();
    }

    [Fact]
    public async Task CastOfThisInsideAcceptTcpClientAsync_ReportsWithoutOfferingAFix()
    {
        var source =
            @"
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class ProviderListener : TcpListener
{
    public ProviderListener() : base(IPAddress.Any, 0) { }

    public new async Task<TcpClient> AcceptTcpClientAsync(CancellationToken cancellationToken)
    {
        return ((ProviderListener)this).{|#0:AcceptTcpClient|}();
    }
}";

        await CreateTest(source, source, Expected("AcceptTcpClient")).RunAsync();
    }

    [Fact]
    public async Task AsThisInsideAcceptTcpClientAsync_ReportsWithoutOfferingAFix()
    {
        var source =
            @"
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class ProviderListener : TcpListener
{
    public ProviderListener() : base(IPAddress.Any, 0) { }

    public new async Task<TcpClient> AcceptTcpClientAsync(CancellationToken cancellationToken)
    {
        return (this as TcpListener).{|#0:AcceptTcpClient|}();
    }
}";

        await CreateTest(source, source, Expected("AcceptTcpClient")).RunAsync();
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

public class ProviderListener : TcpListener
{
    public ProviderListener() : base(IPAddress.Any, 0) { }

    private TcpListener Self
    {
        get { return this; }
    }

    public new async Task<TcpClient> AcceptTcpClientAsync(CancellationToken cancellationToken)
    {
        return Self.{|#0:AcceptTcpClient|}();
    }
}";

        await CreateTest(source, source, Expected("AcceptTcpClient")).RunAsync();
    }

    [Fact]
    public async Task ComposedFieldInsideAcceptTcpClientAsync_ReportsWithoutOfferingAFix()
    {
        var source =
            @"
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class ProviderListener : TcpListener
{
    private TcpListener _other = null!;

    public ProviderListener() : base(IPAddress.Any, 0) { }

    public new async Task<TcpClient> AcceptTcpClientAsync(CancellationToken cancellationToken)
    {
        return _other.{|#0:AcceptTcpClient|}();
    }
}";

        await CreateTest(source, source, Expected("AcceptTcpClient")).RunAsync();
    }

    [Fact]
    public async Task ComposedFieldOutsideAcceptAsync_StillFixes()
    {
        var test =
            @"
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class Server
{
    private readonly TcpListener _listener;

    public Server(TcpListener listener) => _listener = listener;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        _listener.{|#0:AcceptTcpClient|}();
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
    private readonly TcpListener _listener;

    public Server(TcpListener listener) => _listener = listener;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await _listener.AcceptTcpClientAsync(cancellationToken);
        await Task.Yield();
    }
}";

        await CreateTest(test, fixedCode, Expected("AcceptTcpClient")).RunAsync();
    }

    [Fact]
    public async Task SubclassHidingAcceptAsync_ShouldNotReport()
    {
        var test =
            @"
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class HiddenListener : TcpListener
{
    public HiddenListener() : base(IPAddress.Any, 0) { }

    public new int AcceptTcpClientAsync() => 0;
    public new int AcceptTcpClientAsync(CancellationToken cancellationToken) => 0;
}

public class TestClass
{
    public async Task RunAsync(HiddenListener listener, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        listener.AcceptTcpClient();
        await Task.Yield();
    }
}";

        var t = new CSharpAnalyzerTest<BlockingTcpListenerAnalyzer, DefaultVerifier>
        {
            TestCode = test,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        await t.RunAsync();
    }

    [Fact]
    public async Task OptionalIntTapHider_DoesNotStealUsableAcceptAsync()
    {
        var test =
            @"
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class LeftoverListener : TcpListener
{
    public LeftoverListener() : base(IPAddress.Any, 0) { }

    public new Task<TcpClient> AcceptTcpClientAsync(int leftover = 0) =>
        Task.FromResult<TcpClient>(null!);
}

public class TestClass
{
    public async Task RunAsync(LeftoverListener listener, CancellationToken cancellationToken)
    {
        listener.{|#0:AcceptTcpClient|}();
        await Task.Yield();
    }
}";

        var fixedCode =
            @"
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class LeftoverListener : TcpListener
{
    public LeftoverListener() : base(IPAddress.Any, 0) { }

    public new Task<TcpClient> AcceptTcpClientAsync(int leftover = 0) =>
        Task.FromResult<TcpClient>(null!);
}

public class TestClass
{
    public async Task RunAsync(LeftoverListener listener, CancellationToken cancellationToken)
    {
        await listener.AcceptTcpClientAsync(cancellationToken);
        await Task.Yield();
    }
}";

        await CreateTest(test, fixedCode, Expected("AcceptTcpClient")).RunAsync();
    }

    [Fact]
    public async Task FixAll_AcceptTcpClientAndAcceptSocket_BothBecomeAwaitAsync()
    {
        var test =
            @"
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        listener.{|#0:AcceptTcpClient|}();
        listener.{|#1:AcceptSocket|}();
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
    public async Task RunAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        await listener.AcceptTcpClientAsync(cancellationToken);
        await listener.AcceptSocketAsync(cancellationToken);
        await Task.Yield();
    }
}";

        await CreateTest(
                test,
                fixedCode,
                Expected("AcceptTcpClient", 0),
                Expected("AcceptSocket", 1)
            )
            .RunAsync();
    }
}
