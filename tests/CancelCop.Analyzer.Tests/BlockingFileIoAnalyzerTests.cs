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
