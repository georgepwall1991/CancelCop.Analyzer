using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace CancelCop.Analyzer.Tests;

public class BlockingFileIoWriteAllLinesTests
{
    [Fact]
    public async Task WriteAllLines_WithEnumerable_InAsyncMethod_ShouldReportDiagnostic()
    {
        // File.WriteAllLines(string, IEnumerable<string>) has a signature-compatible
        // WriteAllLinesAsync(string, IEnumerable<string>, token), so a blocking call is flagged.
        var test = @"
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(string path, IEnumerable<string> lines)
    {
        File.{|#0:WriteAllLines|}(path, lines);
        await Task.Yield();
    }
}";

        var t = new CSharpAnalyzerTest<BlockingFileIoAnalyzer, DefaultVerifier>
        {
            TestCode = test,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        t.ExpectedDiagnostics.Add(
            new DiagnosticResult("CC028", DiagnosticSeverity.Warning).WithLocation(0).WithArguments("WriteAllLines"));
        await t.RunAsync();
    }
}
