using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace CancelCop.Analyzer.Tests;

public class BlockingFileIoStreamReaderStaticTests
{
    [Fact]
    public async Task ReadToEnd_InStaticAsyncMethod_ShouldReportDiagnostic()
    {
        // CC028 keys off the async context, not token capture, so a blocking StreamReader.ReadToEnd
        // inside a static async method is flagged exactly like an instance one.
        var test = @"
using System.IO;
using System.Threading.Tasks;

public class TestClass
{
    public static async Task<string> RunAsync(StreamReader reader)
    {
        var text = reader.{|#0:ReadToEnd|}();
        await Task.Yield();
        return text;
    }
}";

        var t = new CSharpAnalyzerTest<BlockingFileIoAnalyzer, DefaultVerifier>
        {
            TestCode = test,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        t.ExpectedDiagnostics.Add(
            new DiagnosticResult("CC028", DiagnosticSeverity.Warning).WithLocation(0).WithArguments("ReadToEnd"));
        await t.RunAsync();
    }
}
