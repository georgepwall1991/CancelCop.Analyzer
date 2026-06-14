using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    CancelCop.Analyzer.UndisposedTokenSourceAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace CancelCop.Analyzer.Tests;

public class UndisposedTokenSourceAnalyzerTests
{
    [Fact]
    public async Task LocalSource_NeverDisposed_ShouldReportDiagnostic()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(CancellationToken outer)
    {
        var {|#0:cts|} = new CancellationTokenSource();
        await Task.Delay(1000, cts.Token);
    }
}";

        var expected = VerifyCS.Diagnostic("CC014").WithLocation(0).WithArguments("cts");
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task TargetTypedNew_NeverDisposed_ShouldReportDiagnostic()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(CancellationToken outer)
    {
        CancellationTokenSource {|#0:cts|} = new();
        await Task.Delay(1000, cts.Token);
    }
}";

        var expected = VerifyCS.Diagnostic("CC014").WithLocation(0).WithArguments("cts");
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task UsingDeclaration_ShouldNotReportDiagnostic()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(CancellationToken outer)
    {
        using var cts = new CancellationTokenSource();
        await Task.Delay(1000, cts.Token);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task Disposed_ShouldNotReportDiagnostic()
    {
        var test = @"
using System.Threading;

public class TestClass
{
    public void Run()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel();
        cts.Dispose();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task DisposedViaNullConditional_ShouldNotReportDiagnostic()
    {
        var test = @"
using System.Threading;

public class TestClass
{
    public void Run()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel();
        cts?.Dispose();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task Returned_ShouldNotReportDiagnostic()
    {
        var test = @"
using System.Threading;

public class TestClass
{
    public CancellationTokenSource Create()
    {
        var cts = new CancellationTokenSource();
        return cts;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AssignedToField_ShouldNotReportDiagnostic()
    {
        // A CTS stored in a field escapes the method's scope — ownership (and disposal) moves to the
        // instance, so CC014 must not flag the local.
        var test = @"
using System.Threading;

public class TestClass
{
    private CancellationTokenSource _cts;

    public void Init()
    {
        var cts = new CancellationTokenSource();
        _cts = cts;
    }

    public void Dispose() => _cts.Dispose();
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task PassedAsArgument_ShouldNotReportDiagnostic()
    {
        var test = @"
using System.Threading;

public class TestClass
{
    public void Run()
    {
        var cts = new CancellationTokenSource();
        Use(cts);
    }

    private void Use(CancellationTokenSource source) { }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task CapturedByLambda_ShouldNotReportDiagnostic()
    {
        var test = @"
using System;
using System.Threading;

public class TestClass
{
    public void Run()
    {
        var cts = new CancellationTokenSource();
        Action a = () => cts.Cancel();
        a();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task LinkedSource_NeverDisposed_ShouldReportDiagnostic()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(CancellationToken outer)
    {
        var {|#0:cts|} = CancellationTokenSource.CreateLinkedTokenSource(outer);
        await Task.Delay(1000, cts.Token);
    }
}";

        var expected = VerifyCS.Diagnostic("CC014").WithLocation(0).WithArguments("cts");
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }
}
