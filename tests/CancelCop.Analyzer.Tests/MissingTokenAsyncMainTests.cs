using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    CancelCop.Analyzer.MissingCancellationTokenAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace CancelCop.Analyzer.Tests;

public class MissingTokenAsyncMainTests
{
    [Fact]
    public async Task AsyncMainEntryPoint_ShouldNotReportDiagnostic()
    {
        // CC001 exempts the program entry point: an `async Task Main` cannot take a caller-supplied token,
        // so it must not be flagged.
        var test = @"
using System.Threading.Tasks;

public class Program
{
    public static async Task Main()
    {
        await Task.Yield();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
