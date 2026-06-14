using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace CancelCop.Analyzer.Tests;

public class BlockingFileIoNonCuratedOpenReadTests
{
    [Fact]
    public async Task FileOpenRead_ShouldNotReportDiagnostic()
    {
        // File.OpenRead is not in CC028's curated read/write/append set (it returns a stream the caller
        // drives), so it must not be flagged.
        var test = @"
using System.IO;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(string path)
    {
        using var stream = File.OpenRead(path);
        await Task.Yield();
    }
}";

        var t = new CSharpAnalyzerTest<BlockingFileIoAnalyzer, DefaultVerifier>
        {
            TestCode = test,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        await t.RunAsync();
    }
}
