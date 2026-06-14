using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace CancelCop.Analyzer.Tests;

public class BlockingSemaphoreWaitAsyncCleanTests
{
    [Fact]
    public async Task AwaitWaitAsync_ShouldNotReportDiagnostic()
    {
        // CC026 flags the synchronous Wait(). Awaiting WaitAsync(token) is the recommended async shape
        // and must not be flagged.
        var test = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(SemaphoreSlim gate, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
    }
}";

        var t = new CSharpAnalyzerTest<BlockingSemaphoreAnalyzer, DefaultVerifier>
        {
            TestCode = test,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        await t.RunAsync();
    }
}
