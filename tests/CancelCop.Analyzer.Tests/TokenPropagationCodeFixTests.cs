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
}
