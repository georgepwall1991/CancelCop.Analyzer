using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixVerifier<
    CancelCop.Analyzer.AsyncVoidAnalyzer,
    CancelCop.Analyzer.AsyncVoidCodeFixProvider,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace CancelCop.Analyzer.Tests;

public class AsyncVoidCodeFixTests
{
    [Fact]
    public async Task AsyncVoid_BecomesAsyncTask_AndAddsImport()
    {
        var test = @"
using System;

public class TestClass
{
    public async void {|#0:ProcessAsync|}()
    {
        await System.Threading.Tasks.Task.CompletedTask;
    }
}";

        var fixedCode = @"
using System;
using System.Threading.Tasks;

public class TestClass
{
    public async Task ProcessAsync()
    {
        await System.Threading.Tasks.Task.CompletedTask;
    }
}";

        var expected = VerifyCS.Diagnostic("CC023").WithLocation(0).WithArguments("ProcessAsync");
        await VerifyCS.VerifyCodeFixAsync(test, expected, fixedCode);
    }
}
