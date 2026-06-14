using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace CancelCop.Analyzer.Tests;

public class BlockingFileIoNonAsyncTaskTests
{
    [Fact]
    public async Task ReadAllText_InNonAsyncTaskReturningMethod_ShouldNotReportDiagnostic()
    {
        // CC028 fires only inside an async function (via IsInAsyncFunction). A Task-returning method
        // WITHOUT the async keyword is not an async context, so blocking File.ReadAllText is not flagged.
        var test = @"
using System.IO;
using System.Threading.Tasks;

public class TestClass
{
    public Task<string> RunAsync(string path)
    {
        return Task.FromResult(File.ReadAllText(path));
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
