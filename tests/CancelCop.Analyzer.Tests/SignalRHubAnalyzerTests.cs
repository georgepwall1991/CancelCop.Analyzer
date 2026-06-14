using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    CancelCop.Analyzer.SignalRHubAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace CancelCop.Analyzer.Tests;

public class SignalRHubAnalyzerTests
{
    // A faithful stand-in for Microsoft.AspNetCore.SignalR.Hub (the analyzer gates on the base
    // type's name + namespace), so the tests need no SignalR package.
    private const string HubStub = @"
namespace Microsoft.AspNetCore.SignalR
{
    public abstract class Hub : System.IDisposable
    {
        public virtual System.Threading.Tasks.Task OnConnectedAsync() => System.Threading.Tasks.Task.CompletedTask;
        public void Dispose() { }
    }
}";

    [Fact]
    public async Task HubMethod_ReturningTaskOfT_WithoutToken_ShouldReportDiagnostic()
    {
        // A Task<T>-returning hub method is async-shaped just like a Task-returning one, so a missing
        // token is flagged.
        var test = @"
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;

public class ChatHub : Hub
{
    public async Task<string> {|#0:Echo|}(string message)
    {
        await Task.CompletedTask;
        return message;
    }
}" + HubStub;

        var expected = VerifyCS.Diagnostic("CC018").WithLocation(0).WithArguments("Echo");
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task HubMethod_WithoutToken_ShouldReportDiagnostic()
    {
        var test = @"
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;

public class ChatHub : Hub
{
    public async Task {|#0:Broadcast|}(string message)
    {
        await Task.CompletedTask;
    }
}" + HubStub;

        var expected = VerifyCS.Diagnostic("CC018").WithLocation(0).WithArguments("Broadcast");
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task HubMethod_WithToken_ShouldNotReportDiagnostic()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;

public class ChatHub : Hub
{
    public async Task Broadcast(string message, CancellationToken cancellationToken)
    {
        await Task.CompletedTask;
    }
}" + HubStub;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task OnConnectedAsyncOverride_ShouldNotReportDiagnostic()
    {
        var test = @"
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;

public class ChatHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        await Task.CompletedTask;
    }
}" + HubStub;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task NonHubClass_ShouldNotReportDiagnostic()
    {
        var test = @"
using System.Threading.Tasks;

public class NotAHub
{
    public async Task Broadcast(string message)
    {
        await Task.CompletedTask;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task StaticHubMethod_ShouldNotReportDiagnostic()
    {
        var test = @"
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;

public class ChatHub : Hub
{
    public static async Task Broadcast(string message)
    {
        await Task.CompletedTask;
    }
}" + HubStub;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task PrivateHubMethod_ShouldNotReportDiagnostic()
    {
        var test = @"
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;

public class ChatHub : Hub
{
    private async Task Helper()
    {
        await Task.CompletedTask;
    }
}" + HubStub;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
