using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace CancelCop.Analyzer.Tests;

/// <summary>
/// Custom <c>Stream</c> subclasses can declare members that match the async counterpart's signature
/// without being usable as one. CC028's contract is that the rewrite it offers always compiles, so
/// these shapes must either stay quiet or report without a fix.
/// </summary>
public class BlockingFileIoStreamSubclassTests
{
    /// <summary>A minimal concrete Stream used as a base for the probe types.</summary>
    private const string StreamStub =
        @"
public abstract class TestStreamBase : Stream
{
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => 0;
    public override long Position { get => 0; set { } }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => 0;
    public override void SetLength(long value) { }
}";

    [Fact]
    public async Task NonAwaitableAsyncCounterpart_ShouldNotReportDiagnostic()
    {
        // The subclass declares a same-signature 'ReadAsync' that returns int. It matches by
        // parameters but is not awaitable, and it shadows Stream.ReadAsync at the call site — so
        // there is no async alternative to suggest and 'await' would fail with CS1061.
        var test =
            @"
using System.IO;
using System.Threading.Tasks;
"
            + StreamStub
            + @"

public class CustomStream : TestStreamBase
{
    public override int Read(byte[] buffer, int offset, int count) => 0;
    public override void Write(byte[] buffer, int offset, int count) { }
    public new int ReadAsync(byte[] buffer, int offset, int count) => 0;
}

public class TestClass
{
    public async Task<int> RunAsync(CustomStream stream, byte[] buffer)
    {
        var read = stream.Read(buffer, 0, buffer.Length);
        await Task.Yield();
        return read;
    }
}";

        var t = new CSharpAnalyzerTest<BlockingFileIoAnalyzer, DefaultVerifier>
        {
            TestCode = test,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        await t.RunAsync();
    }

    [Fact]
    public async Task RenamedOverrideParameters_WithNamedArguments_ReportsWithoutOfferingAFix()
    {
        // The override renames its parameters, so 'data:'/'start:' are valid on the blocking call
        // but not on the inherited Stream.ReadAsync ('buffer:'/'offset:'). Copying the argument list
        // would emit CS1739, so the diagnostic is reported with no fix attached — the call really is
        // blocking, the automated rewrite just isn't safe.
        //
        // FixedCode == TestCode asserts exactly that: the fix-all pass leaves the source untouched.
        var source =
            @"
using System.IO;
using System.Threading;
using System.Threading.Tasks;
"
            + StreamStub
            + @"

public class CustomStream : TestStreamBase
{
    public override int Read(byte[] data, int start, int length) => 0;
    public override void Write(byte[] data, int start, int length) { }
}

public class TestClass
{
    public async Task<int> RunAsync(CustomStream stream, byte[] bytes, CancellationToken token)
    {
        var read = stream.{|#0:Read|}(data: bytes, start: 0, length: bytes.Length);
        await Task.Yield();
        return read;
    }
}";

        var t = new CSharpCodeFixTest<
            BlockingFileIoAnalyzer,
            BlockingFileIoCodeFixProvider,
            DefaultVerifier
        >
        {
            TestCode = source,
            FixedCode = source,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        t.ExpectedDiagnostics.Add(
            new DiagnosticResult("CC028", DiagnosticSeverity.Warning)
                .WithLocation(0)
                .WithArguments("Read")
        );
        await t.RunAsync();
    }

    [Fact]
    public async Task RenamedOverrideParameters_WithPositionalArguments_IsFixedNormally()
    {
        // Same renamed override, but the call is positional — nothing depends on parameter names, so
        // the ordinary rewrite is safe and still offered.
        var test =
            @"
using System.IO;
using System.Threading;
using System.Threading.Tasks;
"
            + StreamStub
            + @"

public class CustomStream : TestStreamBase
{
    public override int Read(byte[] data, int start, int length) => 0;
    public override void Write(byte[] data, int start, int length) { }
}

public class TestClass
{
    public async Task<int> RunAsync(CustomStream stream, byte[] bytes, CancellationToken token)
    {
        var read = stream.{|#0:Read|}(bytes, 0, bytes.Length);
        await Task.Yield();
        return read;
    }
}";

        var fixedCode =
            @"
using System.IO;
using System.Threading;
using System.Threading.Tasks;
"
            + StreamStub
            + @"

public class CustomStream : TestStreamBase
{
    public override int Read(byte[] data, int start, int length) => 0;
    public override void Write(byte[] data, int start, int length) { }
}

public class TestClass
{
    public async Task<int> RunAsync(CustomStream stream, byte[] bytes, CancellationToken token)
    {
        var read = await stream.ReadAsync(bytes, 0, bytes.Length, token);
        await Task.Yield();
        return read;
    }
}";

        var t = new CSharpCodeFixTest<
            BlockingFileIoAnalyzer,
            BlockingFileIoCodeFixProvider,
            DefaultVerifier
        >
        {
            TestCode = test,
            FixedCode = fixedCode,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        t.ExpectedDiagnostics.Add(
            new DiagnosticResult("CC028", DiagnosticSeverity.Warning)
                .WithLocation(0)
                .WithArguments("Read")
        );
        await t.RunAsync();
    }

    [Fact]
    public async Task MatchingNamedArguments_AreFixedNormally()
    {
        // Named arguments that DO line up with the async counterpart must keep working — the guard
        // must not reject every named call.
        var test =
            @"
using System.IO;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(string path, string text, CancellationToken cancellationToken)
    {
        File.{|#0:WriteAllText|}(path: path, contents: text);
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
    public async Task RunAsync(string path, string text, CancellationToken cancellationToken)
    {
        await File.WriteAllTextAsync(path: path, contents: text, cancellationToken: cancellationToken);
        await Task.Yield();
    }
}";

        var t = new CSharpCodeFixTest<
            BlockingFileIoAnalyzer,
            BlockingFileIoCodeFixProvider,
            DefaultVerifier
        >
        {
            TestCode = test,
            FixedCode = fixedCode,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        t.ExpectedDiagnostics.Add(
            new DiagnosticResult("CC028", DiagnosticSeverity.Warning)
                .WithLocation(0)
                .WithArguments("WriteAllText")
        );
        await t.RunAsync();
    }
}
