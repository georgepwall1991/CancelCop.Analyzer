using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    CancelCop.Analyzer.BlockingCallAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace CancelCop.Analyzer.Tests;

public class BlockingCallAnalyzerTests
{
    [Fact]
    public async Task TaskResult_ShouldReportDiagnostic()
    {
        var test = @"
using System.Threading.Tasks;

public class TestClass
{
    public void Process()
    {
        var result = GetDataAsync().{|#0:Result|};
    }

    private Task<int> GetDataAsync() => Task.FromResult(42);
}";

        var expected = VerifyCS.Diagnostic("CC011")
            .WithLocation(0)
            .WithArguments(".Result");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task TaskWait_ShouldReportDiagnostic()
    {
        var test = @"
using System.Threading.Tasks;

public class TestClass
{
    public void Process()
    {
        GetDataAsync().{|#0:Wait|}();
    }

    private Task GetDataAsync() => Task.Delay(1000);
}";

        var expected = VerifyCS.Diagnostic("CC011")
            .WithLocation(0)
            .WithArguments(".Wait()");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task GetAwaiterGetResult_ShouldReportDiagnostic()
    {
        var test = @"
using System.Threading.Tasks;

public class TestClass
{
    public void Process()
    {
        var result = {|#0:GetDataAsync().GetAwaiter().GetResult()|};
    }

    private Task<int> GetDataAsync() => Task.FromResult(42);
}";

        var expected = VerifyCS.Diagnostic("CC011")
            .WithLocation(0)
            .WithArguments(".GetAwaiter().GetResult()");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task TaskWaitAll_ShouldReportDiagnostic()
    {
        var test = @"
using System.Threading.Tasks;

public class TestClass
{
    public void Process()
    {
        var t1 = Task.Delay(1000);
        var t2 = Task.Delay(2000);
        Task.{|#0:WaitAll|}(t1, t2);
    }
}";

        var expected = VerifyCS.Diagnostic("CC011")
            .WithLocation(0)
            .WithArguments(".WaitAll()");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task TaskWaitAny_ShouldReportDiagnostic()
    {
        var test = @"
using System.Threading.Tasks;

public class TestClass
{
    public void Process()
    {
        var t1 = Task.Delay(1000);
        var t2 = Task.Delay(2000);
        Task.{|#0:WaitAny|}(t1, t2);
    }
}";

        var expected = VerifyCS.Diagnostic("CC011")
            .WithLocation(0)
            .WithArguments(".WaitAny()");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task AwaitTask_ShouldNotReportDiagnostic()
    {
        var test = @"
using System.Threading.Tasks;

public class TestClass
{
    public async Task ProcessAsync()
    {
        var result = await GetDataAsync();
    }

    private Task<int> GetDataAsync() => Task.FromResult(42);
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ValueTaskResult_ShouldReportDiagnostic()
    {
        var test = @"
using System.Threading.Tasks;

public class TestClass
{
    public void Process()
    {
        var result = GetDataAsync().{|#0:Result|};
    }

    private ValueTask<int> GetDataAsync() => new ValueTask<int>(42);
}";

        var expected = VerifyCS.Diagnostic("CC011")
            .WithLocation(0)
            .WithArguments(".Result");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task NonTaskResult_ShouldNotReportDiagnostic()
    {
        var test = @"
using System.Threading.Tasks;

public class TestClass
{
    public void Process()
    {
        var container = new ResultContainer { Result = 42 };
        var result = container.Result;
    }
}

public class ResultContainer
{
    public int Result { get; set; }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task MultipleBlockingCalls_ShouldReportMultipleDiagnostics()
    {
        var test = @"
using System.Threading.Tasks;

public class TestClass
{
    public void Process()
    {
        GetDataAsync().{|#0:Wait|}();
        var result = GetDataAsync().{|#1:Result|};
    }

    private Task<int> GetDataAsync() => Task.FromResult(42);
}";

        var expected1 = VerifyCS.Diagnostic("CC011")
            .WithLocation(0)
            .WithArguments(".Wait()");

        var expected2 = VerifyCS.Diagnostic("CC011")
            .WithLocation(1)
            .WithArguments(".Result");

        await VerifyCS.VerifyAnalyzerAsync(test, expected1, expected2);
    }
}
