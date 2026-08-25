using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    CancelCop.Analyzer.BlockingSslStreamAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace CancelCop.Analyzer.Tests;

public class BlockingSslStreamAnalyzerTests
{
    [Fact]
    public async Task SslStreamAuthenticateAsClient_InAsyncMethod_ShouldReportDiagnostic()
    {
        var test = @"
using System.Net.Security;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(SslStream stream, CancellationToken ct)
    {
        stream.{|#0:AuthenticateAsClient|}(""host"");
        await Task.Yield();
    }
}";

        var expected = VerifyCS.Diagnostic("CC051")
            .WithLocation(0)
            .WithArguments("AuthenticateAsClient");
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task SslStreamAuthenticateAsClient_WithCertificateCollection_ShouldReportDiagnostic()
    {
        // The ref pack does not mark trailing parameters optional, so every
        // argument is spelled out.
        var test = @"
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(SslStream stream, CancellationToken ct)
    {
        stream.{|#0:AuthenticateAsClient|}(""host"", null, true);
        await Task.Yield();
    }
}";

        var expected = VerifyCS.Diagnostic("CC051")
            .WithLocation(0)
            .WithArguments("AuthenticateAsClient");
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task SslStreamAuthenticateAsClient_InSyncMethod_ShouldNotReportDiagnostic()
    {
        var test = @"
using System.Net.Security;

public class TestClass
{
    public void Run(SslStream stream)
    {
        stream.AuthenticateAsClient(""host"");
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task LookalikeClass_WithOwnMembers_ShouldNotReportDiagnostic()
    {
        var test = @"
using System.Threading.Tasks;

public class FakeSslStream
{
    public void AuthenticateAsClient(string host) { }
    public Task AuthenticateAsClientAsync(string host) => Task.CompletedTask;
}

public static class TestClass
{
    public static async Task RunAsync(FakeSslStream stream)
    {
        stream.AuthenticateAsClient(""host"");
        await Task.Yield();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task OtherMemberName_ShouldNotReportDiagnostic()
    {
        var test = @"
using System.Net.Security;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(SslStream stream)
    {
        stream.BeginAuthenticateAsClient(""host"", null, null);
        await Task.Yield();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task SslStreamAuthenticateAsClient_InsideLock_ShouldReportAwaitUnsafe()
    {
        var test = @"
using System.Net.Security;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    private readonly object gate = new();

    public async Task RunAsync(SslStream stream, CancellationToken ct)
    {
        lock (gate)
        {
            stream.{|#0:AuthenticateAsClient|}(""host"");
        }
        await Task.Yield();
    }
}";

        var expected = VerifyCS.Diagnostic("CC051")
            .WithLocation(0)
            .WithArguments("AuthenticateAsClient");
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task BareCall_InsideAuthenticateAsClientAsyncOverride_ShouldReportSelfAsync()
    {
        // A bare implicit-this call inside a Task-returning
        // AuthenticateAsClientAsync override on an SslStream-derived type is
        // flagged with the "self-async" NoFix reason: no rewrite is offered.
        var test = @"
using System.IO;
using System.Net.Security;
using System.Threading.Tasks;

public class TestStream : SslStream
{
    public TestStream()
        : base(Stream.Null) { }

    public override async Task AuthenticateAsClientAsync(string targetHost)
    {
        {|#0:AuthenticateAsClient|}(""host"");
        await Task.CompletedTask;
    }
}";

        var expected = VerifyCS.Diagnostic("CC051")
            .WithLocation(0)
            .WithArguments("AuthenticateAsClient");
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task SslStreamAuthenticateAsClient_BareInsideAuthenticateAsClientAsync_ShouldReportDiagnostic()
    {
        var test = @"
using System.IO;
using System.Net.Security;
using System.Threading.Tasks;

public class Client : SslStream
{
    public Client()
        : base(Stream.Null) { }

    public async Task<bool> AuthenticateAsClientAsync(string host)
    {
        {|#0:AuthenticateAsClient|}(host);
        return true;
    }
}";

        var expected = VerifyCS.Diagnostic("CC051")
            .WithLocation(0)
            .WithArguments("AuthenticateAsClient");
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }
}
