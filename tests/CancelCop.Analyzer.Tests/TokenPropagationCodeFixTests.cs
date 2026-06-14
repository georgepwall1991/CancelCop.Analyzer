using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixVerifier<
    CancelCop.Analyzer.TokenPropagationAnalyzer,
    CancelCop.Analyzer.TokenPropagationCodeFixProvider,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace CancelCop.Analyzer.Tests;

public class TokenPropagationCodeFixTests
{
    [Fact]
    public async Task FixAll_TwoDelays_BothGetTokenArgument()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task ProcessAsync(CancellationToken cancellationToken)
    {
        await Task.{|#0:Delay|}(100);
        await Task.{|#1:Delay|}(200);
    }
}";

        var fixedCode = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task ProcessAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(100, cancellationToken);
        await Task.Delay(200, cancellationToken);
    }
}";

        await VerifyCS.VerifyCodeFixAsync(
            test,
            new[]
            {
                VerifyCS.Diagnostic("CC002").WithLocation(0).WithArguments("Delay", "cancellationToken"),
                VerifyCS.Diagnostic("CC002").WithLocation(1).WithArguments("Delay", "cancellationToken"),
            },
            fixedCode);
    }

    [Fact]
    public async Task TaskDelay_WithoutToken_AddsTokenParameter()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task ProcessAsync(CancellationToken cancellationToken)
    {
        await Task.{|#0:Delay|}(100);
    }
}";

        var fixedCode = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task ProcessAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(100, cancellationToken);
    }
}";

        var expected = VerifyCS.Diagnostic("CC002")
            .WithLocation(0)
            .WithArguments("Delay", "cancellationToken");

        await VerifyCS.VerifyCodeFixAsync(test, expected, fixedCode);
    }

    [Fact]
    public async Task TaskRun_WithoutToken_AddsTokenParameter()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task ProcessAsync(CancellationToken cancellationToken)
    {
        await Task.{|#0:Run|}(() => DoWork());
    }

    private void DoWork() { }
}";

        var fixedCode = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task ProcessAsync(CancellationToken cancellationToken)
    {
        await Task.Run(() => DoWork(), cancellationToken);
    }

    private void DoWork() { }
}";

        var expected = VerifyCS.Diagnostic("CC002")
            .WithLocation(0)
            .WithArguments("Run", "cancellationToken");

        await VerifyCS.VerifyCodeFixAsync(test, expected, fixedCode);
    }

    [Fact]
    public async Task CustomAsyncMethod_WithoutToken_AddsTokenParameter()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task ProcessAsync(CancellationToken cancellationToken)
    {
        await {|#0:DoWorkAsync|}();
    }

    private async Task DoWorkAsync(CancellationToken cancellationToken = default)
    {
        await Task.Delay(100, cancellationToken);
    }
}";

        var fixedCode = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task ProcessAsync(CancellationToken cancellationToken)
    {
        await DoWorkAsync(cancellationToken);
    }

    private async Task DoWorkAsync(CancellationToken cancellationToken = default)
    {
        await Task.Delay(100, cancellationToken);
    }
}";

        var expected = VerifyCS.Diagnostic("CC002")
            .WithLocation(0)
            .WithArguments("DoWorkAsync", "cancellationToken");

        await VerifyCS.VerifyCodeFixAsync(test, expected, fixedCode);
    }

    [Fact]
    public async Task CustomAsyncMethod_WithExistingArguments_AddsTokenAsLastArgument()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task ProcessAsync(CancellationToken cancellationToken)
    {
        await {|#0:DoWorkAsync|}(""test"", 42);
    }

    private async Task DoWorkAsync(string name, int value, CancellationToken cancellationToken = default)
    {
        await Task.Delay(100, cancellationToken);
    }
}";

        var fixedCode = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task ProcessAsync(CancellationToken cancellationToken)
    {
        await DoWorkAsync(""test"", 42, cancellationToken);
    }

    private async Task DoWorkAsync(string name, int value, CancellationToken cancellationToken = default)
    {
        await Task.Delay(100, cancellationToken);
    }
}";

        var expected = VerifyCS.Diagnostic("CC002")
            .WithLocation(0)
            .WithArguments("DoWorkAsync", "cancellationToken");

        await VerifyCS.VerifyCodeFixAsync(test, expected, fixedCode);
    }

    [Fact]
    public async Task CustomMethod_NamedArgs_PicksTokenNameFromMatchingOverload()
    {
        // Declaration order would find DoWorkAsync(string, CancellationToken ct) first, but the
        // call binds DoWorkAsync(string, int) and the fixed call binds the three-parameter
        // overload — the named argument must use that overload's name, not the first one's.
        var test = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task ProcessAsync(CancellationToken cancellationToken)
    {
        await {|#0:DoWorkAsync|}(retries: 3, name: ""job"");
    }

    private Task DoWorkAsync(string name, CancellationToken ct) => Task.CompletedTask;
    private Task DoWorkAsync(string name, int retries) => Task.CompletedTask;
    private Task DoWorkAsync(string name, int retries, CancellationToken cancellationToken) => Task.CompletedTask;
}";

        var fixedCode = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task ProcessAsync(CancellationToken cancellationToken)
    {
        await DoWorkAsync(retries: 3, name: ""job"", cancellationToken: cancellationToken);
    }

    private Task DoWorkAsync(string name, CancellationToken ct) => Task.CompletedTask;
    private Task DoWorkAsync(string name, int retries) => Task.CompletedTask;
    private Task DoWorkAsync(string name, int retries, CancellationToken cancellationToken) => Task.CompletedTask;
}";

        var expected = VerifyCS.Diagnostic("CC002")
            .WithLocation(0)
            .WithArguments("DoWorkAsync", "cancellationToken");

        await VerifyCS.VerifyCodeFixAsync(test, expected, fixedCode);
    }

    [Fact]
    public async Task CustomMethod_WithOutOfPositionNamedArguments_AddsNamedTokenArgument()
    {
        // Appending a positional argument after an out-of-position named argument is CS8323;
        // the fix must emit a named token argument using the overload's parameter name.
        var test = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task ProcessAsync(CancellationToken cancellationToken)
    {
        await {|#0:DoWorkAsync|}(retries: 3, name: ""job"");
    }

    private Task DoWorkAsync(string name, int retries) => Task.CompletedTask;
    private Task DoWorkAsync(string name, int retries, CancellationToken token) => Task.CompletedTask;
}";

        var fixedCode = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task ProcessAsync(CancellationToken cancellationToken)
    {
        await DoWorkAsync(retries: 3, name: ""job"", token: cancellationToken);
    }

    private Task DoWorkAsync(string name, int retries) => Task.CompletedTask;
    private Task DoWorkAsync(string name, int retries, CancellationToken token) => Task.CompletedTask;
}";

        var expected = VerifyCS.Diagnostic("CC002")
            .WithLocation(0)
            .WithArguments("DoWorkAsync", "cancellationToken");

        await VerifyCS.VerifyCodeFixAsync(test, expected, fixedCode);
    }
}
