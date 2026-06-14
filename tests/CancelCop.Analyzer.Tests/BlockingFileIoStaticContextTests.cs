using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace CancelCop.Analyzer.Tests;

public class BlockingFileIoStaticContextTests
{
    [Fact]
    public async Task ReadAllText_InStaticAsyncMethod_ShouldReportDiagnostic()
    {
        // CC028 keys off the async context via IsInAsyncFunction, not token capture, so a blocking
        // File.ReadAllText inside a static async method is flagged exactly like an instance one —
        // completing the blocking-in-async family's static-context coverage (CC013/CC015/CC026/CC028).
        var test = @"
using System.IO;
using System.Threading.Tasks;

public class TestClass
{
    public static async Task<string> RunAsync(string path)
    {
        var text = File.{|#0:ReadAllText|}(path);
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
            new DiagnosticResult("CC028", DiagnosticSeverity.Warning).WithLocation(0).WithArguments("ReadAllText"));
        await t.RunAsync();
    }
}
