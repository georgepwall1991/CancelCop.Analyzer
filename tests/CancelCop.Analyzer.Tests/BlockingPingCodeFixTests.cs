using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace CancelCop.Analyzer.Tests;

/// <summary>
/// CC050 fixer: rewritten code is compiled by the harness.
/// <c>Send</c> → <c>await SendPingAsync</c> (not event-based <c>SendAsync</c>).
/// </summary>
/// <remarks>
/// The only token-taking <c>SendPingAsync</c> overloads are the
/// <see cref="System.TimeSpan"/> arity-4 forms, so a bare
/// <c>Send("host")</c> rewrites tokenless even with a token in scope —
/// appending a token argument would not bind. Token flow is exercised on
/// the TimeSpan shape.
/// </remarks>
public class BlockingPingCodeFixTests
{
    private static CSharpCodeFixTest<
        BlockingPingAnalyzer,
        BlockingPingCodeFixProvider,
        DefaultVerifier
    > CreateTest(string testCode, string fixedCode, params DiagnosticResult[] expected)
    {
        var test = new CSharpCodeFixTest<
            BlockingPingAnalyzer,
            BlockingPingCodeFixProvider,
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
        new DiagnosticResult("CC050", DiagnosticSeverity.Warning)
            .WithLocation(location)
            .WithArguments("Send");

    [Fact]
    public async Task PingSend_WithoutMatchingTokenArity_RewritesTokenless()
    {
        // No `SendPingAsync(string, CancellationToken)` exists; the rewrite stays honest.
        var test =
            @"
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(Ping ping, CancellationToken cancellationToken)
    {
        ping.{|#0:Send|}(""host"");
        await Task.Yield();
    }
}";

        var fixedCode =
            @"
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(Ping ping, CancellationToken cancellationToken)
    {
        await ping.SendPingAsync(""host"");
        await Task.Yield();
    }
}";

        await CreateTest(test, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task PingSend_WithoutTokenInScope_StillCompiles()
    {
        var test =
            @"
using System.Net.NetworkInformation;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(Ping ping)
    {
        ping.{|#0:Send|}(""host"");
        await Task.Yield();
    }
}";

        var fixedCode =
            @"
using System.Net.NetworkInformation;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(Ping ping)
    {
        await ping.SendPingAsync(""host"");
        await Task.Yield();
    }
}";

        await CreateTest(test, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task PingSend_TimeSpanShape_FlowsTheToken()
    {
        var test =
            @"
using System;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(Ping ping, CancellationToken cancellationToken)
    {
        var buffer = new byte[1];
        ping.{|#0:Send|}(""host"", TimeSpan.FromMilliseconds(500), buffer, null);
        await Task.Yield();
    }
}";

        var fixedCode =
            @"
using System;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(Ping ping, CancellationToken cancellationToken)
    {
        var buffer = new byte[1];
        await ping.SendPingAsync(""host"", TimeSpan.FromMilliseconds(500), buffer, null, cancellationToken);
        await Task.Yield();
    }
}";

        await CreateTest(test, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task PingSend_NullConditional_HoistsToIfNotNullSendPingAsync()
    {
        // The whole null-conditional statement hoists; the in-scope token flows into the call.
        var source =
            @"
using System;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(Ping? ping, CancellationToken cancellationToken)
    {
        var buffer = new byte[1];
        ping?.{|#0:Send|}(""host"", TimeSpan.FromMilliseconds(500), buffer, null);
        await Task.Yield();
    }
}";

        var fixedCode =
            @"
using System;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(Ping? ping, CancellationToken cancellationToken)
    {
        var buffer = new byte[1];
        if (ping is not null)
        {
            await ping.SendPingAsync(""host"", TimeSpan.FromMilliseconds(500), buffer, null, cancellationToken);
        }
        await Task.Yield();
    }
}";

        await CreateTest(source, fixedCode, Expected()).RunAsync();
    }
}
