using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixVerifier<
    CancelCop.Analyzer.SignalRHubAnalyzer,
    CancelCop.Analyzer.HandlerPatternCodeFixProvider,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace CancelCop.Analyzer.Tests;

public class SignalRHubCodeFixTests
{
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
    public async Task HubMethod_AddsCancellationTokenParameter()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;

public class ChatHub : Hub
{
    public async Task {|#0:Broadcast|}(string message)
    {
        await Task.CompletedTask;
    }
}" + HubStub;

        var fixedCode = @"
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

        var expected = VerifyCS.Diagnostic("CC018").WithLocation(0).WithArguments("Broadcast");
        await VerifyCS.VerifyCodeFixAsync(test, expected, fixedCode);
    }

    [Fact]
    public async Task FixAll_TwoHubMethods_BothGetTokenParameter()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;

public class ChatHub : Hub
{
    public async Task {|#0:Broadcast|}(string message)
    {
        await Task.CompletedTask;
    }

    public async Task {|#1:Whisper|}(string message)
    {
        await Task.CompletedTask;
    }
}" + HubStub;

        var fixedCode = @"
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;

public class ChatHub : Hub
{
    public async Task Broadcast(string message, CancellationToken cancellationToken)
    {
        await Task.CompletedTask;
    }

    public async Task Whisper(string message, CancellationToken cancellationToken)
    {
        await Task.CompletedTask;
    }
}" + HubStub;

        await VerifyCS.VerifyCodeFixAsync(
            test,
            new[]
            {
                VerifyCS.Diagnostic("CC018").WithLocation(0).WithArguments("Broadcast"),
                VerifyCS.Diagnostic("CC018").WithLocation(1).WithArguments("Whisper"),
            },
            fixedCode);
    }
}
