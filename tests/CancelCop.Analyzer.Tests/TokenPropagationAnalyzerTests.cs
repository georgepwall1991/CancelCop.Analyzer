using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    CancelCop.Analyzer.TokenPropagationAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace CancelCop.Analyzer.Tests;

public class TokenPropagationAnalyzerTests
{
    [Fact]
    public async Task TaskDelay_WithoutToken_WhenTokenAvailable_ShouldReportDiagnostic()
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

        var expected = VerifyCS.Diagnostic("CC002")
            .WithLocation(0)
            .WithArguments("Delay", "cancellationToken");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task TaskDelay_WithToken_ShouldNotReportDiagnostic()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task ProcessAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(100, cancellationToken);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TaskRun_WithoutToken_WhenTokenAvailable_ShouldReportDiagnostic()
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

        var expected = VerifyCS.Diagnostic("CC002")
            .WithLocation(0)
            .WithArguments("Run", "cancellationToken");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task CustomAsyncMethod_WithoutToken_WhenTokenAvailable_ShouldReportDiagnostic()
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

        var expected = VerifyCS.Diagnostic("CC002")
            .WithLocation(0)
            .WithArguments("DoWorkAsync", "cancellationToken");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task CustomAsyncMethod_WithToken_ShouldNotReportDiagnostic()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task ProcessAsync(CancellationToken cancellationToken)
    {
        await DoWorkAsync(cancellationToken);
    }

    private async Task DoWorkAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(100, cancellationToken);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsyncMethod_NoTokenParameter_ShouldNotReportDiagnostic()
    {
        var test = @"
using System.Threading.Tasks;

public class TestClass
{
    public async Task ProcessAsync()
    {
        await Task.Delay(100);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task MultipleAsyncCalls_WithoutTokens_ShouldReportMultipleDiagnostics()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task ProcessAsync(CancellationToken cancellationToken)
    {
        await Task.{|#0:Delay|}(100);
        await {|#1:DoWorkAsync|}();
    }

    private async Task DoWorkAsync(CancellationToken cancellationToken = default)
    {
        await Task.Delay(100, cancellationToken);
    }
}";

        var expected1 = VerifyCS.Diagnostic("CC002")
            .WithLocation(0)
            .WithArguments("Delay", "cancellationToken");

        var expected2 = VerifyCS.Diagnostic("CC002")
            .WithLocation(1)
            .WithArguments("DoWorkAsync", "cancellationToken");

        await VerifyCS.VerifyAnalyzerAsync(test, expected1, expected2);
    }

    [Fact]
    public async Task LocalFunction_WithToken_TaskDelayWithoutToken_ShouldReportDiagnostic()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task ProcessAsync()
    {
        async Task LocalAsync(CancellationToken ct)
        {
            await Task.{|#0:Delay|}(100);
        }

        await LocalAsync(CancellationToken.None);
    }
}";

        var expected = VerifyCS.Diagnostic("CC002")
            .WithLocation(0)
            .WithArguments("Delay", "ct");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task LocalFunction_WithToken_TaskDelayWithToken_ShouldNotReportDiagnostic()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task ProcessAsync()
    {
        async Task LocalAsync(CancellationToken ct)
        {
            await Task.Delay(100, ct);
        }

        await LocalAsync(CancellationToken.None);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task LocalFunction_NoToken_ButOuterMethodHasToken_ShouldReportDiagnostic()
    {
        // The local function doesn't have a token, so we should look at the outer method
        var test = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task ProcessAsync(CancellationToken cancellationToken)
    {
        async Task LocalAsync()
        {
            await Task.{|#0:Delay|}(100);
        }

        await LocalAsync();
    }
}";

        var expected = VerifyCS.Diagnostic("CC002")
            .WithLocation(0)
            .WithArguments("Delay", "cancellationToken");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task NestedLocalFunction_WithToken_ShouldReportDiagnostic()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task ProcessAsync()
    {
        async Task OuterLocalAsync()
        {
            async Task InnerLocalAsync(CancellationToken ct)
            {
                await Task.{|#0:Delay|}(100);
            }

            await InnerLocalAsync(CancellationToken.None);
        }

        await OuterLocalAsync();
    }
}";

        var expected = VerifyCS.Diagnostic("CC002")
            .WithLocation(0)
            .WithArguments("Delay", "ct");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }
}
