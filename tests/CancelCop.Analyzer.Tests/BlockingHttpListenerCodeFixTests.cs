using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace CancelCop.Analyzer.Tests;

/// <summary>
/// CC040 fixer: rewritten code is compiled by the harness.
/// <c>GetContext()</c> → <c>await GetContextAsync()</c>.
/// The framework TAP is tokenless; the fixer must not invent a token.
/// </summary>
public class BlockingHttpListenerCodeFixTests
{
    private static CSharpCodeFixTest<
        BlockingHttpListenerAnalyzer,
        BlockingHttpListenerCodeFixProvider,
        DefaultVerifier
    > CreateTest(string testCode, string fixedCode, params DiagnosticResult[] expected)
    {
        var test = new CSharpCodeFixTest<
            BlockingHttpListenerAnalyzer,
            BlockingHttpListenerCodeFixProvider,
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
        new DiagnosticResult("CC040", DiagnosticSeverity.Warning)
            .WithLocation(location)
            .WithArguments("GetContext");

    [Fact]
    public async Task GetContext_WithTokenInScope_DoesNotInventAToken()
    {
        var test =
            @"
using System.Net;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(HttpListener listener, CancellationToken cancellationToken)
    {
        listener.{|#0:GetContext|}();
        await Task.Yield();
    }
}";

        var fixedCode =
            @"
using System.Net;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(HttpListener listener, CancellationToken cancellationToken)
    {
        await listener.GetContextAsync();
        await Task.Yield();
    }
}";

        await CreateTest(test, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task GetContext_WithoutTokenInScope_StillCompiles()
    {
        var test =
            @"
using System.Net;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(HttpListener listener)
    {
        listener.{|#0:GetContext|}();
        await Task.Yield();
    }
}";

        var fixedCode =
            @"
using System.Net;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(HttpListener listener)
    {
        await listener.GetContextAsync();
        await Task.Yield();
    }
}";

        await CreateTest(test, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task GetContext_ValueUse_AwaitsGetContextAsync()
    {
        var test =
            @"
using System.Net;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task<HttpListenerContext> RunAsync(HttpListener listener, CancellationToken cancellationToken)
    {
        return listener.{|#0:GetContext|}();
    }
}";

        var fixedCode =
            @"
using System.Net;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task<HttpListenerContext> RunAsync(HttpListener listener, CancellationToken cancellationToken)
    {
        return await listener.GetContextAsync();
    }
}";

        await CreateTest(test, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task GetContext_UsedAsReceiver_ParenthesizesAwait()
    {
        var test =
            @"
using System.Net;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(HttpListener listener, CancellationToken cancellationToken)
    {
        listener.{|#0:GetContext|}().Response.Close();
        await Task.Yield();
    }
}";

        var fixedCode =
            @"
using System.Net;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(HttpListener listener, CancellationToken cancellationToken)
    {
        (await listener.GetContextAsync()).Response.Close();
        await Task.Yield();
    }
}";

        await CreateTest(test, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task GetContext_NullConditional_ReportsWithoutOfferingAFix()
    {
        var source =
            @"
using System.Net;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(HttpListener? listener, CancellationToken cancellationToken)
    {
        listener?.{|#0:GetContext|}();
        await Task.Yield();
    }
}";

        await CreateTest(source, source, Expected()).RunAsync();
    }

    [Fact]
    public async Task GetContext_InsideLock_ReportsWithoutOfferingAFix()
    {
        var source =
            @"
using System.Net;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    private readonly object _gate = new object();

    public async Task RunAsync(HttpListener listener, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            listener.{|#0:GetContext|}();
        }

        await Task.Yield();
    }
}";

        await CreateTest(source, source, Expected()).RunAsync();
    }

    [Fact]
    public async Task ComposedFieldOutsideGetContextAsync_StillFixes()
    {
        var test =
            @"
using System.Net;
using System.Threading;
using System.Threading.Tasks;

public class Server
{
    private readonly HttpListener _listener;

    public Server(HttpListener listener) => _listener = listener;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        _listener.{|#0:GetContext|}();
        await Task.Yield();
    }
}";

        var fixedCode =
            @"
using System.Net;
using System.Threading;
using System.Threading.Tasks;

public class Server
{
    private readonly HttpListener _listener;

    public Server(HttpListener listener) => _listener = listener;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await _listener.GetContextAsync();
        await Task.Yield();
    }
}";

        await CreateTest(test, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task FixAll_TwoGetContexts_BothBecomeAwaitGetContextAsync()
    {
        var test =
            @"
using System.Net;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(HttpListener a, HttpListener b, CancellationToken cancellationToken)
    {
        a.{|#0:GetContext|}();
        b.{|#1:GetContext|}();
        await Task.Yield();
    }
}";

        var fixedCode =
            @"
using System.Net;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(HttpListener a, HttpListener b, CancellationToken cancellationToken)
    {
        await a.GetContextAsync();
        await b.GetContextAsync();
        await Task.Yield();
    }
}";

        await CreateTest(test, fixedCode, Expected(0), Expected(1)).RunAsync();
    }
}
