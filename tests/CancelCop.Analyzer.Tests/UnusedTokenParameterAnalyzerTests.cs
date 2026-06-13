using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    CancelCop.Analyzer.UnusedTokenParameterAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace CancelCop.Analyzer.Tests;

public class UnusedTokenParameterAnalyzerTests
{
    [Fact]
    public async Task AsyncMethod_UnusedToken_ShouldReportDiagnostic()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(CancellationToken {|#0:cancellationToken|})
    {
        await Task.Delay(1000);
    }
}";

        var expected = VerifyCS.Diagnostic("CC016").WithLocation(0).WithArguments("cancellationToken");
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task AsyncMethod_UsedToken_ShouldNotReportDiagnostic()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(1000, cancellationToken);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task SyncMethod_UnusedToken_ShouldNotReportDiagnostic()
    {
        var test = @"
using System.Threading;

public class TestClass
{
    public void Run(CancellationToken cancellationToken)
    {
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task InterfaceImplementation_UnusedToken_ShouldNotReportDiagnostic()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;

public interface IRunner
{
    Task RunAsync(CancellationToken cancellationToken);
}

public class TestClass : IRunner
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(1000);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TokenUsedInsideLambda_ShouldNotReportDiagnostic()
    {
        var test = @"
using System;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        Action a = () => cancellationToken.ThrowIfCancellationRequested();
        a();
        await Task.Delay(1000);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task LocalFunction_UnusedToken_ShouldReportDiagnostic()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public void Outer()
    {
        async Task RunAsync(CancellationToken {|#0:cancellationToken|})
        {
            await Task.Delay(1000);
        }

        _ = RunAsync(default);
    }
}";

        var expected = VerifyCS.Diagnostic("CC016").WithLocation(0).WithArguments("cancellationToken");
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }
}
