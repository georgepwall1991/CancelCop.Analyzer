using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace CancelCop.Analyzer.Tests;

/// <summary>
/// CC042: blocking <c>NamedPipeClientStream.Connect</c> in async code.
/// CC041 is the server accept wait. The client connect is a sibling type,
/// and none of the shipped rules see it.
/// </summary>
public class BlockingNamedPipeClientTests
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
        new DiagnosticResult("CC042", DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("Connect");

    [Fact]
    public async Task Connect_InAsyncMethod_IsMissedByEveryShippedRule()
    {
        // Empirical gap: Connect parks a pool thread until the server accepts.
        // CC041 is NamedPipeServerStream.WaitForConnection only.
        var test =
            @"
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

public class Client
{
    public async Task RunAsync(NamedPipeClientStream client, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        client.{|#0:Connect|}();
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
    public async Task Connect_TimeoutOverload_ShouldReportDiagnostic()
    {
        // Connect(int) still parks the thread until the server accepts or
        // the timeout elapses. ConnectAsync(int, token) is the counterpart.
        var test =
            @"
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

public class Client
{
    public async Task RunAsync(NamedPipeClientStream client, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        client.{|#0:Connect|}(5000);
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
    public async Task Connect_TimeSpanOverload_ShouldReportDiagnostic()
    {
        var test =
            @"
using System;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

public class Client
{
    public async Task RunAsync(NamedPipeClientStream client, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        client.{|#0:Connect|}(TimeSpan.FromSeconds(5));
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
    public async Task Connect_InAsyncLambda_ShouldReportDiagnostic()
    {
        var test =
            @"
using System;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

public class Client
{
    public void Run(NamedPipeClientStream client, CancellationToken cancellationToken)
    {
        Func<Task> work = async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            client.{|#0:Connect|}();
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
    public async Task Connect_InSyncLambdaInsideAsyncMethod_ShouldNotReportDiagnostic()
    {
        var test =
            @"
using System;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

public class Client
{
    public async Task RunAsync(NamedPipeClientStream client, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Action connect = () => client.Connect();
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
    public async Task Connect_NullConditional_ShouldReportDiagnostic()
    {
        var test =
            @"
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

public class Client
{
    public async Task RunAsync(NamedPipeClientStream client, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        client?.{|#0:Connect|}();
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
    public async Task Connect_InSyncMethod_ShouldNotReportDiagnostic()
    {
        var test =
            @"
using System.IO.Pipes;

public class Client
{
    public void Run(NamedPipeClientStream client)
    {
        client.Connect();
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
    public async Task LookalikeConnect_ShouldNotReportDiagnostic()
    {
        var test =
            @"
using System.Threading.Tasks;

public class NamedPipeClientStream
{
    public void Connect() { }
}

public class Client
{
    public async Task RunAsync(NamedPipeClientStream client, System.Threading.CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        client.Connect();
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
