using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixVerifier<
    CancelCop.Analyzer.ExplicitNoneTokenAnalyzer,
    CancelCop.Analyzer.ExplicitNoneTokenCodeFixProvider,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace CancelCop.Analyzer.Tests;

public class ExplicitNoneTokenCodeFixTests
{
    [Fact]
    public async Task FixAll_TwoNoneArguments_BothReplaced()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    private Task DoAsync(CancellationToken token) => Task.CompletedTask;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await DoAsync({|#0:CancellationToken.None|});
        await DoAsync({|#1:default|});
    }
}";

        var fixedCode = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    private Task DoAsync(CancellationToken token) => Task.CompletedTask;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await DoAsync(cancellationToken);
        await DoAsync(cancellationToken);
    }
}";

        await VerifyCS.VerifyCodeFixAsync(
            test,
            new[]
            {
                VerifyCS.Diagnostic("CC012").WithLocation(0).WithArguments("CancellationToken.None", "cancellationToken"),
                VerifyCS.Diagnostic("CC012").WithLocation(1).WithArguments("default", "cancellationToken"),
            },
            fixedCode);
    }

    [Fact]
    public async Task None_ReplacedWithInScopeToken()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    private Task DoAsync(CancellationToken token) => Task.CompletedTask;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await DoAsync({|#0:CancellationToken.None|});
    }
}";

        var fixedCode = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    private Task DoAsync(CancellationToken token) => Task.CompletedTask;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await DoAsync(cancellationToken);
    }
}";

        var expected = VerifyCS.Diagnostic("CC012")
            .WithLocation(0)
            .WithArguments("CancellationToken.None", "cancellationToken");

        await VerifyCS.VerifyCodeFixAsync(test, expected, fixedCode);
    }

    [Fact]
    public async Task Default_ReplacedWithDifferentlyNamedToken()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    private Task DoAsync(CancellationToken token) => Task.CompletedTask;

    public async Task RunAsync(CancellationToken ct)
    {
        await DoAsync({|#0:default|});
    }
}";

        var fixedCode = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    private Task DoAsync(CancellationToken token) => Task.CompletedTask;

    public async Task RunAsync(CancellationToken ct)
    {
        await DoAsync(ct);
    }
}";

        var expected = VerifyCS.Diagnostic("CC012")
            .WithLocation(0)
            .WithArguments("default", "ct");

        await VerifyCS.VerifyCodeFixAsync(test, expected, fixedCode);
    }
}
