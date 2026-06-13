using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace CancelCop.Analyzer.Tests;

public class EnumeratorCancellationCodeFixTests
{
    private static CSharpCodeFixTest<EnumeratorCancellationAnalyzer, EnumeratorCancellationCodeFixProvider, DefaultVerifier> CreateTest(
        string testCode, string fixedCode, params DiagnosticResult[] expected)
    {
        var test = new CSharpCodeFixTest<EnumeratorCancellationAnalyzer, EnumeratorCancellationCodeFixProvider, DefaultVerifier>
        {
            TestCode = testCode,
            FixedCode = fixedCode,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        test.ExpectedDiagnostics.AddRange(expected);
        return test;
    }

    [Fact]
    public async Task AddsAttributeAndImport_WhenMissing()
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

        var fixedCode = @"
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

        var expected = new DiagnosticResult("CC011", DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("token", "ReadAsync");

        await CreateTest(test, fixedCode, expected).RunAsync();
    }

    [Fact]
    public async Task AddsAttribute_WhenImportAlreadyPresent()
    {
        var test = @"
using System.Collections.Generic;
using System.Runtime.CompilerServices;
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

        var fixedCode = @"
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

        var expected = new DiagnosticResult("CC011", DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("token", "ReadAsync");

        await CreateTest(test, fixedCode, expected).RunAsync();
    }
}
