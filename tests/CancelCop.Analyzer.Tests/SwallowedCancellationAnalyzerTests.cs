using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    CancelCop.Analyzer.SwallowedCancellationAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace CancelCop.Analyzer.Tests;

public class SwallowedCancellationAnalyzerTests
{
    private const string Harness = @"
using System;
using System.Threading.Tasks;

public class TestClass
{
    private Task DoAsync() => Task.CompletedTask;
    private void DoSync() { }
";

    [Fact]
    public async Task CatchException_OverAwait_NoRethrow_ShouldReportDiagnostic()
    {
        var test = Harness + @"
    public async Task RunAsync()
    {
        try { await DoAsync(); }
        {|#0:catch|} (Exception) { }
    }
}";

        var expected = VerifyCS.Diagnostic("CC019").WithLocation(0);
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task CatchAll_OverAwait_ShouldReportDiagnostic()
    {
        var test = Harness + @"
    public async Task RunAsync()
    {
        try { await DoAsync(); }
        {|#0:catch|} { }
    }
}";

        var expected = VerifyCS.Diagnostic("CC019").WithLocation(0);
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task CatchException_Rethrows_ShouldNotReportDiagnostic()
    {
        var test = Harness + @"
    public async Task RunAsync()
    {
        try { await DoAsync(); }
        catch (Exception) { throw; }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task CatchException_WithFilter_ShouldNotReportDiagnostic()
    {
        var test = Harness + @"
    public async Task RunAsync()
    {
        try { await DoAsync(); }
        catch (Exception ex) when (ex is not OperationCanceledException) { }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task CatchSpecificException_ShouldNotReportDiagnostic()
    {
        var test = Harness + @"
    public async Task RunAsync()
    {
        try { await DoAsync(); }
        catch (System.IO.IOException) { }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task CatchException_NoAwaitInTry_ShouldNotReportDiagnostic()
    {
        var test = Harness + @"
    public void Run()
    {
        try { DoSync(); }
        catch (Exception) { }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task CatchOperationCanceled_ShouldNotReportDiagnostic()
    {
        var test = Harness + @"
    public async Task RunAsync()
    {
        try { await DoAsync(); }
        catch (OperationCanceledException) { }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
