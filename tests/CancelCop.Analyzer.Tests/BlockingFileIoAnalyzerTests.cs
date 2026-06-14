using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace CancelCop.Analyzer.Tests;

public class BlockingFileIoAnalyzerTests
{
    private static CSharpAnalyzerTest<BlockingFileIoAnalyzer, DefaultVerifier> CreateTest(
        string testCode, params DiagnosticResult[] expected)
    {
        var test = new CSharpAnalyzerTest<BlockingFileIoAnalyzer, DefaultVerifier>
        {
            TestCode = testCode,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        test.ExpectedDiagnostics.AddRange(expected);
        return test;
    }

    [Fact]
    public async Task ReadAllText_InAsyncMethod_ShouldReportDiagnostic()
    {
        var test = @"
using System.IO;
using System.Threading.Tasks;

public class TestClass
{
    public async Task<string> RunAsync(string path)
    {
        var text = File.{|#0:ReadAllText|}(path);
        await Task.Yield();
        return text;
    }
}";

        var expected = new DiagnosticResult("CC028", DiagnosticSeverity.Warning)
            .WithLocation(0).WithArguments("ReadAllText");
        await CreateTest(test, expected).RunAsync();
    }

    [Fact]
    public async Task WriteAllText_InAsyncLambda_ShouldReportDiagnostic()
    {
        var test = @"
using System;
using System.IO;
using System.Threading.Tasks;

public class TestClass
{
    public void Configure()
    {
        Func<Task> work = async () =>
        {
            File.{|#0:WriteAllText|}(""a.txt"", ""data"");
            await Task.Yield();
        };
        _ = work;
    }
}";

        var expected = new DiagnosticResult("CC028", DiagnosticSeverity.Warning)
            .WithLocation(0).WithArguments("WriteAllText");
        await CreateTest(test, expected).RunAsync();
    }

    [Fact]
    public async Task ReadAllText_InAsyncLocalFunction_ShouldReportDiagnostic()
    {
        // The async-context gate covers nested async functions, so a blocking File call inside an
        // async local function is flagged.
        var test = @"
using System.IO;
using System.Threading.Tasks;

public class TestClass
{
    public void Configure(string path)
    {
        async Task LoadAsync()
        {
            var text = File.{|#0:ReadAllText|}(path);
            _ = text;
            await Task.Yield();
        }

        _ = LoadAsync();
    }
}";

        var expected = new DiagnosticResult("CC028", DiagnosticSeverity.Warning)
            .WithLocation(0).WithArguments("ReadAllText");
        await CreateTest(test, expected).RunAsync();
    }

    [Fact]
    public async Task ReadAllText_InSyncMethod_ShouldNotReportDiagnostic()
    {
        // The blocking-in-async family only fires inside async code.
        var test = @"
using System.IO;

public class TestClass
{
    public string Run(string path)
    {
        return File.ReadAllText(path);
    }
}";

        await CreateTest(test).RunAsync();
    }

    [Fact]
    public async Task AsyncCounterpart_ShouldNotReportDiagnostic()
    {
        var test = @"
using System.IO;
using System.Threading.Tasks;

public class TestClass
{
    public async Task<string> RunAsync(string path)
    {
        return await File.ReadAllTextAsync(path);
    }
}";

        await CreateTest(test).RunAsync();
    }

    [Fact]
    public async Task NonListedFileMethod_ShouldNotReportDiagnostic()
    {
        // File.Exists is non-blocking metadata access with no async counterpart; not flagged.
        var test = @"
using System.IO;
using System.Threading.Tasks;

public class TestClass
{
    public async Task<bool> RunAsync(string path)
    {
        await Task.Yield();
        return File.Exists(path);
    }
}";

        await CreateTest(test).RunAsync();
    }

    [Fact]
    public async Task StreamReaderReadToEnd_InAsyncMethod_ShouldReportDiagnostic()
    {
        var test = @"
using System.IO;
using System.Threading.Tasks;

public class TestClass
{
    public async Task<string> RunAsync(StreamReader reader)
    {
        var text = reader.{|#0:ReadToEnd|}();
        await Task.Yield();
        return text;
    }
}";

        var expected = new DiagnosticResult("CC028", DiagnosticSeverity.Warning)
            .WithLocation(0).WithArguments("ReadToEnd");
        await CreateTest(test, expected).RunAsync();
    }

    [Fact]
    public async Task StreamReaderReadLine_InSyncMethod_ShouldNotReportDiagnostic()
    {
        var test = @"
using System.IO;

public class TestClass
{
    public string Run(StreamReader reader)
    {
        return reader.ReadLine();
    }
}";

        await CreateTest(test).RunAsync();
    }

    [Fact]
    public async Task StreamReaderNonCuratedMethod_ShouldNotReportDiagnostic()
    {
        // Peek() is not in the curated set (and has no async counterpart), so it stays clean.
        var test = @"
using System.IO;
using System.Threading.Tasks;

public class TestClass
{
    public async Task<int> RunAsync(StreamReader reader)
    {
        await Task.Yield();
        return reader.Peek();
    }
}";

        await CreateTest(test).RunAsync();
    }

    [Fact]
    public async Task LookalikeStreamReaderType_ShouldNotReportDiagnostic()
    {
        // A user-defined StreamReader outside System.IO must not be flagged.
        var test = @"
using System.Threading.Tasks;

namespace MyIo
{
    public class StreamReader
    {
        public string ReadToEnd() => string.Empty;
    }
}

public class TestClass
{
    public async Task<string> RunAsync(MyIo.StreamReader reader)
    {
        var text = reader.ReadToEnd();
        await Task.Yield();
        return text;
    }
}";

        await CreateTest(test).RunAsync();
    }

    [Fact]
    public async Task StreamWriterWrite_InAsyncMethod_ShouldReportDiagnostic()
    {
        var test = @"
using System.IO;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(StreamWriter writer, string text)
    {
        writer.{|#0:Write|}(text);
        await Task.Yield();
    }
}";

        var expected = new DiagnosticResult("CC028", DiagnosticSeverity.Warning)
            .WithLocation(0).WithArguments("Write");
        await CreateTest(test, expected).RunAsync();
    }

    [Fact]
    public async Task StreamWriterFlush_InAsyncMethod_ShouldReportDiagnostic()
    {
        var test = @"
using System.IO;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(StreamWriter writer)
    {
        writer.{|#0:Flush|}();
        await Task.Yield();
    }
}";

        var expected = new DiagnosticResult("CC028", DiagnosticSeverity.Warning)
            .WithLocation(0).WithArguments("Flush");
        await CreateTest(test, expected).RunAsync();
    }

    [Fact]
    public async Task StreamWriterWriteLine_InAsyncMethod_ShouldReportDiagnostic()
    {
        // StreamWriter overrides WriteLine(string) (so the call's ContainingType is StreamWriter, not
        // TextWriter) and offers a signature-compatible WriteLineAsync(string), so it is flagged.
        var test = @"
using System.IO;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(StreamWriter writer, string text)
    {
        writer.{|#0:WriteLine|}(text);
        await Task.Yield();
    }
}";

        var expected = new DiagnosticResult("CC028", DiagnosticSeverity.Warning)
            .WithLocation(0).WithArguments("WriteLine");
        await CreateTest(test, expected).RunAsync();
    }

    [Fact]
    public async Task StringWriter_InAsyncMethod_ShouldNotReportDiagnostic()
    {
        // StringWriter is an in-memory TextWriter in System.IO — its async methods complete
        // synchronously, so there is no benefit to switching. It is deliberately NOT in the curated
        // map, so a StringWriter.Write/WriteLine must stay quiet even inside async code.
        var test = @"
using System.IO;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(StringWriter writer, string text)
    {
        writer.Write(text);
        writer.WriteLine(text);
        await Task.Yield();
    }
}";

        await CreateTest(test).RunAsync();
    }

    [Fact]
    public async Task StreamWriterWrite_InSyncMethod_ShouldNotReportDiagnostic()
    {
        var test = @"
using System.IO;

public class TestClass
{
    public void Run(StreamWriter writer, string text)
    {
        writer.Write(text);
    }
}";

        await CreateTest(test).RunAsync();
    }

    [Fact]
    public async Task StreamWriterOverloadWithoutSignatureMatchedAsync_ShouldNotReportDiagnostic()
    {
        // StreamWriter declares Write(ReadOnlySpan<char>), but its only span-shaped async overload is
        // WriteAsync(ReadOnlyMemory<char>, CancellationToken) — a different first-parameter type. A
        // name-only "WriteAsync" lookup would wrongly flag this and the fixer would emit a non-compiling
        // 'await writer.WriteAsync(span)'. The v1.27.0 parameter-signature match must keep it quiet.
        var test = @"
using System;
using System.IO;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(StreamWriter writer)
    {
        writer.Write(""abc"".AsSpan());
        await Task.Yield();
    }
}";

        await CreateTest(test).RunAsync();
    }

    [Fact]
    public async Task LookalikeFileType_ShouldNotReportDiagnostic()
    {
        // A user-defined 'File' type is not System.IO.File, so it must stay clean.
        var test = @"
using System.Threading.Tasks;

public static class File
{
    public static string ReadAllText(string path) => string.Empty;
}

public class TestClass
{
    public async Task<string> RunAsync(string path)
    {
        var text = File.ReadAllText(path);
        await Task.Yield();
        return text;
    }
}";

        await CreateTest(test).RunAsync();
    }
}
