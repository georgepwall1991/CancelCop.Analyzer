using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixVerifier<
    CancelCop.Analyzer.AsyncVoidAnalyzer,
    CancelCop.Analyzer.AsyncVoidCodeFixProvider,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace CancelCop.Analyzer.Tests;

public class AsyncVoidCodeFixTests
{
    [Fact]
    public async Task AsyncVoidMethod_ShouldChangeToAsyncTask()
    {
        var test = @"
using System.Threading.Tasks;

public class TestClass
{
    public async void {|#0:ProcessAsync|}()
    {
        await Task.Delay(1000);
    }
}";

        var fixedCode = @"
using System.Threading.Tasks;

public class TestClass
{
    public async Task ProcessAsync()
    {
        await Task.Delay(1000);
    }
}";

        var expected = VerifyCS.Diagnostic("CC010")
            .WithLocation(0)
            .WithArguments("ProcessAsync");

        await VerifyCS.VerifyCodeFixAsync(test, expected, fixedCode);
    }

    [Fact]
    public async Task AsyncVoidMethod_WithoutTasksUsing_ShouldAddUsing()
    {
        var test = @"
public class TestClass
{
    public async void {|#0:ProcessAsync|}()
    {
        // Just an async void method
    }
}";

        var fixedCode = @"
using System.Threading.Tasks;

public class TestClass
{
    public async Task ProcessAsync()
    {
        // Just an async void method
    }
}";

        var expected = VerifyCS.Diagnostic("CC010")
            .WithLocation(0)
            .WithArguments("ProcessAsync");

        await VerifyCS.VerifyCodeFixAsync(test, expected, fixedCode);
    }

    [Fact]
    public async Task AsyncVoidLocalFunction_ShouldChangeToAsyncTask()
    {
        var test = @"
using System.Threading.Tasks;

public class TestClass
{
    public void Process()
    {
        async void {|#0:LocalAsync|}()
        {
            await Task.Delay(1000);
        }

        LocalAsync();
    }
}";

        var fixedCode = @"
using System.Threading.Tasks;

public class TestClass
{
    public void Process()
    {
        async Task LocalAsync()
        {
            await Task.Delay(1000);
        }

        LocalAsync();
    }
}";

        var expected = VerifyCS.Diagnostic("CC010")
            .WithLocation(0)
            .WithArguments("LocalAsync");

        await VerifyCS.VerifyCodeFixAsync(test, expected, fixedCode);
    }

    [Fact]
    public async Task PrivateAsyncVoidMethod_ShouldChangeToAsyncTask()
    {
        var test = @"
using System.Threading.Tasks;

public class TestClass
{
    private async void {|#0:ProcessAsync|}()
    {
        await Task.Delay(1000);
    }
}";

        var fixedCode = @"
using System.Threading.Tasks;

public class TestClass
{
    private async Task ProcessAsync()
    {
        await Task.Delay(1000);
    }
}";

        var expected = VerifyCS.Diagnostic("CC010")
            .WithLocation(0)
            .WithArguments("ProcessAsync");

        await VerifyCS.VerifyCodeFixAsync(test, expected, fixedCode);
    }
}
