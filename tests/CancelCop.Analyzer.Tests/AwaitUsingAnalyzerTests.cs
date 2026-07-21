using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace CancelCop.Analyzer.Tests;

public class AwaitUsingAnalyzerTests
{
    private const string Resources = @"
public class AsyncResource : System.IDisposable, System.IAsyncDisposable
{
    public void Dispose() { }
    public System.Threading.Tasks.ValueTask DisposeAsync() => default;
}

public class SyncResource : System.IDisposable
{
    public void Dispose() { }
}";

    private static CSharpAnalyzerTest<AwaitUsingAnalyzer, DefaultVerifier> CreateTest(
        string testCode, params DiagnosticResult[] expected)
    {
        var test = new CSharpAnalyzerTest<AwaitUsingAnalyzer, DefaultVerifier>
        {
            TestCode = testCode,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        test.ExpectedDiagnostics.AddRange(expected);
        return test;
    }

    [Fact]
    public async Task UsingDeclaration_InAsyncLocalFunction_ShouldReportDiagnostic()
    {
        // The async-context check covers nested async local functions, so a sync `using` over an
        // IAsyncDisposable inside one is flagged.
        var test = @"
using System.Threading.Tasks;

public class TestClass
{
    public void Configure()
    {
        async Task RunAsync()
        {
            {|#0:using|} var x = new AsyncResource();
            await Task.Yield();
        }

        _ = RunAsync();
    }
}" + Resources;

        var expected = new DiagnosticResult("CC025", DiagnosticSeverity.Info).WithLocation(0);
        await CreateTest(test, expected).RunAsync();
    }

    [Fact]
    public async Task UsingDeclaration_OverAsyncDisposable_ShouldReportDiagnostic()
    {
        var test = @"
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync()
    {
        {|#0:using|} var x = new AsyncResource();
        await Task.Yield();
    }
}" + Resources;

        var expected = new DiagnosticResult("CC025", DiagnosticSeverity.Info).WithLocation(0);
        await CreateTest(test, expected).RunAsync();
    }

    [Fact]
    public async Task UsingDeclaration_InTopLevelAsyncProgram_ShouldReportDiagnostic()
    {
        var testCode = @"
using System.Threading.Tasks;

{|#0:using|} var x = new AsyncResource();
await Task.Yield();" + Resources;

        var expected = new DiagnosticResult("CC025", DiagnosticSeverity.Info).WithLocation(0);
        var test = CreateTest(testCode, expected);
        test.TestState.OutputKind = OutputKind.ConsoleApplication;
        await test.RunAsync();
    }

    [Fact]
    public async Task UsingDeclaration_InTopLevelSyncProgram_ShouldNotReportDiagnostic()
    {
        var testCode = @"
using var x = new AsyncResource();" + Resources;

        var test = CreateTest(testCode);
        test.TestState.OutputKind = OutputKind.ConsoleApplication;
        await test.RunAsync();
    }

    [Fact]
    public async Task AwaitUsing_ShouldNotReportDiagnostic()
    {
        var test = @"
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync()
    {
        await using var x = new AsyncResource();
        await Task.Yield();
    }
}" + Resources;

        await CreateTest(test).RunAsync();
    }

    [Fact]
    public async Task UsingDeclaration_OverSyncDisposable_ShouldNotReportDiagnostic()
    {
        var test = @"
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync()
    {
        using var x = new SyncResource();
        await Task.Yield();
    }
}" + Resources;

        await CreateTest(test).RunAsync();
    }

    [Fact]
    public async Task UsingInSyncMethod_ShouldNotReportDiagnostic()
    {
        var test = @"
public class TestClass
{
    public void Run()
    {
        using var x = new AsyncResource();
    }
}" + Resources;

        await CreateTest(test).RunAsync();
    }

    [Fact]
    public async Task UsingStatement_OverAsyncDisposable_ShouldReportDiagnostic()
    {
        var test = @"
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync()
    {
        {|#0:using|} (new AsyncResource())
        {
            await Task.Yield();
        }
    }
}" + Resources;

        var expected = new DiagnosticResult("CC025", DiagnosticSeverity.Info).WithLocation(0);
        await CreateTest(test, expected).RunAsync();
    }
}
