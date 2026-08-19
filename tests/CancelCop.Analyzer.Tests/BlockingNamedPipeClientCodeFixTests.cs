using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace CancelCop.Analyzer.Tests;

/// <summary>
/// CC042 fixer: rewritten code is compiled by the harness.
/// <c>Connect</c> / <c>Connect(int)</c> / <c>Connect(TimeSpan)</c> →
/// <c>await ConnectAsync</c>. <c>NamedPipeClientStream</c> is sealed, so
/// subclass TAP-hider and this-alias tests cannot compile (CS0509).
/// There is no tokenless <c>ConnectAsync(TimeSpan)</c>.
/// </summary>
public class BlockingNamedPipeClientCodeFixTests
{
    private static CSharpCodeFixTest<
        BlockingNamedPipeClientAnalyzer,
        BlockingNamedPipeClientCodeFixProvider,
        DefaultVerifier
    > CreateTest(string testCode, string fixedCode, params DiagnosticResult[] expected)
    {
        var test = new CSharpCodeFixTest<
            BlockingNamedPipeClientAnalyzer,
            BlockingNamedPipeClientCodeFixProvider,
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
        new DiagnosticResult("CC042", DiagnosticSeverity.Warning)
            .WithLocation(location)
            .WithArguments("Connect");

    [Fact]
    public async Task Connect_WithTokenInScope_FlowsTheToken()
    {
        var test =
            @"
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(NamedPipeClientStream client, CancellationToken cancellationToken)
    {
        client.{|#0:Connect|}();
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
    public async Task RunAsync(NamedPipeClientStream client, CancellationToken cancellationToken)
    {
        await client.ConnectAsync(cancellationToken);
        await Task.Yield();
    }
}";

        await CreateTest(test, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task Connect_WithoutTokenInScope_StillCompiles()
    {
        var test =
            @"
using System.IO.Pipes;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(NamedPipeClientStream client)
    {
        client.{|#0:Connect|}();
        await Task.Yield();
    }
}";

        var fixedCode =
            @"
using System.IO.Pipes;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(NamedPipeClientStream client)
    {
        await client.ConnectAsync();
        await Task.Yield();
    }
}";

        await CreateTest(test, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task ConnectIntTimeout_WithTokenInScope_KeepsTimeoutAndFlowsTheToken()
    {
        var test =
            @"
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(NamedPipeClientStream client, CancellationToken cancellationToken)
    {
        client.{|#0:Connect|}(5000);
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
    public async Task RunAsync(NamedPipeClientStream client, CancellationToken cancellationToken)
    {
        await client.ConnectAsync(5000, cancellationToken);
        await Task.Yield();
    }
}";

        await CreateTest(test, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task ConnectIntTimeout_WithoutTokenInScope_KeepsTimeout()
    {
        var test =
            @"
using System.IO.Pipes;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(NamedPipeClientStream client)
    {
        client.{|#0:Connect|}(5000);
        await Task.Yield();
    }
}";

        var fixedCode =
            @"
using System.IO.Pipes;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(NamedPipeClientStream client)
    {
        await client.ConnectAsync(5000);
        await Task.Yield();
    }
}";

        await CreateTest(test, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task ConnectTimeSpan_WithTokenInScope_KeepsTimeoutAndFlowsTheToken()
    {
        var test =
            @"
using System;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(NamedPipeClientStream client, CancellationToken cancellationToken)
    {
        client.{|#0:Connect|}(TimeSpan.FromSeconds(5));
        await Task.Yield();
    }
}";

        var fixedCode =
            @"
using System;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(NamedPipeClientStream client, CancellationToken cancellationToken)
    {
        await client.ConnectAsync(TimeSpan.FromSeconds(5), cancellationToken);
        await Task.Yield();
    }
}";

        await CreateTest(test, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task ConnectTimeSpan_WithoutTokenInScope_ReportsWithoutOfferingAFix()
    {
        var source =
            @"
using System;
using System.IO.Pipes;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(NamedPipeClientStream client)
    {
        client.{|#0:Connect|}(TimeSpan.FromSeconds(5));
        await Task.Yield();
    }
}";

        await CreateTest(source, source, Expected()).RunAsync();
    }

    [Fact]
    public async Task ConnectIntTimeout_NamedTimeout_NamesTheTokenToo()
    {
        var test =
            @"
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(NamedPipeClientStream client, CancellationToken cancellationToken)
    {
        client.{|#0:Connect|}(timeout: 5000);
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
    public async Task RunAsync(NamedPipeClientStream client, CancellationToken cancellationToken)
    {
        await client.ConnectAsync(timeout: 5000, cancellationToken: cancellationToken);
        await Task.Yield();
    }
}";

        await CreateTest(test, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task Connect_NullConditional_ReportsWithoutOfferingAFix()
    {
        var source =
            @"
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(NamedPipeClientStream? client, CancellationToken cancellationToken)
    {
        client?.{|#0:Connect|}();
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
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    private readonly object _gate = new object();

    public async Task RunAsync(NamedPipeClientStream client, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            client.{|#0:Connect|}();
        }

        await Task.Yield();
    }
}";

        await CreateTest(source, source, Expected()).RunAsync();
    }

    [Fact]
    public async Task ComposedField_StillFixes()
    {
        var test =
            @"
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

public class Client
{
    private readonly NamedPipeClientStream _client;

    public Client(NamedPipeClientStream client) => _client = client;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        _client.{|#0:Connect|}();
        await Task.Yield();
    }
}";

        var fixedCode =
            @"
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

public class Client
{
    private readonly NamedPipeClientStream _client;

    public Client(NamedPipeClientStream client) => _client = client;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await _client.ConnectAsync(cancellationToken);
        await Task.Yield();
    }
}";

        await CreateTest(test, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task FixAll_ParameterlessAndIntTimeout_BothBecomeAwaitAsync()
    {
        var test =
            @"
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(NamedPipeClientStream first, NamedPipeClientStream second, CancellationToken cancellationToken)
    {
        first.{|#0:Connect|}();
        second.{|#1:Connect|}(5000);
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
    public async Task RunAsync(NamedPipeClientStream first, NamedPipeClientStream second, CancellationToken cancellationToken)
    {
        await first.ConnectAsync(cancellationToken);
        await second.ConnectAsync(5000, cancellationToken);
        await Task.Yield();
    }
}";

        await CreateTest(test, fixedCode, Expected(0), Expected(1)).RunAsync();
    }
}
