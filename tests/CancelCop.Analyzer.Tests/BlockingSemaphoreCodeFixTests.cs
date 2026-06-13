using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixVerifier<
    CancelCop.Analyzer.BlockingSemaphoreAnalyzer,
    CancelCop.Analyzer.BlockingSemaphoreCodeFixProvider,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace CancelCop.Analyzer.Tests;

public class BlockingSemaphoreCodeFixTests
{
    [Fact]
    public async Task Wait_WithToken_BecomesAwaitWaitAsyncWithToken()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(SemaphoreSlim gate, CancellationToken ct)
    {
        gate.{|#0:Wait|}();
        await Task.Yield();
    }
}";

        var fixedCode = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(SemaphoreSlim gate, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        await Task.Yield();
    }
}";

        var expected = VerifyCS.Diagnostic("CC026").WithLocation(0);
        await VerifyCS.VerifyCodeFixAsync(test, expected, fixedCode);
    }

    [Fact]
    public async Task WaitWithTokenArg_CarriesArgumentThrough()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(SemaphoreSlim gate, CancellationToken ct)
    {
        gate.{|#0:Wait|}(ct);
        await Task.Yield();
    }
}";

        var fixedCode = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(SemaphoreSlim gate, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        await Task.Yield();
    }
}";

        var expected = VerifyCS.Diagnostic("CC026").WithLocation(0);
        await VerifyCS.VerifyCodeFixAsync(test, expected, fixedCode);
    }

    [Fact]
    public async Task Wait_WithoutToken_BecomesAwaitWaitAsync()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(SemaphoreSlim gate)
    {
        gate.{|#0:Wait|}();
        await Task.Yield();
    }
}";

        var fixedCode = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(SemaphoreSlim gate)
    {
        await gate.WaitAsync();
        await Task.Yield();
    }
}";

        var expected = VerifyCS.Diagnostic("CC026").WithLocation(0);
        await VerifyCS.VerifyCodeFixAsync(test, expected, fixedCode);
    }
}
