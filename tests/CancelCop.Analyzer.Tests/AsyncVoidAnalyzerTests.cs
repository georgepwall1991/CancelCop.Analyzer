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
        await Task.Delay(1000);
    }
}";

        var expected = VerifyCS.Diagnostic("CC010")
            .WithLocation(0)
            .WithArguments("ProcessAsync");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
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
        await Task.Delay(1000);
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
    public void Process()
    {
        async void {|#0:LocalAsync|}()
        {
            await Task.Delay(1000);
        }

        LocalAsync();
    }
}";

        var expected = VerifyCS.Diagnostic("CC010")
            .WithLocation(0)
            .WithArguments("LocalAsync");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task AsyncTaskLocalFunction_ShouldNotReportDiagnostic()
    {
        var test = @"
using System.Threading.Tasks;

public class TestClass
{
    public async Task ProcessAsync()
    {
        async Task LocalAsync()
        {
            await Task.Delay(1000);
        }

        await LocalAsync();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task EventHandler_AsyncVoid_ShouldNotReportDiagnostic()
    {
        var test = @"
using System;
using System.Threading.Tasks;

public class TestClass
{
    public async void OnButtonClick(object sender, EventArgs e)
    {
        await Task.Delay(1000);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task PrivateAsyncVoidMethod_ShouldReportDiagnostic()
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

        var expected = VerifyCS.Diagnostic("CC010")
            .WithLocation(0)
            .WithArguments("ProcessAsync");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task OverrideAsyncVoid_ShouldNotReportDiagnostic()
    {
        var test = @"
using System.Threading.Tasks;

public class BaseClass
{
    public virtual async void ProcessAsync()
    {
        await Task.Delay(1000);
    }
}

public class DerivedClass : BaseClass
{
    public override async void ProcessAsync()
    {
        await Task.Delay(2000);
    }
}";

        // Only the base method should be reported
        var expected = VerifyCS.Diagnostic("CC010")
            .WithLocation(7, 26)
            .WithArguments("ProcessAsync");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task MultipleAsyncVoidMethods_ShouldReportMultipleDiagnostics()
    {
        var test = @"
using System.Threading.Tasks;

public class TestClass
{
    public async void {|#0:Method1Async|}()
    {
        await Task.Delay(1000);
    }

    public async void {|#1:Method2Async|}()
    {
        await Task.Delay(1000);
    }
}";

        var expected1 = VerifyCS.Diagnostic("CC010")
            .WithLocation(0)
            .WithArguments("Method1Async");

        var expected2 = VerifyCS.Diagnostic("CC010")
            .WithLocation(1)
            .WithArguments("Method2Async");

        await VerifyCS.VerifyAnalyzerAsync(test, expected1, expected2);
    }

    [Fact]
    public async Task SyncVoidMethod_ShouldNotReportDiagnostic()
    {
        var test = @"
public class TestClass
{
    public void Process()
    {
        // Regular sync method
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
