using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixVerifier<
    CancelCop.Analyzer.ParameterPositionAnalyzer,
    CancelCop.Analyzer.ParameterPositionCodeFixProvider,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace CancelCop.Analyzer.Tests;

public class ParameterPositionCodeFixTests
{
    [Fact]
    public async Task CancellationToken_NotLast_ShouldMoveToEnd()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task ProcessAsync({|#0:CancellationToken ct|}, string data)
    {
        await Task.Delay(1000, ct);
    }
}";

        var fixedCode = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task ProcessAsync(string data, CancellationToken ct)
    {
        await Task.Delay(1000, ct);
    }
}";

        var expected = VerifyCS.Diagnostic("CC006")
            .WithLocation(0)
            .WithArguments("ProcessAsync");

        await VerifyCS.VerifyCodeFixAsync(test, expected, fixedCode);
    }

    [Fact]
    public async Task CancellationToken_FirstOfThree_ShouldMoveToEnd()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task ProcessAsync({|#0:CancellationToken ct|}, string data, int count)
    {
        await Task.Delay(1000, ct);
    }
}";

        var fixedCode = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task ProcessAsync(string data, int count, CancellationToken ct)
    {
        await Task.Delay(1000, ct);
    }
}";

        var expected = VerifyCS.Diagnostic("CC006")
            .WithLocation(0)
            .WithArguments("ProcessAsync");

        await VerifyCS.VerifyCodeFixAsync(test, expected, fixedCode);
    }

    [Fact]
    public async Task CancellationToken_MiddlePosition_ShouldMoveToEnd()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task ProcessAsync(string data, {|#0:CancellationToken ct|}, int count)
    {
        await Task.Delay(1000, ct);
    }
}";

        var fixedCode = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task ProcessAsync(string data, int count, CancellationToken ct)
    {
        await Task.Delay(1000, ct);
    }
}";

        var expected = VerifyCS.Diagnostic("CC006")
            .WithLocation(0)
            .WithArguments("ProcessAsync");

        await VerifyCS.VerifyCodeFixAsync(test, expected, fixedCode);
    }
}
