using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    CancelCop.Analyzer.BlockingOnAsyncAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace CancelCop.Analyzer.Tests;

public class BlockingOnAsyncAnalyzerTests
{
    private const string Harness = @"
using System.Threading.Tasks;

public class TestClass
{
    private Task<int> GetValueAsync() => Task.FromResult(0);
    private Task DoAsync() => Task.CompletedTask;
";

    [Fact]
    public async Task Result_InAsyncMethod_ShouldReportDiagnostic()
    {
        var test = Harness + @"
    public async Task<int> RunAsync()
    {
        await Task.Yield();
        return GetValueAsync().{|#0:Result|};
    }
}";

        var expected = VerifyCS.Diagnostic("CC015").WithLocation(0).WithArguments(".Result");
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task Wait_InAsyncMethod_ShouldReportDiagnostic()
    {
        var test = Harness + @"
    public async Task RunAsync()
    {
        await Task.Yield();
        DoAsync().{|#0:Wait|}();
    }
}";

        var expected = VerifyCS.Diagnostic("CC015").WithLocation(0).WithArguments(".Wait()");
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task Wait_InAsyncLocalFunction_ShouldReportDiagnostic()
    {
        var test = Harness + @"
    public void Outer()
    {
        async Task LocalAsync()
        {
            DoAsync().{|#0:Wait|}();
            await Task.Yield();
        }

        _ = LocalAsync();
    }
}";

        var expected = VerifyCS.Diagnostic("CC015").WithLocation(0).WithArguments(".Wait()");
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task GetAwaiterGetResult_InAsyncMethod_ShouldReportDiagnostic()
    {
        var test = Harness + @"
    public async Task<int> RunAsync()
    {
        await Task.Yield();
        return GetValueAsync().GetAwaiter().{|#0:GetResult|}();
    }
}";

        var expected = VerifyCS.Diagnostic("CC015").WithLocation(0).WithArguments(".GetAwaiter().GetResult()");
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task ValueTaskGetAwaiterGetResult_InAsyncMethod_ShouldReportDiagnostic()
    {
        var test = Harness + @"
    private ValueTask<int> GetVtAsync() => default;

    public async Task<int> RunAsync()
    {
        await Task.Yield();
        return GetVtAsync().GetAwaiter().{|#0:GetResult|}();
    }
}";

        var expected = VerifyCS.Diagnostic("CC015").WithLocation(0).WithArguments(".GetAwaiter().GetResult()");
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task ConfigureAwaitGetResult_InAsyncMethod_ShouldReportDiagnostic()
    {
        var test = Harness + @"
    public async Task<int> RunAsync()
    {
        await Task.Yield();
        return GetValueAsync().ConfigureAwait(false).GetAwaiter().{|#0:GetResult|}();
    }
}";

        var expected = VerifyCS.Diagnostic("CC015").WithLocation(0).WithArguments(".GetAwaiter().GetResult()");
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task WaitWithTimeout_InAsyncMethod_ShouldReportDiagnostic()
    {
        var test = Harness + @"
    public async Task RunAsync()
    {
        await Task.Yield();
        DoAsync().{|#0:Wait|}(1000);
    }
}";

        var expected = VerifyCS.Diagnostic("CC015").WithLocation(0).WithArguments(".Wait()");
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task WaitAll_InAsyncMethod_ShouldReportDiagnostic()
    {
        var test = Harness + @"
    public async Task RunAsync()
    {
        await Task.Yield();
        Task.{|#0:WaitAll|}(DoAsync());
    }
}";

        var expected = VerifyCS.Diagnostic("CC015").WithLocation(0).WithArguments(".WaitAll()");
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task ValueTaskResult_InAsyncMethod_ShouldReportDiagnostic()
    {
        var test = Harness + @"
    private ValueTask<int> GetValueTaskAsync() => default;

    public async Task<int> RunValueTaskAsync()
    {
        await Task.Yield();
        return GetValueTaskAsync().{|#0:Result|};
    }
}";

        var expected = VerifyCS.Diagnostic("CC015").WithLocation(0).WithArguments(".Result");
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task Result_InSyncMethod_ShouldNotReportDiagnostic()
    {
        var test = Harness + @"
    public int Run()
    {
        return GetValueAsync().Result;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task Result_OnNonTask_ShouldNotReportDiagnostic()
    {
        var test = Harness + @"
    public async Task RunAsync()
    {
        await Task.Yield();
        var c = new Custom();
        var x = c.Result;
    }
}

public class Custom { public int Result => 0; }";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
