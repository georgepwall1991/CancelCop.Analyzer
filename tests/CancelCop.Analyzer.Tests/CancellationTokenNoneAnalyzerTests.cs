using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    CancelCop.Analyzer.CancellationTokenNoneAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace CancelCop.Analyzer.Tests;

public class CancellationTokenNoneAnalyzerTests
{
    [Fact]
    public async Task CancellationTokenNone_WhenTokenAvailable_ShouldReportDiagnostic()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task ProcessAsync(CancellationToken ct)
    {
        await Task.Delay(1000, {|#0:CancellationToken.None|});
    }
}";

        var expected = VerifyCS.Diagnostic("CC007")
            .WithLocation(0)
            .WithArguments("ct");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task CancellationTokenNone_WhenNoTokenAvailable_ShouldNotReportDiagnostic()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task ProcessAsync()
    {
        await Task.Delay(1000, CancellationToken.None);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task DefaultCancellationToken_WhenTokenAvailable_ShouldReportDiagnostic()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task ProcessAsync(CancellationToken ct)
    {
        await Task.Delay(1000, {|#0:default(CancellationToken)|});
    }
}";

        var expected = VerifyCS.Diagnostic("CC007")
            .WithLocation(0)
            .WithArguments("ct");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task UsingActualToken_ShouldNotReportDiagnostic()
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
    public async Task LocalFunction_WithToken_CancellationTokenNone_ShouldReportDiagnostic()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task ProcessAsync()
    {
        async Task LocalAsync(CancellationToken ct)
        {
            await Task.Delay(1000, {|#0:CancellationToken.None|});
        }

        await LocalAsync(CancellationToken.None);
    }
}";

        var expected = VerifyCS.Diagnostic("CC007")
            .WithLocation(0)
            .WithArguments("ct");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task MultipleCancellationTokenNone_ShouldReportMultipleDiagnostics()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task ProcessAsync(CancellationToken ct)
    {
        await Task.Delay(500, {|#0:CancellationToken.None|});
        await Task.Delay(1000, {|#1:CancellationToken.None|});
    }
}";

        var expected1 = VerifyCS.Diagnostic("CC007")
            .WithLocation(0)
            .WithArguments("ct");

        var expected2 = VerifyCS.Diagnostic("CC007")
            .WithLocation(1)
            .WithArguments("ct");

        await VerifyCS.VerifyAnalyzerAsync(test, expected1, expected2);
    }

    [Fact]
    public async Task CancellationTokenNone_InConstructor_WhenTokenAvailable_ShouldReportDiagnostic()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    private readonly CancellationToken _token;

    public TestClass(CancellationToken ct)
    {
        // Some initialization that uses CancellationToken.None instead of ct
        var cts = CancellationTokenSource.CreateLinkedTokenSource({|#0:CancellationToken.None|});
    }
}";

        var expected = VerifyCS.Diagnostic("CC007")
            .WithLocation(0)
            .WithArguments("ct");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }
}
