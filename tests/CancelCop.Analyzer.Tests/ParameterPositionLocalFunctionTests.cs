using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    CancelCop.Analyzer.ParameterPositionAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace CancelCop.Analyzer.Tests;

public class ParameterPositionLocalFunctionTests
{
    [Fact]
    public async Task TokenLastInLocalFunction_ShouldNotReportDiagnostic()
    {
        // CC006 checks local functions too. A local function whose CancellationToken is already last
        // satisfies the convention and must not be flagged.
        var test = @"
using System.Threading;

public class TestClass
{
    public void Configure()
    {
        void Local(int value, CancellationToken cancellationToken)
        {
        }

        Local(1, default);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
