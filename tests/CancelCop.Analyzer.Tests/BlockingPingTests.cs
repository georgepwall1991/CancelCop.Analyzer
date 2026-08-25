using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    CancelCop.Analyzer.BlockingPingAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace CancelCop.Analyzer.Tests;

public class BlockingPingAnalyzerTests
{
    [Fact]
    public async Task PingSend_InAsyncMethod_ShouldReportDiagnostic()
    {
        var test = @"
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(Ping ping, CancellationToken ct)
    {
        ping.{|#0:Send|}(""host"");
        await Task.Yield();
    }
}";

        var expected = VerifyCS.Diagnostic("CC050").WithLocation(0).WithArguments("Send");
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task PingSend_WithTimeout_ShouldReportDiagnostic()
    {
        var test = @"
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(Ping ping, CancellationToken ct)
    {
        ping.{|#0:Send|}(""host"", 1000);
        await Task.Yield();
    }
}";

        var expected = VerifyCS.Diagnostic("CC050").WithLocation(0).WithArguments("Send");
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task PingSend_InSyncMethod_ShouldNotReportDiagnostic()
    {
        var test = @"
using System.Net.NetworkInformation;

public class TestClass
{
    public void Run(Ping ping)
    {
        ping.Send(""host"");
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task LookalikeClass_WithOwnMembers_ShouldNotReportDiagnostic()
    {
        var test = @"
using System.Threading.Tasks;

public class FakePing
{
    public int Send(string host) => 0;
    public Task<int> SendPingAsync(string host) => Task.FromResult(0);
}

public static class TestClass
{
    public static async Task RunAsync(FakePing ping)
    {
        ping.Send(""host"");
        await Task.Yield();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task PingSendAsync_EapOverload_ShouldNotReportDiagnostic()
    {
        var test = @"
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(Ping ping)
    {
        ping.SendAsync(""host"", null);
        await Task.Yield();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task PingSend_InsideLock_ShouldReportAwaitUnsafe()
    {
        var test = @"
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    private readonly object gate = new();

    public async Task RunAsync(Ping ping, CancellationToken ct)
    {
        lock (gate)
        {
            ping.{|#0:Send|}(""host"");
        }
        await Task.Yield();
    }
}";

        var expected = VerifyCS.Diagnostic("CC050").WithLocation(0).WithArguments("Send");
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }
}
