using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace CancelCop.Analyzer.Tests;

/// <summary>
/// CC043: blocking <c>System.Net.Dns.GetHostAddresses</c> in async code.
/// CC002 requires a token overload of the invoked method; GetHostAddresses has
/// none. CC036–CC042 are Socket / Tcp / Udp / HttpListener / named-pipe.
/// DNS is a separate type, and none of the shipped rules see it.
/// </summary>
public class BlockingDnsTests
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
        new DiagnosticResult("CC043", DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("GetHostAddresses");

    [Fact]
    public async Task GetHostAddresses_InAsyncMethod_IsMissedByEveryShippedRule()
    {
        // Empirical gap: GetHostAddresses parks a pool thread on a DNS query.
        // CC002 cannot see it (no token overload of this method). GetHostEntry
        // is a sibling, deferred.
        var test =
            @"
using System.Net;
using System.Threading;
using System.Threading.Tasks;

public class Resolver
{
    public async Task RunAsync(string host, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Dns.{|#0:GetHostAddresses|}(host);
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
    public async Task GetHostAddresses_AddressFamilyOverload_ShouldReportDiagnostic()
    {
        var test =
            @"
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class Resolver
{
    public async Task RunAsync(string host, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Dns.{|#0:GetHostAddresses|}(host, AddressFamily.InterNetwork);
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
    public async Task GetHostAddresses_UsingStatic_ShouldReportDiagnostic()
    {
        var test =
            @"
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using static System.Net.Dns;

public class Resolver
{
    public async Task RunAsync(string host, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        {|#0:GetHostAddresses|}(host);
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
    public async Task GetHostAddresses_InAsyncLambda_ShouldReportDiagnostic()
    {
        var test =
            @"
using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

public class Resolver
{
    public void Run(string host, CancellationToken cancellationToken)
    {
        Func<Task> work = async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            Dns.{|#0:GetHostAddresses|}(host);
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
    public async Task GetHostAddresses_InSyncLambdaInsideAsyncMethod_ShouldNotReportDiagnostic()
    {
        var test =
            @"
using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

public class Resolver
{
    public async Task RunAsync(string host, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Action resolve = () => Dns.GetHostAddresses(host);
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
    public async Task GetHostAddresses_InSyncMethod_ShouldNotReportDiagnostic()
    {
        var test =
            @"
using System.Net;

public class Resolver
{
    public void Run(string host)
    {
        Dns.GetHostAddresses(host);
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
    public async Task LookalikeGetHostAddresses_ShouldNotReportDiagnostic()
    {
        var test =
            @"
using System.Threading.Tasks;

public class Dns
{
    public static object GetHostAddresses(string host) => host;
}

public class Resolver
{
    public async Task RunAsync(string host, System.Threading.CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Dns.GetHostAddresses(host);
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
