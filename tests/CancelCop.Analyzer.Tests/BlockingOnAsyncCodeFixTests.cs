using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixVerifier<
    CancelCop.Analyzer.BlockingOnAsyncAnalyzer,
    CancelCop.Analyzer.BlockingOnAsyncCodeFixProvider,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier
>;

namespace CancelCop.Analyzer.Tests;

public class BlockingOnAsyncCodeFixTests
{
    private const string Harness =
        @"
using System.Threading.Tasks;

public class TestClass
{
    private Task<int> GetValueAsync() => Task.FromResult(0);
    private Task DoAsync() => Task.CompletedTask;
";

    [Fact]
    public async Task ResultAsReceiver_ParenthesizesAwaitBeforeMemberAccess()
    {
        var test =
            Harness
            + @"
    public async Task<string> RunAsync()
    {
        await Task.Yield();
        return GetValueAsync().{|#0:Result|}.ToString();
    }
}";

        var fixedCode =
            Harness
            + @"
    public async Task<string> RunAsync()
    {
        await Task.Yield();
        return (await GetValueAsync()).ToString();
    }
}";

        var expected = VerifyCS.Diagnostic("CC015").WithLocation(0).WithArguments(".Result");
        await VerifyCS.VerifyCodeFixAsync(test, expected, fixedCode);
    }

    [Fact]
    public async Task GetResultAsReceiver_ParenthesizesAwaitBeforeMemberAccess()
    {
        var test =
            Harness
            + @"
    public async Task<string> RunAsync()
    {
        await Task.Yield();
        return GetValueAsync().GetAwaiter().{|#0:GetResult|}().ToString();
    }
}";

        var fixedCode =
            Harness
            + @"
    public async Task<string> RunAsync()
    {
        await Task.Yield();
        return (await GetValueAsync()).ToString();
    }
}";

