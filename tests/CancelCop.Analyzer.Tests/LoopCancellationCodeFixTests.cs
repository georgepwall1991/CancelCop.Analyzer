using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixVerifier<
    CancelCop.Analyzer.LoopCancellationAnalyzer,
    CancelCop.Analyzer.LoopCancellationCodeFixProvider,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace CancelCop.Analyzer.Tests;

public class LoopCancellationCodeFixTests
{
    [Fact]
    public async Task ForEachLoop_AddsThrowIfCancellationRequested()
    {
        var test = @"
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task ProcessAsync(List<int> items, CancellationToken cancellationToken)
    {
        {|#0:foreach|} (var item in items)
        {
            await Task.Delay(100, cancellationToken);
        }
    }
}";

        var fixedCode = @"
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task ProcessAsync(List<int> items, CancellationToken cancellationToken)
    {
        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(100, cancellationToken);
        }
    }
}";

        var expected = VerifyCS.Diagnostic("CC009")
            .WithLocation(0)
            .WithArguments("cancellationToken");

        await VerifyCS.VerifyCodeFixAsync(test, expected, fixedCode);
    }

    [Fact]
    public async Task ForLoop_AddsThrowIfCancellationRequested()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task ProcessAsync(int count, CancellationToken ct)
    {
        {|#0:for|} (int i = 0; i < count; i++)
        {
            await Task.Delay(100, ct);
        }
    }
}";

        var fixedCode = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task ProcessAsync(int count, CancellationToken ct)
    {
        for (int i = 0; i < count; i++)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(100, ct);
        }
    }
}";

        var expected = VerifyCS.Diagnostic("CC009")
            .WithLocation(0)
            .WithArguments("ct");

        await VerifyCS.VerifyCodeFixAsync(test, expected, fixedCode);
    }

    [Fact]
    public async Task WhileLoop_AddsThrowIfCancellationRequested()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task ProcessAsync(CancellationToken token)
    {
        int i = 0;
        {|#0:while|} (i < 10)
        {
            await Task.Delay(100, token);
            i++;
        }
    }
}";

        var fixedCode = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task ProcessAsync(CancellationToken token)
    {
        int i = 0;
        while (i < 10)
        {
            token.ThrowIfCancellationRequested();
            await Task.Delay(100, token);
            i++;
        }
    }
}";

        var expected = VerifyCS.Diagnostic("CC009")
            .WithLocation(0)
            .WithArguments("token");

        await VerifyCS.VerifyCodeFixAsync(test, expected, fixedCode);
    }

    [Fact]
    public async Task DoWhileLoop_AddsThrowIfCancellationRequested()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task ProcessAsync(CancellationToken cancellationToken)
    {
        int i = 0;
        {|#0:do|}
        {
            await Task.Delay(100, cancellationToken);
            i++;
        } while (i < 10);
    }
}";

        var fixedCode = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task ProcessAsync(CancellationToken cancellationToken)
    {
        int i = 0;
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(100, cancellationToken);
            i++;
        } while (i < 10);
    }
}";

        var expected = VerifyCS.Diagnostic("CC009")
            .WithLocation(0)
            .WithArguments("cancellationToken");

        await VerifyCS.VerifyCodeFixAsync(test, expected, fixedCode);
    }

    [Fact]
    public async Task EmptyLoop_AddsThrowIfCancellationRequested()
    {
        var test = @"
using System.Threading;

public class TestClass
{
    public void Process(int count, CancellationToken ct)
    {
        {|#0:for|} (int i = 0; i < count; i++)
        {
        }
    }
}";

        var fixedCode = @"
using System.Threading;

public class TestClass
{
    public void Process(int count, CancellationToken ct)
    {
        for (int i = 0; i < count; i++)
        {
            ct.ThrowIfCancellationRequested();
        }
    }
}";

        var expected = VerifyCS.Diagnostic("CC009")
            .WithLocation(0)
            .WithArguments("ct");

        await VerifyCS.VerifyCodeFixAsync(test, expected, fixedCode);
    }

    [Fact]
    public async Task LoopInLocalFunction_AddsThrowIfCancellationRequested()
    {
        var test = @"
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task ProcessAsync()
    {
        async Task LocalAsync(List<int> items, CancellationToken ct)
        {
            {|#0:foreach|} (var item in items)
            {
                await Task.Delay(100, ct);
            }
        }

        await LocalAsync(new List<int>(), CancellationToken.None);
    }
}";

        var fixedCode = @"
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task ProcessAsync()
    {
        async Task LocalAsync(List<int> items, CancellationToken ct)
        {
            foreach (var item in items)
            {
                ct.ThrowIfCancellationRequested();
                await Task.Delay(100, ct);
            }
        }

        await LocalAsync(new List<int>(), CancellationToken.None);
    }
}";

        var expected = VerifyCS.Diagnostic("CC009")
            .WithLocation(0)
            .WithArguments("ct");

        await VerifyCS.VerifyCodeFixAsync(test, expected, fixedCode);
    }
}
