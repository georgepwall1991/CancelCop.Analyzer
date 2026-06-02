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

    [Fact]
    public async Task TokenImmediatelyBeforeTrailingParams_ShouldNotReportDiagnostic()
    {
        // A 'params' parameter must stay last, so a token directly before it is already in its
        // best possible position and cannot be moved further right.
        var test = @"
using System.Threading;

public class TestClass
{
    public void Log(CancellationToken cancellationToken, params string[] messages)
    {
    }
}";

        await CreateTest(test).RunAsync();
    }

    [Fact]
    public async Task TokenBeforeParamsButNotAdjacent_ShouldReportDiagnostic()
    {
        // The token can still be moved to sit just before the trailing params, so it fires.
        var test = @"
using System.Threading;

public class TestClass
{
    public void Log({|#0:CancellationToken cancellationToken|}, int level, params string[] messages)
    {
    }
}";

        var expected = new DiagnosticResult("CC006", Microsoft.CodeAnalysis.DiagnosticSeverity.Info)
            .WithLocation(0)
            .WithArguments("Log");

        await CreateTest(test, expected).RunAsync();
    }

    [Fact]
    public async Task ExtensionMethodWithTokenReceiver_ShouldNotReportDiagnostic()
    {
        // The 'this' receiver of an extension method must be first; the token cannot be moved.
        var test = @"
using System.Threading;

public static class Extensions
{
    public static void Process(this CancellationToken cancellationToken, string name)
    {
    }
}";

        await CreateTest(test).RunAsync();
    }

    [Fact]
    public async Task ExtensionMethodWithNonReceiverToken_ShouldReportDiagnostic()
    {
        // The token is not the receiver here, so it can and should be moved last.
        var test = @"
using System.Threading;

public static class Extensions
{
    public static void Process(this string name, {|#0:CancellationToken cancellationToken|}, int value)
    {
    }
}";

        var expected = new DiagnosticResult("CC006", Microsoft.CodeAnalysis.DiagnosticSeverity.Info)
            .WithLocation(0)
            .WithArguments("Process");

        await CreateTest(test, expected).RunAsync();
    }

    [Fact]
    public async Task ExtensionMethodWithReceiverAndMovableToken_ShouldReportOnMovableToken()
    {
        // The 'this CancellationToken' receiver is exempt, but the second token can still be
        // moved last and must be reported.
        var test = @"
using System.Threading;

public static class Extensions
{
    public static void Process(this CancellationToken source, {|#0:CancellationToken cancellationToken|}, int value)
    {
    }
}";

        var expected = new DiagnosticResult("CC006", Microsoft.CodeAnalysis.DiagnosticSeverity.Info)
            .WithLocation(0)
            .WithArguments("Process");

        await CreateTest(test, expected).RunAsync();
    }

    [Fact]
    public async Task OverrideMethod_TokenNotLast_ShouldNotReportOnOverride()
    {
        // The override cannot reorder its parameters (must match the base), so CC006 must
        // only fire on the base declaration, not the override.
        var test = @"
using System.Threading;
using System.Threading.Tasks;

public abstract class BaseProcessor
{
    public abstract Task ProcessAsync({|#0:CancellationToken cancellationToken|}, string name);
}

public class Processor : BaseProcessor
{
    public override async Task ProcessAsync(CancellationToken cancellationToken, string name)
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
    public async Task ImplicitInterfaceImplementation_TokenNotLast_ShouldNotReportOnImplementation()
    {
        // The implementation cannot reorder its parameters (must match the interface), so
        // CC006 must only fire on the interface declaration, not the implementing method.
        var test = @"
using System.Threading;
using System.Threading.Tasks;

public interface IProcessor
{
    Task ProcessAsync({|#0:CancellationToken cancellationToken|}, string name);
}

public class Processor : IProcessor
{
    public async Task ProcessAsync(CancellationToken cancellationToken, string name)
    {
        await Task.Delay(100, cancellationToken);
    }
}";

        var expected = new DiagnosticResult("CC006", Microsoft.CodeAnalysis.DiagnosticSeverity.Info)
            .WithLocation(0)
            .WithArguments("ProcessAsync");

        await CreateTest(test, expected).RunAsync();
    }
}
