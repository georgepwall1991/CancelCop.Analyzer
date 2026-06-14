using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace CancelCop.Analyzer.Tests;

public class BlockingFileIoStreamWriterWriteLineTests
{
    [Fact]
    public async Task StreamWriterWriteLine_InAsyncMethod_ShouldReportDiagnostic()
    {
        // StreamWriter overrides WriteLine(string) and offers a signature-compatible WriteLineAsync(string),
        // so a blocking WriteLine inside async code is flagged.
        var test = @"
using System.IO;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(StreamWriter writer, string text)
    {
        writer.{|#0:WriteLine|}(text);
        await Task.Yield();
    }
}";

        var t = new CSharpAnalyzerTest<BlockingFileIoAnalyzer, DefaultVerifier>
        {
            TestCode = test,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        t.ExpectedDiagnostics.Add(
            new DiagnosticResult("CC028", DiagnosticSeverity.Warning).WithLocation(0).WithArguments("WriteLine"));
        await t.RunAsync();
    }
}
