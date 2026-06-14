using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace CancelCop.Analyzer.Tests;

public class BlockingFileIoReadAllLinesTests
{
    [Fact]
    public async Task ReadAllLines_InAsyncMethod_ShouldReportDiagnostic()
    {
        // File.ReadAllLines(string) has a signature-compatible ReadAllLinesAsync(string, token), so a
        // blocking call inside async code is flagged.
        var test = @"
using System.IO;
using System.Threading.Tasks;

public class TestClass
{
    public async Task<string[]> RunAsync(string path)
    {
        var lines = File.{|#0:ReadAllLines|}(path);
        await Task.Yield();
        return lines;
    }
}";

        var t = new CSharpAnalyzerTest<BlockingFileIoAnalyzer, DefaultVerifier>
        {
            TestCode = test,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        t.ExpectedDiagnostics.Add(
            new DiagnosticResult("CC028", DiagnosticSeverity.Warning).WithLocation(0).WithArguments("ReadAllLines"));
        await t.RunAsync();
    }
}
