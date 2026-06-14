using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace CancelCop.Analyzer.Tests;

public class EnumeratorCancellationAnalyzerTests
{
    private static CSharpAnalyzerTest<EnumeratorCancellationAnalyzer, DefaultVerifier> CreateTest(
        string testCode, params DiagnosticResult[] expected)
    {
        var test = new CSharpAnalyzerTest<EnumeratorCancellationAnalyzer, DefaultVerifier>
        {
            TestCode = testCode,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        test.ExpectedDiagnostics.AddRange(expected);
        return test;
    }

    [Fact]
    public async Task AsyncIterator_WithUnmarkedToken_ShouldReportDiagnostic()
    {
        var test = @"
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async IAsyncEnumerable<int> ReadAsync(CancellationToken {|#0:token|})
    {
        yield return 1;
        await Task.CompletedTask;
    }
}";

        var expected = new DiagnosticResult("CC011", DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("token", "ReadAsync");

        await CreateTest(test, expected).RunAsync();
    }

    [Fact]
    public async Task AsyncIterator_WithMarkedToken_ShouldNotReportDiagnostic()
    {
        var test = @"
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async IAsyncEnumerable<int> ReadAsync([EnumeratorCancellation] CancellationToken token)
    {
        yield return 1;
        await Task.CompletedTask;
    }
}";

        await CreateTest(test).RunAsync();
    }

    [Fact]
    public async Task AsyncIterator_WithoutToken_ShouldNotReportDiagnostic()
    {
        var test = @"
using System.Collections.Generic;
using System.Threading.Tasks;

public class TestClass
{
    public async IAsyncEnumerable<int> ReadAsync()
    {
        yield return 1;
        await Task.CompletedTask;
    }
}";

        await CreateTest(test).RunAsync();
    }

    [Fact]
    public async Task YieldInNestedLocalFunction_OuterNotFlagged_ShouldNotReportDiagnostic()
    {
        // The outer method has a token parameter and returns IAsyncEnumerable, but the yield belongs
        // to a nested local-function iterator (whose token is marked), so neither is flagged.
        var test = @"
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public IAsyncEnumerable<int> Outer(CancellationToken token)
    {
        async IAsyncEnumerable<int> Inner([EnumeratorCancellation] CancellationToken t)
        {
            yield return 1;
            await Task.CompletedTask;
        }

        return Inner(token);
    }
}";

        await CreateTest(test).RunAsync();
    }

    [Fact]
    public async Task NonIteratorReturningAsyncEnumerable_ShouldNotReportDiagnostic()
    {
        var test = @"
using System.Collections.Generic;
using System.Threading;

public class TestClass
{
    public IAsyncEnumerable<int> ReadAsync(IAsyncEnumerable<int> source, CancellationToken token)
    {
        return source;
    }
}";

        await CreateTest(test).RunAsync();
    }

    [Fact]
    public async Task LocalFunctionAsyncIterator_WithUnmarkedToken_ShouldReportDiagnostic()
    {
        var test = @"
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public void Outer()
    {
        async IAsyncEnumerable<int> ReadAsync(CancellationToken {|#0:token|})
        {
            yield return 1;
            await Task.CompletedTask;
        }
    }
}";

        var expected = new DiagnosticResult("CC011", DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("token", "ReadAsync");

        await CreateTest(test, expected).RunAsync();
    }

    [Fact]
    public async Task AsyncIterator_WithSecondTokenMarked_ShouldNotReportDiagnostic()
    {
        var test = @"
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async IAsyncEnumerable<int> ReadAsync(CancellationToken outer, [EnumeratorCancellation] CancellationToken token)
    {
        yield return 1;
        await Task.CompletedTask;
    }
}";

        await CreateTest(test).RunAsync();
    }
}
