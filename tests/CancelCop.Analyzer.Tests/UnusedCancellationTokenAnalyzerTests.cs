using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    CancelCop.Analyzer.UnusedCancellationTokenAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace CancelCop.Analyzer.Tests;

public class UnusedCancellationTokenAnalyzerTests
{
    [Fact]
    public async Task UnusedCancellationToken_ShouldReportDiagnostic()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task ProcessAsync({|#0:CancellationToken ct|})
    {
        await Task.Delay(1000);
    }
}";

        var expected = VerifyCS.Diagnostic("CC008")
            .WithLocation(0)
            .WithArguments("ct", "ProcessAsync");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task UsedCancellationToken_PassedToMethod_ShouldNotReportDiagnostic()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task ProcessAsync(CancellationToken ct)
    {
        await Task.Delay(1000, ct);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task UsedCancellationToken_ThrowIfCancellationRequested_ShouldNotReportDiagnostic()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task ProcessAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        await Task.Delay(1000);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task UsedCancellationToken_IsCancellationRequested_ShouldNotReportDiagnostic()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task ProcessAsync(CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
            return;
        await Task.Delay(1000);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task LocalFunction_UnusedCancellationToken_ShouldReportDiagnostic()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task ProcessAsync()
    {
        async Task LocalAsync({|#0:CancellationToken ct|})
        {
            await Task.Delay(1000);
        }

        await LocalAsync(CancellationToken.None);
    }
}";

        var expected = VerifyCS.Diagnostic("CC008")
            .WithLocation(0)
            .WithArguments("ct", "LocalAsync");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task ExpressionBodyMethod_UnusedToken_ShouldReportDiagnostic()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public Task ProcessAsync({|#0:CancellationToken ct|}) => Task.Delay(1000);
}";

        var expected = VerifyCS.Diagnostic("CC008")
            .WithLocation(0)
            .WithArguments("ct", "ProcessAsync");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task ExpressionBodyMethod_UsedToken_ShouldNotReportDiagnostic()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public Task ProcessAsync(CancellationToken ct) => Task.Delay(1000, ct);
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task CancellationToken_PassedToLinkedTokenSource_ShouldNotReportDiagnostic()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task ProcessAsync(CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        await Task.Delay(1000, cts.Token);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task MultipleCancellationTokens_OneUnused_ShouldReportDiagnostic()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task ProcessAsync(CancellationToken ct1, {|#0:CancellationToken ct2|})
    {
        await Task.Delay(1000, ct1);
    }
}";

        var expected = VerifyCS.Diagnostic("CC008")
            .WithLocation(0)
            .WithArguments("ct2", "ProcessAsync");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }
}
