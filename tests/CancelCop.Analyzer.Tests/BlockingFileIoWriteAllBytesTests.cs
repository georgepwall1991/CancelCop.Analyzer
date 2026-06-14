using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace CancelCop.Analyzer.Tests;

public class BlockingFileIoWriteAllBytesTests
{
    [Fact]
    public async Task WriteAllBytes_InAsyncMethod_ShouldReportDiagnostic()
    {
        // File.WriteAllBytes(string, byte[]) has a signature-compatible WriteAllBytesAsync(string,
        // byte[], token), so a blocking call inside async code is flagged.
        var test = @"
using System.IO;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(string path, byte[] data)
    {
        File.{|#0:WriteAllBytes|}(path, data);
        await Task.Yield();
    }
}";

        var t = new CSharpAnalyzerTest<BlockingFileIoAnalyzer, DefaultVerifier>
        {
            TestCode = test,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        t.ExpectedDiagnostics.Add(
            new DiagnosticResult("CC028", DiagnosticSeverity.Warning).WithLocation(0).WithArguments("WriteAllBytes"));
        await t.RunAsync();
    }
}
