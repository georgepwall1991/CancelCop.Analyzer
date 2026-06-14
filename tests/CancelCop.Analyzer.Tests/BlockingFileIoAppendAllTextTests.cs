using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace CancelCop.Analyzer.Tests;

public class BlockingFileIoAppendAllTextTests
{
    [Fact]
    public async Task AppendAllText_InAsyncMethod_ShouldReportDiagnostic()
    {
        // File.AppendAllText(string, string) has a signature-compatible AppendAllTextAsync(string,
        // string, token), so a blocking call inside async code is flagged.
        var test = @"
using System.IO;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(string path)
    {
        File.{|#0:AppendAllText|}(path, ""entry"");
        await Task.Yield();
    }
}";

        var t = new CSharpAnalyzerTest<BlockingFileIoAnalyzer, DefaultVerifier>
        {
            TestCode = test,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        t.ExpectedDiagnostics.Add(
            new DiagnosticResult("CC028", DiagnosticSeverity.Warning).WithLocation(0).WithArguments("AppendAllText"));
        await t.RunAsync();
    }
}
