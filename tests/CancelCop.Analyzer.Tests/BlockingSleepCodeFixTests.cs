using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixVerifier<
    CancelCop.Analyzer.BlockingSleepAnalyzer,
    CancelCop.Analyzer.BlockingSleepCodeFixProvider,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace CancelCop.Analyzer.Tests;

public class BlockingSleepCodeFixTests
{
    [Fact]
    public async Task ThreadSleep_WithToken_BecomesTaskDelayWithToken()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(CancellationToken ct)
    {
        {|#0:Thread.Sleep(1000)|};
        await Task.Yield();
    }
}";

        var fixedCode = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(CancellationToken ct)
    {
        await Task.Delay(1000, ct);
        await Task.Yield();
    }
}";

        var expected = VerifyCS.Diagnostic("CC013").WithLocation(0);
        await VerifyCS.VerifyCodeFixAsync(test, expected, fixedCode);
    }

    [Fact]
    public async Task MultipleThreadSleeps_AllRewritten()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    private Task WorkAsync() => Task.CompletedTask;

    public async Task RunAsync(CancellationToken ct)
    {
        {|#0:Thread.Sleep(1000)|};
        await WorkAsync();
        {|#1:Thread.Sleep(2000)|};
    }
}";

        var fixedCode = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    private Task WorkAsync() => Task.CompletedTask;

    public async Task RunAsync(CancellationToken ct)
    {
        await Task.Delay(1000, ct);
        await WorkAsync();
        await Task.Delay(2000, ct);
    }
}";

        await VerifyCS.VerifyCodeFixAsync(
            test,
            new[]
            {
                VerifyCS.Diagnostic("CC013").WithLocation(0),
                VerifyCS.Diagnostic("CC013").WithLocation(1),
            },
            fixedCode);
    }

    [Fact]
    public async Task ThreadSleep_WithoutToken_BecomesTaskDelay()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync()
    {
        {|#0:Thread.Sleep(1000)|};
        await Task.Yield();
    }
}";

        var fixedCode = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync()
    {
        await Task.Delay(1000);
        await Task.Yield();
    }
}";

        var expected = VerifyCS.Diagnostic("CC013").WithLocation(0);
        await VerifyCS.VerifyCodeFixAsync(test, expected, fixedCode);
    }
}
