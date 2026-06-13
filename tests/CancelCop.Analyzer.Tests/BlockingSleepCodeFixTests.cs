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
