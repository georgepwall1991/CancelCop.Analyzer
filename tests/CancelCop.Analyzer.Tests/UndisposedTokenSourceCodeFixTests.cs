using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixVerifier<
    CancelCop.Analyzer.UndisposedTokenSourceAnalyzer,
    CancelCop.Analyzer.UndisposedTokenSourceCodeFixProvider,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace CancelCop.Analyzer.Tests;

public class UndisposedTokenSourceCodeFixTests
{
    [Fact]
    public async Task AddsUsingDeclaration()
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

        var fixedCode = @"
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

        var expected = VerifyCS.Diagnostic("CC014").WithLocation(0).WithArguments("cts");
        await VerifyCS.VerifyCodeFixAsync(test, expected, fixedCode);
    }

    [Fact]
    public async Task AddsUsingDeclaration_ForLinkedSource()
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

        var fixedCode = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(CancellationToken outer)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(outer);
        await Task.Delay(1000, cts.Token);
    }
}";

        var expected = VerifyCS.Diagnostic("CC014").WithLocation(0).WithArguments("cts");
        await VerifyCS.VerifyCodeFixAsync(test, expected, fixedCode);
    }
}
