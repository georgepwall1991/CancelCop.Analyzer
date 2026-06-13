using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace CancelCop.Analyzer.Tests;

public class MissingCancellationTokenAsyncIteratorTests
{
    private static CSharpAnalyzerTest<MissingCancellationTokenAnalyzer, DefaultVerifier> CreateTest(
        string testCode, params DiagnosticResult[] expected)
    {
        var test = new CSharpAnalyzerTest<MissingCancellationTokenAnalyzer, DefaultVerifier>
        {
            TestCode = testCode,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        test.ExpectedDiagnostics.AddRange(expected);
        return test;
    }

    [Fact]
    public async Task PublicAsyncIterator_WithoutToken_ShouldReportDiagnostic()
    {
        var test = @"
using System.Collections.Generic;
using System.Threading.Tasks;

public class TestClass
{
    public async IAsyncEnumerable<int> {|#0:StreamAsync|}()
    {
        yield return 1;
        await Task.CompletedTask;
    }
}";

        var expected = new DiagnosticResult("CC001", DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("StreamAsync");

        await CreateTest(test, expected).RunAsync();
    }

    [Fact]
    public async Task PublicAsyncIterator_WithToken_ShouldNotReportDiagnostic()
    {
        var test = @"
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async IAsyncEnumerable<int> StreamAsync([EnumeratorCancellation] CancellationToken token)
    {
        yield return 1;
        await Task.CompletedTask;
    }
}";

        await CreateTest(test).RunAsync();
    }

    [Fact]
    public async Task PrivateAsyncIterator_WithoutToken_ShouldNotReportDiagnostic()
    {
        var test = @"
using System.Collections.Generic;
using System.Threading.Tasks;

public class TestClass
{
    private async IAsyncEnumerable<int> StreamAsync()
    {
        yield return 1;
        await Task.CompletedTask;
    }
}";

        await CreateTest(test).RunAsync();
    }
}
