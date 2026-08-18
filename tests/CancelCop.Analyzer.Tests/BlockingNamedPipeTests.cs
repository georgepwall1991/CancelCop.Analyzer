using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace CancelCop.Analyzer.Tests;

/// <summary>
/// CC041: blocking <c>NamedPipeServerStream.WaitForConnection</c> in async code.
/// CC028 is File/Stream Read/Write/CopyTo/Flush; CC036–CC040 are Socket/Tcp/Udp/HttpListener.
/// Named-pipe accept is a sixth type, and none of the shipped rules see it.
/// </summary>
public class BlockingNamedPipeTests
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
        new DiagnosticResult("CC041", DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("WaitForConnection");

    [Fact]
    public async Task WaitForConnection_InAsyncMethod_IsMissedByEveryShippedRule()
    {
        // Empirical gap: WaitForConnection parks a pool thread until a client connects.
        // CC028 maps Stream Read/Write/CopyTo/Flush only; WaitForConnection is not
        // a stream primitive. CC002 requires a token overload of the invoked method.
        var test =
            @"
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

public class Server
{
    public async Task RunAsync(NamedPipeServerStream server, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        server.{|#0:WaitForConnection|}();
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
    public async Task WaitForConnection_InAsyncLambda_ShouldReportDiagnostic()
    {
        var test =
            @"
using System;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

public class Server
{
    public void Run(NamedPipeServerStream server, CancellationToken cancellationToken)
    {
        Func<Task> work = async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            server.{|#0:WaitForConnection|}();
            await Task.Yield();
        };
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
    public async Task WaitForConnection_InSyncLambdaInsideAsyncMethod_ShouldNotReportDiagnostic()
    {
        var test =
            @"
using System;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

public class Server
{
    public async Task RunAsync(NamedPipeServerStream server, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Action accept = () => server.WaitForConnection();
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
    public async Task WaitForConnection_NullConditional_ShouldReportDiagnostic()
    {
        var test =
            @"
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

public class Server
{
    public async Task RunAsync(NamedPipeServerStream server, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        server?.{|#0:WaitForConnection|}();
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
    public async Task WaitForConnection_InSyncMethod_ShouldNotReportDiagnostic()
    {
        var test =
            @"
using System.IO.Pipes;

public class Server
{
    public void Run(NamedPipeServerStream server)
    {
        server.WaitForConnection();
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
    public async Task LookalikeWaitForConnection_ShouldNotReportDiagnostic()
    {
        var test =
            @"
using System.Threading.Tasks;

public class NamedPipeServerStream
{
    public void WaitForConnection() { }
}

public class Server
{
    public async Task RunAsync(NamedPipeServerStream server, System.Threading.CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        server.WaitForConnection();
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
