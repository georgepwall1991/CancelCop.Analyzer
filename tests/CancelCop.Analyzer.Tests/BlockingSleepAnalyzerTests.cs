using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    CancelCop.Analyzer.BlockingSleepAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace CancelCop.Analyzer.Tests;

public class BlockingSleepAnalyzerTests
{
    [Fact]
    public async Task ThreadSleep_InAsyncMethod_ShouldReportDiagnostic()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(CancellationToken ct)
    {
        {|#0:Thread.Sleep(1000)|};
        await Task.Yield();
    }
}";

        var expected = VerifyCS.Diagnostic("CC013").WithLocation(0);
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task ThreadSleep_InAsyncMethodWithoutToken_ShouldReportDiagnostic()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync()
    {
        {|#0:Thread.Sleep(1000)|};
        await Task.Yield();
    }
}";

        var expected = VerifyCS.Diagnostic("CC013").WithLocation(0);
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task ThreadSleep_ViaStaticImport_ShouldReportDiagnostic()
    {
        // Symbol-resolved, not name-only on a member access: a bare Sleep(...) via `using static`
        // is still flagged.
        var test = @"
using static System.Threading.Thread;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync()
    {
        {|#0:Sleep(1000)|};
        await Task.Yield();
    }
}";

        var expected = VerifyCS.Diagnostic("CC013").WithLocation(0);
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task ThreadSleep_InSyncMethod_ShouldNotReportDiagnostic()
    {
        var test = @"
using System.Threading;

public class TestClass
{
    public void Run()
    {
        Thread.Sleep(1000);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ThreadSleep_InAsyncLambda_ShouldReportDiagnostic()
    {
        var test = @"
using System;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public void Register()
    {
        Func<Task> f = async () =>
        {
            {|#0:Thread.Sleep(1000)|};
            await Task.Yield();
        };
    }
}";

        var expected = VerifyCS.Diagnostic("CC013").WithLocation(0);
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task ThreadSleep_InAsyncAnonymousMethod_ShouldReportDiagnostic()
    {
        var test = @"
using System;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public void Register()
    {
        Func<Task> f = async delegate
        {
            {|#0:Thread.Sleep(1000)|};
            await Task.Yield();
        };
    }
}";

        var expected = VerifyCS.Diagnostic("CC013").WithLocation(0);
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task ThreadSleep_InSyncLambdaInsideAsyncMethod_ShouldNotReportDiagnostic()
    {
        var test = @"
using System;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync()
    {
        Action a = () => Thread.Sleep(1000);
        a();
        await Task.Yield();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
