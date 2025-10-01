using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace CancelCop.Analyzer.Tests;

public class ParameterPositionAnalyzerTests
{
    private static CSharpAnalyzerTest<ParameterPositionAnalyzer, DefaultVerifier> CreateTest(string testCode, params DiagnosticResult[] expected)
    {
        var test = new CSharpAnalyzerTest<ParameterPositionAnalyzer, DefaultVerifier>
        {
            TestCode = testCode,
            ReferenceAssemblies = Microsoft.CodeAnalysis.Testing.ReferenceAssemblies.Net.Net90,
        };

        test.ExpectedDiagnostics.AddRange(expected);
        return test;
    }

    [Fact]
    public async Task CancellationToken_NotLastParameter_ShouldReportDiagnostic()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task ProcessAsync({|#0:CancellationToken cancellationToken|}, string name)
    {
        await Task.Delay(100, cancellationToken);
    }
}";

        var expected = new DiagnosticResult("CC006", Microsoft.CodeAnalysis.DiagnosticSeverity.Info)
            .WithLocation(0)
            .WithArguments("ProcessAsync");

        await CreateTest(test, expected).RunAsync();
    }

    [Fact]
    public async Task CancellationToken_LastParameter_ShouldNotReportDiagnostic()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task ProcessAsync(string name, CancellationToken cancellationToken)
    {
        await Task.Delay(100, cancellationToken);
    }
}";

        await CreateTest(test).RunAsync();
    }

    [Fact]
    public async Task CancellationToken_OnlyParameter_ShouldNotReportDiagnostic()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task ProcessAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(100, cancellationToken);
    }
}";

        await CreateTest(test).RunAsync();
    }

    [Fact]
    public async Task CancellationToken_MiddleParameter_ShouldReportDiagnostic()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task ProcessAsync(string firstName, {|#0:CancellationToken cancellationToken|}, string lastName)
    {
        await Task.Delay(100, cancellationToken);
    }
}";

        var expected = new DiagnosticResult("CC006", Microsoft.CodeAnalysis.DiagnosticSeverity.Info)
            .WithLocation(0)
            .WithArguments("ProcessAsync");

        await CreateTest(test, expected).RunAsync();
    }

    [Fact]
    public async Task SyncMethod_WithCancellationToken_NotLast_ShouldReportDiagnostic()
    {
        var test = @"
using System.Threading;

public class TestClass
{
    public void Process({|#0:CancellationToken cancellationToken|}, string name)
    {
        // Sync method
    }
}";

        var expected = new DiagnosticResult("CC006", Microsoft.CodeAnalysis.DiagnosticSeverity.Info)
            .WithLocation(0)
            .WithArguments("Process");

        await CreateTest(test, expected).RunAsync();
    }

    [Fact]
    public async Task ProtectedMethod_CancellationToken_NotLast_ShouldReportDiagnostic()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    protected async Task ProcessAsync({|#0:CancellationToken cancellationToken|}, int id)
    {
        await Task.Delay(100, cancellationToken);
    }
}";

        var expected = new DiagnosticResult("CC006", Microsoft.CodeAnalysis.DiagnosticSeverity.Info)
            .WithLocation(0)
            .WithArguments("ProcessAsync");

        await CreateTest(test, expected).RunAsync();
    }

    [Fact]
    public async Task PrivateMethod_CancellationToken_NotLast_ShouldNotReportDiagnostic()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    private async Task ProcessAsync(CancellationToken cancellationToken, int id)
    {
        await Task.Delay(100, cancellationToken);
    }
}";

        // Private methods are not checked by convention
        await CreateTest(test).RunAsync();
    }
}
