using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace CancelCop.Analyzer.Tests;

/// <summary>
/// Pins the documented tokenless-async-iterator guided sequence: a public async iterator with no
/// token is reported by CC001 only (add a token); once it has an unmarked token, CC011 takes over
/// (add <c>[EnumeratorCancellation]</c>). The two never fire simultaneously.
/// </summary>
public class IteratorTokenCascadeTests
{
    private sealed class CC001AndCC011Test : CSharpAnalyzerTest<MissingCancellationTokenAnalyzer, DefaultVerifier>
    {
        protected override IEnumerable<DiagnosticAnalyzer> GetDiagnosticAnalyzers()
        {
            yield return new MissingCancellationTokenAnalyzer();
            yield return new EnumeratorCancellationAnalyzer();
        }
    }

    [Fact]
    public async Task TokenlessIterator_ReportsOnlyCC001()
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

        var verifier = new CC001AndCC011Test
        {
            TestCode = test,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        verifier.ExpectedDiagnostics.Add(
            new DiagnosticResult("CC001", DiagnosticSeverity.Warning).WithLocation(0).WithArguments("StreamAsync"));

        await verifier.RunAsync();
    }

    [Fact]
    public async Task IteratorWithUnmarkedToken_ReportsOnlyCC011()
    {
        var test = @"
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async IAsyncEnumerable<int> StreamAsync(CancellationToken {|#0:token|})
    {
        yield return 1;
        await Task.CompletedTask;
    }
}";

        var verifier = new CC001AndCC011Test
        {
            TestCode = test,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        verifier.ExpectedDiagnostics.Add(
            new DiagnosticResult("CC011", DiagnosticSeverity.Warning).WithLocation(0).WithArguments("token", "StreamAsync"));

        await verifier.RunAsync();
    }
}
