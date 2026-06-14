using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace CancelCop.Analyzer.Tests;

public class BlockingFileIoCodeFixTests
{
    private static CSharpCodeFixTest<BlockingFileIoAnalyzer, BlockingFileIoCodeFixProvider, DefaultVerifier> CreateTest(
        string testCode, string fixedCode, params DiagnosticResult[] expected)
    {
        var test = new CSharpCodeFixTest<BlockingFileIoAnalyzer, BlockingFileIoCodeFixProvider, DefaultVerifier>
        {
            TestCode = testCode,
            FixedCode = fixedCode,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        test.ExpectedDiagnostics.AddRange(expected);
        return test;
    }

    [Fact]
    public async Task ReadAllText_WithToken_BecomesAwaitReadAllTextAsyncWithToken()
    {
        var test = @"
using System.IO;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task<string> RunAsync(string path, CancellationToken cancellationToken)
    {
        var text = File.{|#0:ReadAllText|}(path);
        await Task.Yield();
        return text;
    }
}";

        var fixedCode = @"
using System.IO;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task<string> RunAsync(string path, CancellationToken cancellationToken)
    {
        var text = await File.ReadAllTextAsync(path, cancellationToken);
        await Task.Yield();
        return text;
    }
}";

        var expected = new DiagnosticResult("CC028", DiagnosticSeverity.Warning)
            .WithLocation(0).WithArguments("ReadAllText");
        await CreateTest(test, fixedCode, expected).RunAsync();
    }

    [Fact]
    public async Task WriteAllText_WithoutTokenInScope_BecomesAwaitWriteAllTextAsync()
    {
        var test = @"
using System.IO;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync()
    {
        File.{|#0:WriteAllText|}(""a.txt"", ""data"");
        await Task.Yield();
    }
}";

        var fixedCode = @"
using System.IO;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync()
    {
        await File.WriteAllTextAsync(""a.txt"", ""data"");
        await Task.Yield();
    }
}";

        var expected = new DiagnosticResult("CC028", DiagnosticSeverity.Warning)
            .WithLocation(0).WithArguments("WriteAllText");
        await CreateTest(test, fixedCode, expected).RunAsync();
    }

    [Fact]
    public async Task NamedArgument_AddsTokenAsNamedArgument()
    {
        // A positional token after a named argument is a compile error (CS8323), so when the original
        // call uses a named argument the token must be added as `cancellationToken: token`.
        var test = @"
using System.IO;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task<string> RunAsync(string p, CancellationToken cancellationToken)
    {
        var text = File.{|#0:ReadAllText|}(path: p);
        await Task.Yield();
        return text;
    }
}";

        var fixedCode = @"
using System.IO;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task<string> RunAsync(string p, CancellationToken cancellationToken)
    {
        var text = await File.ReadAllTextAsync(path: p, cancellationToken: cancellationToken);
        await Task.Yield();
        return text;
    }
}";

        var expected = new DiagnosticResult("CC028", DiagnosticSeverity.Warning)
            .WithLocation(0).WithArguments("ReadAllText");
        await CreateTest(test, fixedCode, expected).RunAsync();
    }

    [Fact]
    public async Task StreamReaderReadToEnd_WithToken_BecomesAwaitReadToEndAsyncWithToken()
    {
        var test = @"
using System.IO;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task<string> RunAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var text = reader.{|#0:ReadToEnd|}();
        await Task.Yield();
        return text;
    }
}";

        var fixedCode = @"
using System.IO;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task<string> RunAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var text = await reader.ReadToEndAsync(cancellationToken);
        await Task.Yield();
        return text;
    }
}";

        var expected = new DiagnosticResult("CC028", DiagnosticSeverity.Warning)
            .WithLocation(0).WithArguments("ReadToEnd");
        await CreateTest(test, fixedCode, expected).RunAsync();
    }

    [Fact]
    public async Task StreamReaderReadLine_WithToken_BecomesAwaitReadLineAsyncWithToken()
    {
        var test = @"
using System.IO;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var line = reader.{|#0:ReadLine|}();
        _ = line;
        await Task.Yield();
    }
}";

        var fixedCode = @"
