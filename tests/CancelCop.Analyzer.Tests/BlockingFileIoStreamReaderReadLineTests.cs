using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace CancelCop.Analyzer.Tests;

public class BlockingFileIoStreamReaderReadLineTests
{
    [Fact]
    public async Task StreamReaderReadLine_InAsyncMethod_ShouldReportDiagnostic()
    {
        // StreamReader.ReadLine() has a signature-compatible ReadLineAsync(), so a blocking call inside
        // async code is flagged.
        var test = @"
using System.IO;
using System.Threading.Tasks;

public class TestClass
{
    public async Task<string?> RunAsync(StreamReader reader)
    {
        var line = reader.{|#0:ReadLine|}();
        await Task.Yield();
        return line;
    }
}";

        var t = new CSharpAnalyzerTest<BlockingFileIoAnalyzer, DefaultVerifier>
        {
            TestCode = test,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        t.ExpectedDiagnostics.Add(
            new DiagnosticResult("CC028", DiagnosticSeverity.Warning).WithLocation(0).WithArguments("ReadLine"));
        await t.RunAsync();
    }
}
