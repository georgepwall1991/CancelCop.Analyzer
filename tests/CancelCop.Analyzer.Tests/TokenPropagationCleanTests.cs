using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    CancelCop.Analyzer.TokenPropagationAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace CancelCop.Analyzer.Tests;

public class TokenPropagationCleanTests
{
    [Fact]
    public async Task TokenForwardedToInnerCall_ShouldNotReportDiagnostic()
    {
        // CC002 flags an inner async call that drops an in-scope token. When the token is forwarded
        // (Task.Delay(delay, token)), propagation is correct and nothing is flagged.
        var test = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(100, cancellationToken);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task NestedCancellationTokenLookalike_ShouldNotReportDiagnostic()
    {
        // A nested type reports its outer type's namespace, so Outer.CancellationToken inside a
        // System.Threading class shares the framework type's name AND namespace — but it is not a
        // CancellationToken. Treating the lookalike parameter as an in-scope token would report
        // CC002 and offer a fix that passes the wrong type.
        var test = @"
namespace System.Threading
{
    public class Outer
    {
        public struct CancellationToken { }
    }
}

public class TestClass
{
    public async System.Threading.Tasks.Task RunAsync(System.Threading.Outer.CancellationToken lookalike)
    {
        await System.Threading.Tasks.Task.Delay(100);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
