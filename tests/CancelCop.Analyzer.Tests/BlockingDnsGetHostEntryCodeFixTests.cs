using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace CancelCop.Analyzer.Tests;

/// <summary>
/// CC044 fixer: rewritten code is compiled by the harness.
/// <c>Dns.GetHostEntry</c> → <c>await GetHostEntryAsync</c>.
/// <c>Dns</c> is a static type. The <c>IPAddress</c> TAP is tokenless.
/// The <c>AddressFamily</c> TAP has an optional token.
/// </summary>
public class BlockingDnsGetHostEntryCodeFixTests
{
    private static CSharpCodeFixTest<
        BlockingDnsGetHostEntryAnalyzer,
        BlockingDnsGetHostEntryCodeFixProvider,
        DefaultVerifier
    > CreateTest(string testCode, string fixedCode, params DiagnosticResult[] expected)
    {
        var test = new CSharpCodeFixTest<
            BlockingDnsGetHostEntryAnalyzer,
            BlockingDnsGetHostEntryCodeFixProvider,
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
        new DiagnosticResult("CC044", DiagnosticSeverity.Warning)
            .WithLocation(location)
            .WithArguments("GetHostEntry");

    [Fact]
    public async Task GetHostEntry_String_WithTokenInScope_FlowsTheToken()
    {
        var test =
            @"
using System.Net;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(string host, CancellationToken cancellationToken)
    {
        Dns.{|#0:GetHostEntry|}(host);
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
    public async Task RunAsync(string host, CancellationToken cancellationToken)
    {
        await Dns.GetHostEntryAsync(host, cancellationToken);
        await Task.Yield();
    }
}";

        await CreateTest(test, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task GetHostEntry_String_WithoutTokenInScope_StillCompiles()
    {
        var test =
            @"
using System.Net;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(string host)
    {
        Dns.{|#0:GetHostEntry|}(host);
        await Task.Yield();
    }
}";

        var fixedCode =
            @"
using System.Net;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(string host)
    {
        await Dns.GetHostEntryAsync(host);
        await Task.Yield();
    }
}";

        await CreateTest(test, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task GetHostEntry_AddressFamily_WithToken_KeepsFamilyAndFlowsTheToken()
    {
        var test =
            @"
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(string host, CancellationToken cancellationToken)
    {
        Dns.{|#0:GetHostEntry|}(host, AddressFamily.InterNetwork);
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
    public async Task RunAsync(string host, CancellationToken cancellationToken)
    {
        await Dns.GetHostEntryAsync(host, AddressFamily.InterNetwork, cancellationToken);
        await Task.Yield();
    }
}";

        await CreateTest(test, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task GetHostEntry_AddressFamily_WithoutToken_UsesOptionalTokenDefault()
    {
        var test =
            @"
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(string host)
    {
        Dns.{|#0:GetHostEntry|}(host, AddressFamily.InterNetwork);
        await Task.Yield();
    }
}";

        var fixedCode =
            @"
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(string host)
    {
        await Dns.GetHostEntryAsync(host, AddressFamily.InterNetwork);
        await Task.Yield();
    }
}";

        await CreateTest(test, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task GetHostEntry_IpAddress_WithTokenInScope_DoesNotInventAToken()
    {
        var test =
            @"
using System.Net;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(IPAddress address, CancellationToken cancellationToken)
    {
        Dns.{|#0:GetHostEntry|}(address);
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
    public async Task RunAsync(IPAddress address, CancellationToken cancellationToken)
    {
        await Dns.GetHostEntryAsync(address);
        await Task.Yield();
    }
}";

        await CreateTest(test, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task GetHostEntry_IpAddress_WithoutToken_StillCompiles()
    {
        var test =
            @"
using System.Net;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(IPAddress address)
    {
        Dns.{|#0:GetHostEntry|}(address);
        await Task.Yield();
    }
}";

        var fixedCode =
            @"
using System.Net;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(IPAddress address)
    {
        await Dns.GetHostEntryAsync(address);
        await Task.Yield();
    }
}";

        await CreateTest(test, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task GetHostEntry_UsingStatic_WithToken_FlowsTheToken()
    {
        var test =
            @"
using System.Threading;
using System.Threading.Tasks;
using static System.Net.Dns;

public class TestClass
{
    public async Task RunAsync(string host, CancellationToken cancellationToken)
    {
        {|#0:GetHostEntry|}(host);
        await Task.Yield();
    }
}";

        var fixedCode =
            @"
using System.Threading;
using System.Threading.Tasks;
using static System.Net.Dns;

public class TestClass
{
    public async Task RunAsync(string host, CancellationToken cancellationToken)
    {
        await GetHostEntryAsync(host, cancellationToken);
        await Task.Yield();
    }
}";

        await CreateTest(test, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task GetHostEntry_NamedHost_NamesTheTokenToo()
    {
        var test =
            @"
using System.Net;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(string host, CancellationToken cancellationToken)
    {
        Dns.{|#0:GetHostEntry|}(hostNameOrAddress: host);
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
    public async Task RunAsync(string host, CancellationToken cancellationToken)
    {
        await Dns.GetHostEntryAsync(hostNameOrAddress: host, cancellationToken: cancellationToken);
        await Task.Yield();
    }
}";

        await CreateTest(test, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task GetHostEntry_UsedAsReceiver_ParenthesizesAwait()
    {
        var test =
            @"
using System.Net;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task<string> RunAsync(string host, CancellationToken cancellationToken)
    {
        return Dns.{|#0:GetHostEntry|}(host).HostName;
    }
}";

        var fixedCode =
            @"
using System.Net;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task<string> RunAsync(string host, CancellationToken cancellationToken)
    {
        return (await Dns.GetHostEntryAsync(host, cancellationToken)).HostName;
    }
}";

        await CreateTest(test, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task GetHostEntry_InsideLock_ReportsWithoutOfferingAFix()
    {
        var source =
            @"
using System.Net;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    private readonly object _gate = new object();

    public async Task RunAsync(string host, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            Dns.{|#0:GetHostEntry|}(host);
        }

        await Task.Yield();
    }
}";

        await CreateTest(source, source, Expected()).RunAsync();
    }

    [Fact]
    public async Task FixAll_StringAndIpAddress_BothBecomeAwaitAsync()
    {
        var test =
            @"
using System.Net;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(string host, IPAddress address, CancellationToken cancellationToken)
    {
        Dns.{|#0:GetHostEntry|}(host);
        Dns.{|#1:GetHostEntry|}(address);
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
    public async Task RunAsync(string host, IPAddress address, CancellationToken cancellationToken)
    {
        await Dns.GetHostEntryAsync(host, cancellationToken);
        await Dns.GetHostEntryAsync(address);
        await Task.Yield();
    }
}";

        await CreateTest(test, fixedCode, Expected(0), Expected(1)).RunAsync();
    }

    [Fact]
    public async Task UsingStatic_ShadowingTokenHelper_ReportsWithoutOfferingAFix()
    {
        var source =
            @"
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using static System.Net.Dns;

public class TestClass
{
    public static Task<IPHostEntry> GetHostEntryAsync(string host, CancellationToken cancellationToken) =>
        Task.FromResult(new IPHostEntry());

    public async Task RunAsync(string host, CancellationToken cancellationToken)
    {
        {|#0:GetHostEntry|}(host);
        await Task.Yield();
    }
}";

        await CreateTest(source, source, Expected()).RunAsync();
    }

    [Fact]
    public async Task UsingStatic_ShadowingStringHelper_WithoutToken_ReportsWithoutOfferingAFix()
    {
        var source =
            @"
using System.Net;
using System.Threading.Tasks;
using static System.Net.Dns;

public class TestClass
{
    public static Task<IPHostEntry> GetHostEntryAsync(string host) =>
        Task.FromResult(new IPHostEntry());

    public async Task RunAsync(string host)
    {
        {|#0:GetHostEntry|}(host);
        await Task.Yield();
    }
}";

        await CreateTest(source, source, Expected()).RunAsync();
    }
}
