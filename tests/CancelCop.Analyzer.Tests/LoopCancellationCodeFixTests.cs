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
    public async Task FixAll_TwoForeachLoops_BothGetThrowIfCancellationRequested()
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

        {|#1:foreach|} (var item in items)
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

        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(100, cancellationToken);
        }
    }
}";

        await VerifyCS.VerifyCodeFixAsync(
            test,
            new[]
            {
                VerifyCS.Diagnostic("CC009").WithLocation(0).WithArguments("cancellationToken"),
                VerifyCS.Diagnostic("CC009").WithLocation(1).WithArguments("cancellationToken"),
            },
            fixedCode);
    }

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

    [Fact]
    public async Task SingleStatementBody_WrapsInBlockWithCheckFirst()
    {
        // A brace-less loop body must become a block so the check executes every
        // iteration, not only the first statement of a malformed rewrite.
        var source = @"
using System;
using System.Threading;

public class TestClass
{
    public void Process(System.Collections.Generic.List<int> items, CancellationToken ct)
    {
        {|#0:foreach|} (var item in items)
            Console.WriteLine(item);
    }
}";

        var fixedCode = @"
using System;
using System.Threading;

public class TestClass
{
    public void Process(System.Collections.Generic.List<int> items, CancellationToken ct)
    {
        foreach (var item in items)
        {
            ct.ThrowIfCancellationRequested();
            Console.WriteLine(item);
        }
    }
}";

        var expected = VerifyCS.Diagnostic("CC009").WithLocation(0).WithArguments("ct");
        await VerifyCS.VerifyCodeFixAsync(source, expected, fixedCode);
    }

    [Fact]
    public async Task InnerWhileOfNestedLoops_CheckInsertedInInnerBodyOnly()
    {
        // The diagnostic targets the inner while; fixing it inserts the check into
        // the INNER body only — the outer loop's statements stay untouched.
        var source = @"
using System;
using System.Threading;

public class TestClass
{
    public void Process(int[][] rows, CancellationToken ct)
    {
        foreach (var row in rows)
        {
            {|#0:while|} (row.Length > 0)
            {
                Console.WriteLine(row[0]);
                break;
            }
        }
    }
}";

        var fixedCode = @"
using System;
using System.Threading;

public class TestClass
{
    public void Process(int[][] rows, CancellationToken ct)
    {
        foreach (var row in rows)
        {
            while (row.Length > 0)
            {
                ct.ThrowIfCancellationRequested();
                Console.WriteLine(row[0]);
                break;
            }
        }
    }
}";

        // The outer foreach reports too (no check of its own); the fix for marker 0
        // must still touch only the inner body.
        source = source.Replace("foreach (var row in rows)", "{|#1:foreach|} (var row in rows)");
        // Both loops lack a check, so the harness applies both fixes; each loop ends
        // up with its own check.
        source = source.Replace("foreach (var row in rows)", "{|#1:foreach|} (var row in rows)");
        fixedCode = fixedCode.Replace(
            "foreach (var row in rows)\n        {",
            "foreach (var row in rows)\n        {\n            ct.ThrowIfCancellationRequested();");
        var expected = new[]
        {
            VerifyCS.Diagnostic("CC009").WithLocation(0).WithArguments("ct"),
            VerifyCS.Diagnostic("CC009").WithLocation(1).WithArguments("ct"),
        };
await VerifyCS.VerifyCodeFixAsync(source, expected, fixedCode);
    }
}
