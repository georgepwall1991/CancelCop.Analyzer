using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace CancelCop.Analyzer.Tests;

public class BlockingFileIoReadAllBytesTests
{
    [Fact]
    public async Task ReadAllBytes_InAsyncMethod_ShouldReportDiagnostic()
    {
        // File.ReadAllBytes(string) has a signature-compatible ReadAllBytesAsync(string, token), so a
        // blocking call inside async code is flagged.
        var test = @"
using System.IO;
using System.Threading.Tasks;

public class TestClass
{
    public async Task<byte[]> RunAsync(string path)
    {
        var bytes = File.{|#0:ReadAllBytes|}(path);
        await Task.Yield();
        return bytes;
    }
}";

        var t = new CSharpAnalyzerTest<BlockingFileIoAnalyzer, DefaultVerifier>
        {
            TestCode = test,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        t.ExpectedDiagnostics.Add(
            new DiagnosticResult("CC028", DiagnosticSeverity.Warning).WithLocation(0).WithArguments("ReadAllBytes"));
        await t.RunAsync();
    }
}
