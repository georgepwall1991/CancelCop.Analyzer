using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    CancelCop.Analyzer.ParameterPositionAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace CancelCop.Analyzer.Tests;

public class ParameterPositionConstructorTests
{
    [Fact]
    public async Task TokenLastInConstructor_ShouldNotReportDiagnostic()
    {
        // CC006 checks constructors too. A constructor whose CancellationToken is already the last
        // parameter satisfies the convention and must not be flagged.
        var test = @"
using System.Threading;

public class TestClass
{
    public TestClass(int value, CancellationToken cancellationToken)
    {
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