        var expected = VerifyCS
            .Diagnostic("CC015")
            .WithLocation(0)
            .WithArguments(".GetAwaiter().GetResult()");
        await VerifyCS.VerifyCodeFixAsync(test, expected, fixedCode);
    }

    [Fact]
    public async Task FixAll_TwoResults_BothBecomeAwait()
    {
        var test =
            Harness
            + @"
    public async Task<int> RunAsync()
    {
        await Task.Yield();
        var x = GetValueAsync().{|#0:Result|};
        var y = GetValueAsync().{|#1:Result|};
        return x + y;
    }
}";

        var fixedCode =
            Harness
            + @"
    public async Task<int> RunAsync()
    {
        await Task.Yield();
        var x = (await GetValueAsync());
        var y = (await GetValueAsync());
        return x + y;
    }
}";

        await VerifyCS.VerifyCodeFixAsync(
            test,
            new[]
            {
                VerifyCS.Diagnostic("CC015").WithLocation(0).WithArguments(".Result"),
                VerifyCS.Diagnostic("CC015").WithLocation(1).WithArguments(".Result"),
            },
            fixedCode
        );
    }

    [Fact]
    public async Task Result_BecomesAwait()
    {
        var test =
            Harness
            + @"
    public async Task<int> RunAsync()
    {
        await Task.Yield();
        return GetValueAsync().{|#0:Result|};
    }
}";

        var fixedCode =
            Harness
            + @"
    public async Task<int> RunAsync()
    {
        await Task.Yield();
        return (await GetValueAsync());
    }
}";

        var expected = VerifyCS.Diagnostic("CC015").WithLocation(0).WithArguments(".Result");
        await VerifyCS.VerifyCodeFixAsync(test, expected, fixedCode);
    }

    [Fact]
    public async Task ChainedConditionalAccess_Result_ReportsWithoutOfferingAFix()
    {
        // `host?.Work.Result` is an ordinary member access, but it is the WhenNotNull
        // of `?.`. Replacing it with `(await .Work)` yields `host?(await .Work)`.
        var test =
            Harness
            + @"
    public async Task<int> RunAsync(Host? host)
    {
        await Task.Yield();
        return host?.Work.{|#0:Result|} ?? 0;
    }
}

public class Host
{
    public Task<int> Work { get; set; } = Task.FromResult(0);
}
";

        var expected = VerifyCS.Diagnostic("CC015").WithLocation(0).WithArguments(".Result");
        await VerifyCS.VerifyCodeFixAsync(test, expected, test);
    }

    [Fact]
    public async Task ChainedConditionalAccess_Wait_ReportsWithoutOfferingAFix()
    {
        var test =
            Harness
            + @"
    public async Task RunAsync(Host? host)
    {
        await Task.Yield();
        host?.Work.{|#0:Wait|}();
    }
}

public class Host
{
    public Task Work { get; set; } = Task.CompletedTask;
}
";

        var expected = VerifyCS.Diagnostic("CC015").WithLocation(0).WithArguments(".Wait()");
        await VerifyCS.VerifyCodeFixAsync(test, expected, test);
    }

    [Fact]
    public async Task ChainedConditionalAccess_GetResult_ReportsWithoutOfferingAFix()
    {
        var test =
            Harness
            + @"
    public async Task<int> RunAsync(Host? host)
    {
        await Task.Yield();
        return host?.Work.GetAwaiter().{|#0:GetResult|}() ?? 0;
    }
}

public class Host
{
    public Task<int> Work { get; set; } = Task.FromResult(0);
}
";

        var expected = VerifyCS
            .Diagnostic("CC015")
            .WithLocation(0)
            .WithArguments(".GetAwaiter().GetResult()");
        await VerifyCS.VerifyCodeFixAsync(test, expected, test);
    }

    [Fact]
    public async Task DirectConditionalResult_ReportsWithoutOfferingAFix()
    {
        var test =
            Harness
            + @"
    public async Task<int> RunAsync(Task<int>? task)
    {
        await Task.Yield();
        return task?.{|#0:Result|} ?? 0;
    }
}";

        var expected = VerifyCS.Diagnostic("CC015").WithLocation(0).WithArguments(".Result");
        await VerifyCS.VerifyCodeFixAsync(test, expected, test);
    }

    [Fact]
    public async Task ResultAsArgumentInsideUnrelatedConditionalAccess_IsFixed()
    {
        var test =
            Harness
            + @"
    public async Task<int> RunAsync(Host? host)
    {
        await Task.Yield();
        return host?.Use(GetValueAsync().{|#0:Result|}) ?? 0;
    }
}

public class Host
{
    public int Use(int value) => value;
}
";

        var fixedCode =
            Harness
            + @"
    public async Task<int> RunAsync(Host? host)
    {
        await Task.Yield();
        return host?.Use((await GetValueAsync())) ?? 0;
    }
}

public class Host
{
    public int Use(int value) => value;
}
";

        var expected = VerifyCS.Diagnostic("CC015").WithLocation(0).WithArguments(".Result");
        await VerifyCS.VerifyCodeFixAsync(test, expected, fixedCode);
    }

    [Fact]
    public async Task Wait_BecomesAwait()
    {
        var test =
            Harness
            + @"
    public async Task RunAsync()
    {
        await Task.Yield();
        DoAsync().{|#0:Wait|}();
    }
}";

        var fixedCode =
            Harness
            + @"
    public async Task RunAsync()
    {
        await Task.Yield();
        await DoAsync();
    }
}";

        var expected = VerifyCS.Diagnostic("CC015").WithLocation(0).WithArguments(".Wait()");
        await VerifyCS.VerifyCodeFixAsync(test, expected, fixedCode);
    }

    [Fact]
    public async Task ConfigureAwaitGetResult_BecomesAwaitOfConfigured()
    {
        var test =
            Harness
            + @"
    public async Task<int> RunAsync()
    {
        await Task.Yield();
        return GetValueAsync().ConfigureAwait(false).GetAwaiter().{|#0:GetResult|}();
    }
}";

        var fixedCode =
            Harness
            + @"
    public async Task<int> RunAsync()
    {
        await Task.Yield();
        return (await GetValueAsync().ConfigureAwait(false));
    }
}";

        var expected = VerifyCS
            .Diagnostic("CC015")
            .WithLocation(0)
            .WithArguments(".GetAwaiter().GetResult()");
        await VerifyCS.VerifyCodeFixAsync(test, expected, fixedCode);
    }

    [Fact]
    public async Task GetAwaiterGetResult_BecomesAwait()
    {
        var test =
            Harness
            + @"
    public async Task<int> RunAsync()
    {
        await Task.Yield();
        return GetValueAsync().GetAwaiter().{|#0:GetResult|}();
    }
}";

        var fixedCode =
            Harness
            + @"
    public async Task<int> RunAsync()
    {
        await Task.Yield();
        return (await GetValueAsync());
    }
}";

        var expected = VerifyCS
            .Diagnostic("CC015")
            .WithLocation(0)
            .WithArguments(".GetAwaiter().GetResult()");
        await VerifyCS.VerifyCodeFixAsync(test, expected, fixedCode);
    }
}
