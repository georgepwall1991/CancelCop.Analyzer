using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace CancelCop.Analyzer.Tests;

public class AwaitUsingCodeFixTests
{
    private const string Resources = @"
public class AsyncResource : System.IDisposable, System.IAsyncDisposable
{
    public void Dispose() { }
    public System.Threading.Tasks.ValueTask DisposeAsync() => default;
}";

    private static CSharpCodeFixTest<AwaitUsingAnalyzer, AwaitUsingCodeFixProvider, DefaultVerifier> CreateTest(
        string testCode, string fixedCode, params DiagnosticResult[] expected)
    {
        var test = new CSharpCodeFixTest<AwaitUsingAnalyzer, AwaitUsingCodeFixProvider, DefaultVerifier>
        {
            TestCode = testCode,
            FixedCode = fixedCode,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        test.ExpectedDiagnostics.AddRange(expected);
        return test;
    }

    [Fact]
    public async Task UsingDeclaration_BecomesAwaitUsing()
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

        var fixedCode = @"
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync()
    {
        await using var x = new AsyncResource();
        await Task.Yield();
    }
}" + Resources;

        var expected = new DiagnosticResult("CC025", DiagnosticSeverity.Info).WithLocation(0);
        await CreateTest(test, fixedCode, expected).RunAsync();
    }
}
