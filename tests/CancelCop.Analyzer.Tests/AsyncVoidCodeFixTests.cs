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
    public async Task FixAll_TwoAsyncVoidMethods_AddImportOnce()
    {
        var test = @"
using System;

public class TestClass
{
    public async void {|#0:A|}() => await System.Threading.Tasks.Task.CompletedTask;
    public async void {|#1:B|}() => await System.Threading.Tasks.Task.CompletedTask;
}";

        var fixedCode = @"
using System;
using System.Threading.Tasks;

public class TestClass
{
    public async Task A() => await System.Threading.Tasks.Task.CompletedTask;
    public async Task B() => await System.Threading.Tasks.Task.CompletedTask;
}";

        await VerifyCS.VerifyCodeFixAsync(
            test,
            new[]
            {
                VerifyCS.Diagnostic("CC023").WithLocation(0).WithArguments("A"),
                VerifyCS.Diagnostic("CC023").WithLocation(1).WithArguments("B"),
            },
            fixedCode);
    }

    [Fact]
    public async Task AsyncVoid_BecomesAsyncTask_AndAddsImport()
    {
        var test = @"
using System;

public class TestClass
{
    public async void {|#0:ProcessAsync|}()
    {
        await System.Threading.Tasks.Task.CompletedTask;
    }
}";

        var fixedCode = @"
using System;
using System.Threading.Tasks;

public class TestClass
{
    public async Task ProcessAsync()
    {
        await System.Threading.Tasks.Task.CompletedTask;
    }
}";

        var expected = VerifyCS.Diagnostic("CC023").WithLocation(0).WithArguments("ProcessAsync");
        await VerifyCS.VerifyCodeFixAsync(test, expected, fixedCode);
    }

    [Fact]
    public async Task AsyncVoidLocalFunction_BecomesAsyncTask()
    {
        var test = @"
using System.Threading.Tasks;

public class TestClass
{
    public void Outer()
    {
        async void {|#0:Local|}()
        {
            await Task.CompletedTask;
        }

        Local();
    }
}";

        var fixedCode = @"
using System.Threading.Tasks;

public class TestClass
{
    public void Outer()
    {
        async Task Local()
        {
            await Task.CompletedTask;
        }

        Local();
    }
}";

        var expected = VerifyCS.Diagnostic("CC023").WithLocation(0).WithArguments("Local");
        await VerifyCS.VerifyCodeFixAsync(test, expected, fixedCode);
    }
}
