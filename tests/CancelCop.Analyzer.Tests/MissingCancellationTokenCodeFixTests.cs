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

        var fixedCode = @"using System.Threading;

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

        var fixedCode = @"using System.Threading;

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

        var fixedCode = @"using System.Threading;

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
}
