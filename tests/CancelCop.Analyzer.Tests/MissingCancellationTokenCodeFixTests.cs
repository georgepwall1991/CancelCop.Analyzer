using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixVerifier<
    CancelCop.Analyzer.MissingCancellationTokenAnalyzer,
    CancelCop.Analyzer.MissingCancellationTokenCodeFixProvider,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace CancelCop.Analyzer.Tests;

public class MissingCancellationTokenCodeFixTests
{
    [Fact]
    public async Task PublicAsyncMethod_WithoutParameters_AddsDefaultCancellationToken()
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

        var fixedCode = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task ProcessDataAsync(CancellationToken cancellationToken = default)
    {
        await Task.Delay(100);
    }
}";

        var expected = VerifyCS.Diagnostic("CC001")
            .WithLocation(0)
            .WithArguments("ProcessDataAsync");

        await VerifyCS.VerifyCodeFixAsync(test, expected, fixedCode);
    }

    [Fact]
    public async Task PublicAsyncMethod_WithExistingParameters_AddsCancellationTokenAsLastParameter()
    {
        var test = @"
using System.Threading.Tasks;

public class TestClass
{
    public async Task {|#0:ProcessDataAsync|}(string name, int value)
    {
        await Task.Delay(100);
    }
}";

        var fixedCode = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task ProcessDataAsync(string name, int value, CancellationToken cancellationToken = default)
    {
        await Task.Delay(100);
    }
}";

        var expected = VerifyCS.Diagnostic("CC001")
            .WithLocation(0)
            .WithArguments("ProcessDataAsync");

        await VerifyCS.VerifyCodeFixAsync(test, expected, fixedCode);
    }

    [Fact]
    public async Task PublicAsyncMethodReturningTaskOfT_AddsCancellationToken()
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

        var fixedCode = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task<int> GetDataAsync(CancellationToken cancellationToken = default)
    {
        await Task.Delay(100);
        return 42;
    }
}";

        var expected = VerifyCS.Diagnostic("CC001")
            .WithLocation(0)
            .WithArguments("GetDataAsync");

        await VerifyCS.VerifyCodeFixAsync(test, expected, fixedCode);
    }

    [Fact]
    public async Task PublicAsyncMethod_WithUsingAlreadyPresent_DoesNotDuplicateUsing()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task {|#0:ProcessDataAsync|}()
    {
        await Task.Delay(100);
    }
}";

        var fixedCode = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task ProcessDataAsync(CancellationToken cancellationToken = default)
    {
        await Task.Delay(100);
    }
}";

        var expected = VerifyCS.Diagnostic("CC001")
            .WithLocation(0)
            .WithArguments("ProcessDataAsync");

        await VerifyCS.VerifyCodeFixAsync(test, expected, fixedCode);
    }

    [Fact]
    public async Task PublicAsyncMethod_WithTrailingParamsParameter_InsertsTokenBeforeParams()
    {
        var test = @"
using System.Threading.Tasks;

public class TestClass
{
    public async Task {|#0:ProcessAsync|}(params int[] values)
    {
        await Task.Delay(100);
    }
}";

        // The token must be inserted BEFORE the params parameter, otherwise the
        // fixed code does not compile (CS0231: a params parameter must be last).
        var fixedCode = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task ProcessAsync(CancellationToken cancellationToken = default, params int[] values)
    {
        await Task.Delay(100);
    }
}";

        var expected = VerifyCS.Diagnostic("CC001")
            .WithLocation(0)
            .WithArguments("ProcessAsync");

        await VerifyCS.VerifyCodeFixAsync(test, expected, fixedCode);
    }

    [Fact]
    public async Task PublicAsyncMethod_WithCollidingParameterName_UsesNonCollidingName()
    {
        var test = @"
using System.Threading.Tasks;

public class TestClass
{
    public async Task {|#0:ProcessAsync|}(string cancellationToken)
    {
        await Task.Delay(100);
    }
}";

        // A parameter already named 'cancellationToken' (of a different type) means
        // the fix must pick a non-colliding name, otherwise CS0100 (duplicate name).
        var fixedCode = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task ProcessAsync(string cancellationToken, CancellationToken ct = default)
    {
        await Task.Delay(100);
    }
}";

        var expected = VerifyCS.Diagnostic("CC001")
            .WithLocation(0)
            .WithArguments("ProcessAsync");

        await VerifyCS.VerifyCodeFixAsync(test, expected, fixedCode);
    }
}