using System.IO;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var line = await reader.ReadLineAsync(cancellationToken);
        _ = line;
        await Task.Yield();
    }
}";

        var expected = new DiagnosticResult("CC028", DiagnosticSeverity.Warning)
            .WithLocation(0).WithArguments("ReadLine");
        await CreateTest(test, fixedCode, expected).RunAsync();
    }

    [Fact]
    public async Task AppendAllText_WithToken_BecomesAwaitAppendAllTextAsyncWithToken()
    {
        var test = @"
using System.IO;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(string path, CancellationToken cancellationToken)
    {
        File.{|#0:AppendAllText|}(path, ""line"");
        await Task.Yield();
    }
}";

        var fixedCode = @"
using System.IO;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(string path, CancellationToken cancellationToken)
    {
        await File.AppendAllTextAsync(path, ""line"", cancellationToken);
        await Task.Yield();
    }
}";

        var expected = new DiagnosticResult("CC028", DiagnosticSeverity.Warning)
            .WithLocation(0).WithArguments("AppendAllText");
        await CreateTest(test, fixedCode, expected).RunAsync();
    }

    [Fact]
    public async Task ResultUsedAsReceiver_ParenthesizesAwait()
    {
        // The blocking call is the receiver of `.Trim()`, so the await must be parenthesized:
        // File.ReadAllText(p).Trim() -> (await File.ReadAllTextAsync(p, token)).Trim().
        var test = @"
using System.IO;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task<string> RunAsync(string p, CancellationToken cancellationToken)
    {
        var text = File.{|#0:ReadAllText|}(p).Trim();
        await Task.Yield();
        return text;
    }
}";

        var fixedCode = @"
using System.IO;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task<string> RunAsync(string p, CancellationToken cancellationToken)
    {
        var text = (await File.ReadAllTextAsync(p, cancellationToken)).Trim();
        await Task.Yield();
        return text;
    }
}";

        var expected = new DiagnosticResult("CC028", DiagnosticSeverity.Warning)
            .WithLocation(0).WithArguments("ReadAllText");
        await CreateTest(test, fixedCode, expected).RunAsync();
    }

    [Fact]
    public async Task ResultUsedWithElementAccess_ParenthesizesAwait()
    {
        // File.ReadAllLines(p)[0] -> (await File.ReadAllLinesAsync(p, token))[0]
        var test = @"
using System.IO;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task<string> RunAsync(string p, CancellationToken cancellationToken)
    {
        var first = File.{|#0:ReadAllLines|}(p)[0];
        await Task.Yield();
        return first;
    }
}";

        var fixedCode = @"
using System.IO;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task<string> RunAsync(string p, CancellationToken cancellationToken)
    {
        var first = (await File.ReadAllLinesAsync(p, cancellationToken))[0];
        await Task.Yield();
        return first;
    }
}";

        var expected = new DiagnosticResult("CC028", DiagnosticSeverity.Warning)
            .WithLocation(0).WithArguments("ReadAllLines");
        await CreateTest(test, fixedCode, expected).RunAsync();
    }

    [Fact]
    public async Task ResultUsedWithConditionalAccess_ParenthesizesAwait()
    {
        // reader.ReadLine()?.Trim() -> (await reader.ReadLineAsync(token))?.Trim()
        var test = @"
using System.IO;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var t = reader.{|#0:ReadLine|}()?.Trim();
        _ = t;
        await Task.Yield();
    }
}";

        var fixedCode = @"
