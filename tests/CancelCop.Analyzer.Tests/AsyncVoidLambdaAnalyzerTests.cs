using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    CancelCop.Analyzer.AsyncVoidLambdaAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace CancelCop.Analyzer.Tests;

public class AsyncVoidLambdaAnalyzerTests
{
    [Fact]
    public async Task AsyncLambda_AssignedToAction_ShouldReportDiagnostic()
    {
        var test = @"
using System;
using System.Threading.Tasks;

public class TestClass
{
    public void Register()
    {
        Action run = {|#0:async|} () => await Task.Yield();
    }
}";

        var expected = VerifyCS.Diagnostic("CC024").WithLocation(0);
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task AsyncAnonymousMethod_AssignedToAction_ShouldReportDiagnostic()
    {
        var test = @"
using System;
using System.Threading.Tasks;

public class TestClass
{
    public void Register()
    {
        Action run = {|#0:async|} delegate { await Task.Yield(); };
    }
}";

        var expected = VerifyCS.Diagnostic("CC024").WithLocation(0);
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task AsyncLambda_AssignedToGenericAction_ShouldReportDiagnostic()
    {
        var test = @"
using System;
using System.Threading.Tasks;

public class TestClass
{
    public void Register()
    {
        Action<int> handler = {|#0:async|} x => await Task.Yield();
    }
}";

        var expected = VerifyCS.Diagnostic("CC024").WithLocation(0);
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task AsyncLambda_AssignedToCustomVoidDelegate_ShouldReportDiagnostic()
    {
        var test = @"
using System.Threading.Tasks;

public delegate void Work();

public class TestClass
{
    public void Register()
    {
        Work work = {|#0:async|} () => await Task.Yield();
    }
}";

        var expected = VerifyCS.Diagnostic("CC024").WithLocation(0);
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task AsyncLambda_AssignedToCustomEventHandlerDelegate_ShouldNotReportDiagnostic()
    {
        var test = @"
using System;
using System.Threading.Tasks;

public delegate void ChangedHandler(object sender, EventArgs args);

public class TestClass
{
    public void Register()
    {
        ChangedHandler handler = async (sender, args) => await Task.Yield();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsyncLambda_AssignedToFuncTask_ShouldNotReportDiagnostic()
    {
        var test = @"
using System;
using System.Threading.Tasks;

public class TestClass
{
    public void Register()
    {
        Func<Task> run = async () => await Task.Yield();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsyncLambda_InTaskRun_ShouldNotReportDiagnostic()
    {
        // Task.Run binds an async lambda to its Func<Task> overload, not Action — this extremely
        // common pattern must never be flagged.
        var test = @"
using System.Threading.Tasks;

public class TestClass
{
    public void Register()
    {
        Task.Run(async () => await Task.Yield());
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task SyncLambda_AssignedToAction_ShouldNotReportDiagnostic()
    {
        var test = @"
using System;

public class TestClass
{
    public void Register()
    {
        Action run = () => { };
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsyncLambda_AsEventHandler_ShouldNotReportDiagnostic()
    {
        var test = @"
using System;
using System.Threading.Tasks;

public class TestClass
{
    public void Register()
    {
        EventHandler handler = async (s, e) => await Task.Yield();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsyncLambda_PassedWhereActionExpected_ShouldReportDiagnostic()
    {
        var test = @"
using System;
using System.Threading.Tasks;

public class TestClass
{
    private void Run(Action action) => action();

    public void Register()
    {
        Run({|#0:async|} () => await Task.Yield());
    }
}";

        var expected = VerifyCS.Diagnostic("CC024").WithLocation(0);
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }
}
