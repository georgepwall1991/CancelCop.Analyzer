using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    CancelCop.Analyzer.BlockingWebRequestAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace CancelCop.Analyzer.Tests;

/// <remarks>
/// <c>WebRequest</c> is abstract with only family (protected) constructors,
/// so derived fixtures chain <c>FileWebRequest</c>'s serialization
/// constructor (<c>base(default, default)</c>) — the concrete,
/// derivable shapes in the <c>WebRequest</c> family besides the equally
/// non-parameterless-constructed <see cref="System.Net.HttpWebRequest"/>.
/// </remarks>
public class BlockingWebRequestAnalyzerTests
{
    [Fact]
    public async Task WebRequestGetResponse_InAsyncMethod_ShouldReportDiagnostic()
    {
        var test = @"
using System.Net;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(WebRequest request, CancellationToken ct)
    {
        request.{|#0:GetResponse|}();
        await Task.Yield();
    }
}";

        var expected = VerifyCS.Diagnostic("CC052")
            .WithLocation(0)
            .WithArguments("GetResponse");
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task HttpWebRequestVariable_InAsyncMethod_ShouldReportDiagnostic()
    {
        // HttpWebRequest.GetResponse overrides the virtual framework member;
        // the override walk resolves it back to System.Net.WebRequest.
        var test = @"
using System.Net;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(HttpWebRequest request)
    {
        request.{|#0:GetResponse|}();
        await Task.Yield();
    }
}";

        var expected = VerifyCS.Diagnostic("CC052")
            .WithLocation(0)
            .WithArguments("GetResponse");
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task WebRequestGetResponse_InSyncMethod_ShouldNotReportDiagnostic()
    {
        var test = @"
using System.Net;

public class TestClass
{
    public void Run(WebRequest request)
    {
        request.GetResponse();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task LookalikeClass_WithOwnMembers_ShouldNotReportDiagnostic()
    {
        var test = @"
using System.Threading.Tasks;

public class FakeWebRequest
{
    public void GetResponse() { }
    public Task GetResponseAsync() => Task.CompletedTask;
}

public static class TestClass
{
    public static async Task RunAsync(FakeWebRequest request)
    {
        request.GetResponse();
        await Task.Yield();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task OtherMemberName_ShouldNotReportDiagnostic()
    {
        // The APM BeginGetResponse/EndGetResponse pair is never treated as
        // the blocking member or its counterpart.
        var test = @"
using System;
using System.Net;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(WebRequest request)
    {
        request.BeginGetResponse(null, null);
        await Task.Yield();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task WebRequestGetResponse_InsideLock_ShouldReportAwaitUnsafe()
    {
        var test = @"
using System.Net;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    private readonly object gate = new();

    public async Task RunAsync(WebRequest request, CancellationToken ct)
    {
        lock (gate)
        {
            request.{|#0:GetResponse|}();
        }
        await Task.Yield();
    }
}";

        var expected = VerifyCS.Diagnostic("CC052")
            .WithLocation(0)
            .WithArguments("GetResponse");
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task BareCall_InsideGetResponseAsyncMember_ShouldReportSelfAsync()
    {
        // A bare implicit-this call inside a Task-returning
        // GetResponseAsync member on a WebRequest-derived type is flagged
        // with the "self-async" NoFix reason: no rewrite is offered.
        var test = @"
using System.Net;
using System.Runtime.Serialization;
using System.Threading.Tasks;

public class Client : FileWebRequest
{
    public Client()
        : base(default, default) { }

    public async Task<bool> GetResponseAsync(bool verbose)
    {
        {|#0:GetResponse|}();
        return true;
    }
}";

        var expected = VerifyCS.Diagnostic("CC052")
            .WithLocation(0)
            .WithArguments("GetResponse");
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }
}
