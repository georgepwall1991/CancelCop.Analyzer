using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    CancelCop.Analyzer.ReturnedTaskUsingDisposedAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace CancelCop.Analyzer.Tests;

public class ReturnedTaskUsingDisposedAnalyzerTests
{
    private const string Resource = @"
public sealed class Resource : System.IDisposable
{
    public void Dispose() { }
    public System.Threading.Tasks.Task<int> DoAsync() => System.Threading.Tasks.Task.FromResult(0);
    public int Value => 0;
}";

    [Fact]
    public async Task ReturnTaskFromUsingResource_ShouldReportDiagnostic()
    {
        var test = @"
using System.Threading.Tasks;

public class TestClass
{
    public Task<int> ReadAsync()
    {
        using var resource = new Resource();
        return {|#0:resource.DoAsync()|};
    }
}" + Resource;

        var expected = VerifyCS.Diagnostic("CC027").WithLocation(0).WithArguments("resource");
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task ReturnTaskFromUsingStatementResource_ShouldReportDiagnostic()
    {
        var test = @"
using System.Threading.Tasks;

public class TestClass
{
    public Task<int> ReadAsync()
    {
        using (var resource = new Resource())
        {
            return {|#0:resource.DoAsync()|};
        }
    }
}" + Resource;

        var expected = VerifyCS.Diagnostic("CC027").WithLocation(0).WithArguments("resource");
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task ReturnCompletedTaskReadingResource_ShouldNotReportDiagnostic()
    {
        var test = @"
using System.Threading.Tasks;

public class TestClass
{
    public Task<int> ReadAsync()
    {
        using var resource = new Resource();
        return Task.FromResult(resource.Value);
    }
}" + Resource;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ReturnHelperTask_ResourceReadIntoArgument_ShouldNotReportDiagnostic()
    {
        // The returned task is produced by a helper (the receiver is the helper, not the using
        // resource); the resource is only read synchronously into an argument before disposal, so there
        // is no premature-disposal bug and CC027 must stay quiet — only the receiver case is flagged.
        var test = @"
using System.Threading.Tasks;

public class TestClass
{
    private Task<int> Compute(int v) => Task.FromResult(v);

    public Task<int> ReadAsync()
    {
        using var resource = new Resource();
        return Compute(resource.Value);
    }
}" + Resource;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ReturnAliasedTaskLocal_ShouldNotReportDiagnostic()
    {
        // CC027 deliberately flags only the direct-receiver case (return resource.DoAsync();). An
        // aliased task local is a precision boundary — not flagged, to stay high-confidence.
        var test = @"
using System.Threading.Tasks;

public class TestClass
{
    public Task<int> ReadAsync()
    {
        using var resource = new Resource();
        var task = resource.DoAsync();
        return task;
    }
}" + Resource;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ReturnTaskFromNonUsingResource_ShouldNotReportDiagnostic()
    {
        var test = @"
using System.Threading.Tasks;

public class TestClass
{
    public Task<int> ReadAsync()
    {
        var resource = new Resource();
        return resource.DoAsync();
    }
}" + Resource;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task UsingStatementWithExpression_NoVariable_ShouldNotReportDiagnostic()
    {
        // A `using (new Resource())` statement declares no variable to reference, so the returned
        // task (which uses something else) is not flagged.
        var test = @"
using System.Threading.Tasks;

public class TestClass
{
    private Task<int> OtherAsync() => Task.FromResult(0);

    public Task<int> ReadAsync()
    {
        using (new Resource())
        {
            return OtherAsync();
        }
    }
}" + Resource;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsyncMethod_AwaitsResource_ShouldNotReportDiagnostic()
    {
        var test = @"
using System.Threading.Tasks;

public class TestClass
{
    public async Task<int> ReadAsync()
    {
        using var resource = new Resource();
        return await resource.DoAsync();
    }
}" + Resource;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ReturnUnrelatedTask_ShouldNotReportDiagnostic()
    {
        var test = @"
using System.Threading.Tasks;

public class TestClass
{
    public Task<int> ReadAsync()
    {
        using var resource = new Resource();
        resource.DoAsync();
        return Task.FromResult(0);
    }
}" + Resource;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
