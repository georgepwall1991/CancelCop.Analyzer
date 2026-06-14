using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    CancelCop.Analyzer.AsyncVoidAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace CancelCop.Analyzer.Tests;

public class AsyncVoidAnalyzerTests
{
    [Fact]
    public async Task AsyncVoidMethod_ShouldReportDiagnostic()
    {
        var test = @"
using System.Threading.Tasks;

public class TestClass
{
    public async void {|#0:ProcessAsync|}()
    {
        await Task.CompletedTask;
    }
}";

        var expected = VerifyCS.Diagnostic("CC023").WithLocation(0).WithArguments("ProcessAsync");
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task StaticAsyncVoid_ShouldReportDiagnostic()
    {
        // static does not make a method an event handler, so a static async void is still flagged.
        var test = @"
using System.Threading.Tasks;

public class TestClass
{
    public static async void {|#0:ProcessAsync|}()
    {
        await Task.CompletedTask;
    }
}";

        var expected = VerifyCS.Diagnostic("CC023").WithLocation(0).WithArguments("ProcessAsync");
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task ProtectedAsyncVoid_ShouldReportDiagnostic()
    {
        var test = @"
using System.Threading.Tasks;

public class TestClass
{
    protected async void {|#0:ProcessAsync|}()
    {
        await Task.CompletedTask;
    }
}";

        var expected = VerifyCS.Diagnostic("CC023").WithLocation(0).WithArguments("ProcessAsync");
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task EventHandler_ShouldNotReportDiagnostic()
    {
        var test = @"
using System;
using System.Threading.Tasks;

public class TestClass
{
    public async void Button_Click(object sender, EventArgs e)
    {
        await Task.CompletedTask;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task CustomEventArgsHandler_ShouldNotReportDiagnostic()
    {
        var test = @"
using System;
using System.Threading.Tasks;

public class MyEventArgs : EventArgs { }

public class TestClass
{
    public async void Handler(object sender, MyEventArgs e)
    {
        await Task.CompletedTask;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsyncTaskMethod_ShouldNotReportDiagnostic()
    {
        var test = @"
using System.Threading.Tasks;

public class TestClass
{
    public async Task ProcessAsync()
    {
        await Task.CompletedTask;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsyncVoidLocalFunction_ShouldReportDiagnostic()
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

        var expected = VerifyCS.Diagnostic("CC023").WithLocation(0).WithArguments("Local");
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task SyncVoidMethod_ShouldNotReportDiagnostic()
    {
        var test = @"
public class TestClass
{
    public void Process()
    {
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
