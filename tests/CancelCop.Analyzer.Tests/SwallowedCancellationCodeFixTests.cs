using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixVerifier<
    CancelCop.Analyzer.SwallowedCancellationAnalyzer,
    CancelCop.Analyzer.SwallowedCancellationCodeFixProvider,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace CancelCop.Analyzer.Tests;

public class SwallowedCancellationCodeFixTests
{
    private const string Harness = @"
using System;
using System.Threading.Tasks;

public class TestClass
{
    private Task DoAsync() => Task.CompletedTask;
";

    [Fact]
    public async Task NamedException_AddsRethrowGuard()
    {
        var test = Harness + @"
    public async Task RunAsync()
    {
        try { await DoAsync(); }
        {|#0:catch|} (Exception ex)
        {
            _ = ex;
        }
    }
}";

        var fixedCode = Harness + @"
    public async Task RunAsync()
    {
        try { await DoAsync(); }
        catch (Exception ex)
        {
            if (ex is OperationCanceledException)
                throw;
            _ = ex;
        }
    }
}";

        var expected = VerifyCS.Diagnostic("CC019").WithLocation(0);
        await VerifyCS.VerifyCodeFixAsync(test, expected, fixedCode);
    }

    [Fact]
    public async Task UnnamedException_AddsVariableAndRethrowGuard()
    {
        var test = Harness + @"
    public async Task RunAsync()
    {
        try { await DoAsync(); }
        {|#0:catch|} (Exception)
        {
        }
    }
}";

        var fixedCode = Harness + @"
    public async Task RunAsync()
    {
        try { await DoAsync(); }
        catch (Exception ex)
        {
            if (ex is OperationCanceledException)
                throw;
        }
    }
}";

        var expected = VerifyCS.Diagnostic("CC019").WithLocation(0);
        await VerifyCS.VerifyCodeFixAsync(test, expected, fixedCode);
    }
}
