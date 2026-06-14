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
}
