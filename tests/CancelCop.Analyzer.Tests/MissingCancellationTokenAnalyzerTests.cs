using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    CancelCop.Analyzer.MissingCancellationTokenAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace CancelCop.Analyzer.Tests;

public class MissingCancellationTokenAnalyzerTests
{
    [Fact]
    public async Task PublicAsyncMethod_WithoutCancellationToken_ShouldReportDiagnostic()
    {
        var test = @"
using System.Threading.Tasks;

public class TestClass
{
    public async Task {|#0:ProcessDataAsync|}()
    {
        await Task.Delay(100);
    }
}";

        var expected = VerifyCS.Diagnostic("CC001")
            .WithLocation(0)
            .WithArguments("ProcessDataAsync");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task PublicAsyncMethod_WithCancellationToken_ShouldNotReportDiagnostic()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task ProcessDataAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(100, cancellationToken);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task PrivateAsyncMethod_WithoutCancellationToken_ShouldNotReportDiagnostic()
    {
        var test = @"
using System.Threading.Tasks;

public class TestClass
{
    private async Task ProcessDataAsync()
    {
        await Task.Delay(100);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task PublicAsyncMethodReturningTaskOfT_WithoutCancellationToken_ShouldReportDiagnostic()
    {
        var test = @"
using System.Threading.Tasks;

public class TestClass
{
    public async Task<int> {|#0:GetDataAsync|}()
    {
        await Task.Delay(100);
        return 42;
    }
}";

        var expected = VerifyCS.Diagnostic("CC001")
            .WithLocation(0)
            .WithArguments("GetDataAsync");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task PublicAsyncMethod_WithCancellationTokenDefaultValue_ShouldNotReportDiagnostic()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task ProcessDataAsync(CancellationToken cancellationToken = default)
    {
        await Task.Delay(100, cancellationToken);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ProtectedAsyncMethod_WithoutCancellationToken_ShouldReportDiagnostic()
    {
        var test = @"
using System.Threading.Tasks;

public class TestClass
{
    protected async Task {|#0:ProcessDataAsync|}()
    {
        await Task.Delay(100);
    }
}";

        var expected = VerifyCS.Diagnostic("CC001")
            .WithLocation(0)
            .WithArguments("ProcessDataAsync");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task PublicAsyncMethodReturningValueTask_WithoutCancellationToken_ShouldReportDiagnostic()
    {
        var test = @"
using System.Threading.Tasks;

public class TestClass
{
    public async ValueTask {|#0:ProcessDataAsync|}()
    {
        await Task.Delay(100);
    }
}";

        var expected = VerifyCS.Diagnostic("CC001")
            .WithLocation(0)
            .WithArguments("ProcessDataAsync");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task PublicAsyncMethodReturningValueTaskOfT_WithoutCancellationToken_ShouldReportDiagnostic()
    {
        var test = @"
using System.Threading.Tasks;

public class TestClass
{
    public async ValueTask<int> {|#0:GetDataAsync|}()
    {
        await Task.Delay(100);
        return 42;
    }
}";

        var expected = VerifyCS.Diagnostic("CC001")
            .WithLocation(0)
            .WithArguments("GetDataAsync");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task PublicAsyncMethodReturningValueTask_WithCancellationToken_ShouldNotReportDiagnostic()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async ValueTask ProcessDataAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(100, cancellationToken);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
