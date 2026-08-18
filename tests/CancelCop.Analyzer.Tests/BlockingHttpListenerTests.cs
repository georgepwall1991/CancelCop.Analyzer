using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace CancelCop.Analyzer.Tests;

/// <summary>
/// CC040: blocking <c>HttpListener.GetContext</c> in async code.
/// CC036 is Socket-only; CC037/CC038/CC039 are TcpClient/TcpListener/UdpClient.
/// HttpListener accept is a fifth type, and none of the shipped rules see it.
/// </summary>
public class BlockingHttpListenerTests
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
        new DiagnosticResult("CC040", DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("GetContext");

    [Fact]
    public async Task GetContext_InAsyncMethod_IsMissedByEveryShippedRule()
    {
        // Empirical gap: GetContext parks a pool thread until a request arrives.
        // CC038 is TcpListener; CC002 requires a token overload of the invoked
        // method, and GetContext has none.
        var test =
            @"
using System.Net;
using System.Threading;
using System.Threading.Tasks;

public class Server
{
    public async Task RunAsync(HttpListener listener, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        listener.{|#0:GetContext|}();
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
    public async Task GetContext_InAsyncLambda_ShouldReportDiagnostic()
    {
        var test =
            @"
using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

public class Server
{
    public void Run(HttpListener listener, CancellationToken cancellationToken)
    {
        Func<Task> work = async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            listener.{|#0:GetContext|}();
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
    public async Task GetContext_InSyncLambdaInsideAsyncMethod_ShouldNotReportDiagnostic()
    {
        var test =
            @"
using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

public class Server
{
    public async Task RunAsync(HttpListener listener, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Action accept = () => listener.GetContext();
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
    public async Task GetContext_NullConditional_ShouldReportDiagnostic()
    {
        var test =
            @"
using System.Net;
using System.Threading;
using System.Threading.Tasks;

public class Server
{
    public async Task RunAsync(HttpListener listener, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        listener?.{|#0:GetContext|}();
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
    public async Task GetContext_InSyncMethod_ShouldNotReportDiagnostic()
    {
        var test =
            @"
using System.Net;

public class Server
{
    public void Run(HttpListener listener)
    {
        listener.GetContext();
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
    public async Task LookalikeGetContext_ShouldNotReportDiagnostic()
    {
        var test =
            @"
using System.Threading.Tasks;

public class HttpListener
{
    public object GetContext() => new object();
}

public class Server
{
    public async Task RunAsync(HttpListener listener, System.Threading.CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        listener.GetContext();
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
