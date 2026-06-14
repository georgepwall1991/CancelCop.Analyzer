using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace CancelCop.Analyzer.Tests;

/// <summary>
/// Pins the CC005C → CC002 guided sequence: a tokenless Minimal API handler lambda is reported by
/// CC005C only (the handler needs a token). CC002 does not fire yet because there is no in-scope
/// token to propagate — it only fires once the CC005C fix has added the parameter.
/// </summary>
public class MinimalApiPropagationCascadeTests
{
    private sealed class CC005CAndCC002Test : CSharpAnalyzerTest<MinimalApiAnalyzer, DefaultVerifier>
    {
        protected override IEnumerable<DiagnosticAnalyzer> GetDiagnosticAnalyzers()
        {
            yield return new MinimalApiAnalyzer();
            yield return new TokenPropagationAnalyzer();
        }
    }

    [Fact]
    public async Task TokenlessHandler_ReportsOnlyCC005C()
    {
        var test = @"
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;

namespace Microsoft.AspNetCore.Routing
{
    public interface IEndpointRouteBuilder { }
}

namespace Microsoft.AspNetCore.Builder
{
    public static class EndpointRouteBuilderExtensions
    {
        public static void MapGet(this Microsoft.AspNetCore.Routing.IEndpointRouteBuilder endpoints, string pattern, Delegate handler) { }
    }
}

public class TestClass
{
    private Task DoAsync() => Task.CompletedTask;
    private Task DoAsync(CancellationToken token) => Task.CompletedTask;

    public void Map(Microsoft.AspNetCore.Routing.IEndpointRouteBuilder app)
    {
        app.MapGet(""/"", {|#0:async () => await DoAsync()|});
    }
}";

        var verifier = new CC005CAndCC002Test
        {
            TestCode = test,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        verifier.ExpectedDiagnostics.Add(
            new DiagnosticResult("CC005C", DiagnosticSeverity.Warning).WithLocation(0).WithArguments("MapGet"));

        await verifier.RunAsync();
    }
}
