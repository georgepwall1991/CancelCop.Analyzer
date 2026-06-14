using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace CancelCop.Analyzer.Tests;

public class BlockingFileIoAppendAllLinesTests
{
    [Fact]
    public async Task AppendAllLines_WithEnumerable_InAsyncMethod_ShouldReportDiagnostic()
    {
        // File.AppendAllLines(string, IEnumerable<string>) has a signature-compatible
        // AppendAllLinesAsync(string, IEnumerable<string>, token), so a blocking call is flagged.
        var test = @"
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(string path, IEnumerable<string> lines)
    {
        File.{|#0:AppendAllLines|}(path, lines);
        await Task.Yield();
    }
}";

        var t = new CSharpAnalyzerTest<BlockingFileIoAnalyzer, DefaultVerifier>
        {
            TestCode = test,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        t.ExpectedDiagnostics.Add(
            new DiagnosticResult("CC028", DiagnosticSeverity.Warning).WithLocation(0).WithArguments("AppendAllLines"));
        await t.RunAsync();
    }
}