using System.IO;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var t = (await reader.ReadLineAsync(cancellationToken))?.Trim();
        _ = t;
        await Task.Yield();
    }
}";

        var expected = new DiagnosticResult("CC028", DiagnosticSeverity.Warning)
            .WithLocation(0).WithArguments("ReadLine");
        await CreateTest(test, fixedCode, expected).RunAsync();
    }

    [Fact]
    public async Task FixAll_MixedFileAndStreamReader_BothBecomeAsync()
    {
        // Fix All must rewrite across the generalized type map in one batch: a File helper and a
        // StreamReader read in the same method.
        var test = @"
using System.IO;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(string path, StreamReader reader, CancellationToken cancellationToken)
    {
        var a = File.{|#0:ReadAllText|}(path);
        var b = reader.{|#1:ReadToEnd|}();
        _ = a + b;
        await Task.Yield();
    }
}";

        var fixedCode = @"
using System.IO;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(string path, StreamReader reader, CancellationToken cancellationToken)
    {
        var a = await File.ReadAllTextAsync(path, cancellationToken);
        var b = await reader.ReadToEndAsync(cancellationToken);
        _ = a + b;
        await Task.Yield();
    }
}";

        await CreateTest(
            test,
            fixedCode,
            new DiagnosticResult("CC028", DiagnosticSeverity.Warning).WithLocation(0).WithArguments("ReadAllText"),
            new DiagnosticResult("CC028", DiagnosticSeverity.Warning).WithLocation(1).WithArguments("ReadToEnd")).RunAsync();
    }

    [Fact]
    public async Task FixAll_TwoBlockingCalls_BothBecomeAsync()
    {
        var test = @"
using System.IO;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(string path, CancellationToken cancellationToken)
    {
        var a = File.{|#0:ReadAllText|}(path);
        File.{|#1:WriteAllText|}(path, a);
        await Task.Yield();
    }
}";

        var fixedCode = @"
using System.IO;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(string path, CancellationToken cancellationToken)
    {
        var a = await File.ReadAllTextAsync(path, cancellationToken);
        await File.WriteAllTextAsync(path, a, cancellationToken);
        await Task.Yield();
    }
}";

        await CreateTest(
            test,
            fixedCode,
            new DiagnosticResult("CC028", DiagnosticSeverity.Warning).WithLocation(0).WithArguments("ReadAllText"),
            new DiagnosticResult("CC028", DiagnosticSeverity.Warning).WithLocation(1).WithArguments("WriteAllText")).RunAsync();
    }

    [Fact]
    public async Task StreamWriterFlush_WithToken_BecomesAwaitFlushAsyncWithToken()
    {
        // FlushAsync has a CancellationToken overload, so the in-scope token is flowed.
        var test = @"
using System.IO;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(StreamWriter writer, CancellationToken cancellationToken)
    {
        writer.{|#0:Flush|}();
        await Task.Yield();
    }
}";

        var fixedCode = @"
using System.IO;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(StreamWriter writer, CancellationToken cancellationToken)
    {
        await writer.FlushAsync(cancellationToken);
        await Task.Yield();
    }
}";

        var expected = new DiagnosticResult("CC028", DiagnosticSeverity.Warning)
            .WithLocation(0).WithArguments("Flush");
        await CreateTest(test, fixedCode, expected).RunAsync();
    }

    [Fact]
    public async Task StreamWriterWrite_TokenInScope_BecomesAwaitWriteAsyncWithoutToken()
    {
        // StreamWriter.WriteAsync(string) has no CancellationToken overload, so even with a token in
        // scope the fix must not add one — the analyzer's signature match reports takesToken = false.
        var test = @"
using System.IO;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(StreamWriter writer, string text, CancellationToken cancellationToken)
    {
        writer.{|#0:Write|}(text);
        await Task.Yield();
    }
}";

        var fixedCode = @"
using System.IO;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(StreamWriter writer, string text, CancellationToken cancellationToken)
    {
        await writer.WriteAsync(text);
        await Task.Yield();
    }
}";

        var expected = new DiagnosticResult("CC028", DiagnosticSeverity.Warning)
            .WithLocation(0).WithArguments("Write");
        await CreateTest(test, fixedCode, expected).RunAsync();
    }
}
