using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace CancelCop.Analyzer.Tests;

/// <summary>
/// CC043 fixer: rewritten code is compiled by the harness.
/// <c>Dns.GetHostAddresses</c> → <c>await GetHostAddressesAsync</c>.
/// <c>Dns</c> is a static type. The <c>AddressFamily</c> TAP has an
/// optional token, so a tokenless rewrite still compiles.
/// </summary>
public class BlockingDnsCodeFixTests
{
    private static CSharpCodeFixTest<
        BlockingDnsAnalyzer,
        BlockingDnsCodeFixProvider,
        DefaultVerifier
    > CreateTest(string testCode, string fixedCode, params DiagnosticResult[] expected)
    {
        var test = new CSharpCodeFixTest<
            BlockingDnsAnalyzer,
            BlockingDnsCodeFixProvider,
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
        new DiagnosticResult("CC043", DiagnosticSeverity.Warning)
            .WithLocation(location)
            .WithArguments("GetHostAddresses");

    [Fact]
    public async Task GetHostAddresses_WithTokenInScope_FlowsTheToken()
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
        Dns.{|#0:GetHostAddresses|}(host);
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
        await Dns.GetHostAddressesAsync(host, cancellationToken);
        await Task.Yield();
    }
}";

        await CreateTest(test, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task GetHostAddresses_WithoutTokenInScope_StillCompiles()
    {
        var test =
            @"
using System.Net;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(string host)
    {
        Dns.{|#0:GetHostAddresses|}(host);
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
        await Dns.GetHostAddressesAsync(host);
        await Task.Yield();
    }
}";

        await CreateTest(test, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task GetHostAddresses_AddressFamily_WithToken_KeepsFamilyAndFlowsTheToken()
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
        Dns.{|#0:GetHostAddresses|}(host, AddressFamily.InterNetwork);
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
        await Dns.GetHostAddressesAsync(host, AddressFamily.InterNetwork, cancellationToken);
        await Task.Yield();
    }
}";

        await CreateTest(test, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task GetHostAddresses_AddressFamily_WithoutToken_UsesOptionalTokenDefault()
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
        Dns.{|#0:GetHostAddresses|}(host, AddressFamily.InterNetwork);
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
        await Dns.GetHostAddressesAsync(host, AddressFamily.InterNetwork);
        await Task.Yield();
    }
}";

        await CreateTest(test, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task GetHostAddresses_UsingStatic_WithToken_FlowsTheToken()
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
        {|#0:GetHostAddresses|}(host);
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
        await GetHostAddressesAsync(host, cancellationToken);
        await Task.Yield();
    }
}";

        await CreateTest(test, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task GetHostAddresses_NamedHost_NamesTheTokenToo()
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
        Dns.{|#0:GetHostAddresses|}(hostNameOrAddress: host);
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
        await Dns.GetHostAddressesAsync(hostNameOrAddress: host, cancellationToken: cancellationToken);
        await Task.Yield();
    }
}";

        await CreateTest(test, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task GetHostAddresses_UsedAsReceiver_ParenthesizesAwait()
    {
        var test =
            @"
using System.Net;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task<int> RunAsync(string host, CancellationToken cancellationToken)
    {
        return Dns.{|#0:GetHostAddresses|}(host).Length;
    }
}";

        var fixedCode =
            @"
using System.Net;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task<int> RunAsync(string host, CancellationToken cancellationToken)
    {
        return (await Dns.GetHostAddressesAsync(host, cancellationToken)).Length;
    }
}";

        await CreateTest(test, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task GetHostAddresses_InsideLock_ReportsWithoutOfferingAFix()
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
            Dns.{|#0:GetHostAddresses|}(host);
        }

        await Task.Yield();
    }
}";

        await CreateTest(source, source, Expected()).RunAsync();
    }

    [Fact]
    public async Task FixAll_StringAndAddressFamily_BothBecomeAwaitAsync()
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
        Dns.{|#0:GetHostAddresses|}(host);
        Dns.{|#1:GetHostAddresses|}(host, AddressFamily.InterNetwork);
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
        await Dns.GetHostAddressesAsync(host, cancellationToken);
        await Dns.GetHostAddressesAsync(host, AddressFamily.InterNetwork, cancellationToken);
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
    public static Task<IPAddress[]> GetHostAddressesAsync(string host, CancellationToken cancellationToken) =>
        Task.FromResult(System.Array.Empty<IPAddress>());

    public async Task RunAsync(string host, CancellationToken cancellationToken)
    {
        {|#0:GetHostAddresses|}(host);
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
    public static Task<IPAddress[]> GetHostAddressesAsync(string host) =>
        Task.FromResult(System.Array.Empty<IPAddress>());

    public async Task RunAsync(string host)
    {
        {|#0:GetHostAddresses|}(host);
        await Task.Yield();
    }
}";

        await CreateTest(source, source, Expected()).RunAsync();
    }
}
