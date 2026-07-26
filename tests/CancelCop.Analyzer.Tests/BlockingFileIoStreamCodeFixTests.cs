using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace CancelCop.Analyzer.Tests;

/// <summary>
/// The CC028 fix is generic over the blocking call, so extending the rule to
/// <c>System.IO.Stream</c> must produce a rewrite that still compiles. These tests pin that:
/// the fixed code is compiled by the harness, so a bad overload choice fails here.
/// </summary>
public class BlockingFileIoStreamCodeFixTests
{
    private static CSharpCodeFixTest<
        BlockingFileIoAnalyzer,
        BlockingFileIoCodeFixProvider,
        DefaultVerifier
    > CreateTest(string testCode, string fixedCode, params DiagnosticResult[] expected)
    {
        var test = new CSharpCodeFixTest<
            BlockingFileIoAnalyzer,
            BlockingFileIoCodeFixProvider,
            DefaultVerifier
        >
        {
            TestCode = testCode,
            FixedCode = fixedCode,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        test.ExpectedDiagnostics.AddRange(expected);
        return test;
    }

    [Fact]
    public async Task StreamCopyTo_WithToken_BecomesAwaitCopyToAsyncWithToken()
    {
        var test =
            @"
using System.IO;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(Stream source, Stream destination, CancellationToken cancellationToken)
    {
        source.{|#0:CopyTo|}(destination);
        await Task.Yield();
    }
}";

        var fixedCode =
            @"
using System.IO;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(Stream source, Stream destination, CancellationToken cancellationToken)
    {
        await source.CopyToAsync(destination, cancellationToken);
        await Task.Yield();
    }
}";

        var expected = new DiagnosticResult("CC028", DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("CopyTo");
        await CreateTest(test, fixedCode, expected).RunAsync();
    }

    [Fact]
    public async Task StreamRead_WithToken_BecomesAwaitReadAsyncWithToken()
    {
        var test =
            @"
using System.IO;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task<int> RunAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var read = stream.{|#0:Read|}(buffer, 0, buffer.Length);
        await Task.Yield();
        return read;
    }
}";

        var fixedCode =
            @"
using System.IO;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task<int> RunAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var read = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
        await Task.Yield();
        return read;
    }
}";

        var expected = new DiagnosticResult("CC028", DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("Read");
        await CreateTest(test, fixedCode, expected).RunAsync();
    }

    [Fact]
    public async Task StreamFlush_WithoutTokenInScope_BecomesAwaitFlushAsync()
    {
        // No token in scope: the rewrite still has to compile, using the tokenless async form.
        var test =
            @"
using System.IO;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(Stream stream)
    {
        stream.{|#0:Flush|}();
        await Task.Yield();
    }
}";

        var fixedCode =
            @"
using System.IO;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(Stream stream)
    {
        await stream.FlushAsync();
        await Task.Yield();
    }
}";

        var expected = new DiagnosticResult("CC028", DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("Flush");
        await CreateTest(test, fixedCode, expected).RunAsync();
    }
}
