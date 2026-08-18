using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace CancelCop.Analyzer.Tests;

/// <summary>
/// CC044: blocking <c>System.Net.Dns.GetHostEntry</c> in async code.
/// CC043 is GetHostAddresses only. GetHostEntry does a reverse lookup even
/// for a numeric IP, and none of the shipped rules see it.
/// </summary>
public class BlockingDnsGetHostEntryTests
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
        new DiagnosticResult("CC044", DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("GetHostEntry");

    [Fact]
    public async Task GetHostEntry_InAsyncMethod_IsMissedByEveryShippedRule()
    {
        // Empirical gap: GetHostEntry parks a pool thread on a DNS query
        // (including reverse lookup). CC043 is GetHostAddresses only.
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
        Dns.{|#0:GetHostEntry|}(host);
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
    public async Task GetHostEntry_AddressFamilyOverload_ShouldReportDiagnostic()
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
        Dns.{|#0:GetHostEntry|}(host, AddressFamily.InterNetwork);
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
    public async Task GetHostEntry_IpAddressOverload_ShouldReportDiagnostic()
    {
        var test =
            @"
using System.Net;
using System.Threading;
using System.Threading.Tasks;

public class Resolver
{
    public async Task RunAsync(IPAddress address, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Dns.{|#0:GetHostEntry|}(address);
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
    public async Task GetHostEntry_Ipv4Literal_ShouldReportDiagnostic()
    {
        // Unlike GetHostAddresses, GetHostEntry on a numeric IP still does
        // a reverse-DNS query. The CC043 parse exemption must not apply.
        var test =
            @"
using System.Net;
using System.Threading;
using System.Threading.Tasks;

public class Resolver
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Dns.{|#0:GetHostEntry|}(""127.0.0.1"");
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
    public async Task GetHostEntry_UsingStatic_ShouldReportDiagnostic()
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
        {|#0:GetHostEntry|}(host);
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
    public async Task GetHostEntry_InAsyncLambda_ShouldReportDiagnostic()
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
            Dns.{|#0:GetHostEntry|}(host);
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
    public async Task GetHostEntry_InSyncLambdaInsideAsyncMethod_ShouldNotReportDiagnostic()
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
        Action resolve = () => Dns.GetHostEntry(host);
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
    public async Task GetHostEntry_InSyncMethod_ShouldNotReportDiagnostic()
    {
        var test =
            @"
using System.Net;

public class Resolver
{
    public void Run(string host)
    {
        Dns.GetHostEntry(host);
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
    public async Task LookalikeGetHostEntry_ShouldNotReportDiagnostic()
    {
        var test =
            @"
using System.Threading.Tasks;

public class Dns
{
    public static object GetHostEntry(string host) => host;
}

public class Resolver
{
    public async Task RunAsync(string host, System.Threading.CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Dns.GetHostEntry(host);
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
