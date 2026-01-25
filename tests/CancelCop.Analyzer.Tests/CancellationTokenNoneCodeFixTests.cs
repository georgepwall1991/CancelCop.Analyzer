using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixVerifier<
    CancelCop.Analyzer.CancellationTokenNoneAnalyzer,
    CancelCop.Analyzer.CancellationTokenNoneCodeFixProvider,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace CancelCop.Analyzer.Tests;

public class CancellationTokenNoneCodeFixTests
{
    [Fact]
    public async Task CancellationTokenNone_ShouldReplaceWithAvailableToken()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task ProcessAsync(CancellationToken ct)
    {
        await Task.Delay(1000, {|#0:CancellationToken.None|});
    }
}";

        var fixedCode = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task ProcessAsync(CancellationToken ct)
    {
        await Task.Delay(1000, ct);
    }
}";

        var expected = VerifyCS.Diagnostic("CC007")
            .WithLocation(0)
            .WithArguments("ct");

        await VerifyCS.VerifyCodeFixAsync(test, expected, fixedCode);
    }

    [Fact]
    public async Task DefaultCancellationToken_ShouldReplaceWithAvailableToken()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task ProcessAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(1000, {|#0:default(CancellationToken)|});
    }
}";

        var fixedCode = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task ProcessAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(1000, cancellationToken);
    }
}";

        var expected = VerifyCS.Diagnostic("CC007")
            .WithLocation(0)
            .WithArguments("cancellationToken");

        await VerifyCS.VerifyCodeFixAsync(test, expected, fixedCode);
    }

    [Fact]
    public async Task LocalFunction_CancellationTokenNone_ShouldReplaceWithLocalToken()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task ProcessAsync()
    {
        async Task LocalAsync(CancellationToken token)
        {
            await Task.Delay(1000, {|#0:CancellationToken.None|});
        }

        await LocalAsync(CancellationToken.None);
    }
}";

        var fixedCode = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task ProcessAsync()
    {
        async Task LocalAsync(CancellationToken token)
        {
            await Task.Delay(1000, token);
        }

        await LocalAsync(CancellationToken.None);
    }
}";

        var expected = VerifyCS.Diagnostic("CC007")
            .WithLocation(0)
            .WithArguments("token");

        await VerifyCS.VerifyCodeFixAsync(test, expected, fixedCode);
    }
}
