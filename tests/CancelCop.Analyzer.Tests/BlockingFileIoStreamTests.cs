using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace CancelCop.Analyzer.Tests;

/// <summary>
/// CC028 coverage for <c>System.IO.Stream</c> itself. The curated type map keys on the exact
/// declaring type name (<c>File</c>/<c>StreamReader</c>/<c>StreamWriter</c>), which silently
/// missed the stream primitives — <c>CopyTo</c>, <c>Read</c>, <c>Write</c>, <c>Flush</c> — even
/// though every one of them has a token-taking async counterpart.
/// </summary>
public class BlockingFileIoStreamTests
{
    private static CSharpAnalyzerTest<BlockingFileIoAnalyzer, DefaultVerifier> Test(
        string source
    ) => new() { TestCode = source, ReferenceAssemblies = ReferenceAssemblies.Net.Net90 };

    [Fact]
    public async Task StreamCopyTo_InAsyncMethod_ShouldReportDiagnostic()
    {
        // Stream.CopyTo(Stream) has CopyToAsync(Stream, CancellationToken) — a signature-compatible
        // counterpart — so blocking on it inside async code is exactly the CC028 shape.
        var test =
            @"
using System.IO;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(Stream source, Stream destination)
    {
        source.{|#0:CopyTo|}(destination);
        await Task.Yield();
    }
}";

        var t = Test(test);
        t.ExpectedDiagnostics.Add(
            new DiagnosticResult("CC028", DiagnosticSeverity.Warning)
                .WithLocation(0)
                .WithArguments("CopyTo")
        );
        await t.RunAsync();
    }

    [Fact]
    public async Task DerivedStreamRead_InAsyncMethod_ShouldReportDiagnostic()
    {
        // FileStream overrides Read, and ReadAsync is inherited from Stream, so both the type gate
        // and the counterpart lookup have to walk the inheritance chain.
        var test =
            @"
using System.IO;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(FileStream stream, byte[] buffer)
    {
        stream.{|#0:Read|}(buffer, 0, buffer.Length);
        await Task.Yield();
    }
}";

        var t = Test(test);
        t.ExpectedDiagnostics.Add(
            new DiagnosticResult("CC028", DiagnosticSeverity.Warning)
                .WithLocation(0)
                .WithArguments("Read")
        );
        await t.RunAsync();
    }

    [Fact]
    public async Task StreamFlush_InAsyncMethod_ShouldReportDiagnostic()
    {
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

        var t = Test(test);
        t.ExpectedDiagnostics.Add(
            new DiagnosticResult("CC028", DiagnosticSeverity.Warning)
                .WithLocation(0)
                .WithArguments("Flush")
        );
        await t.RunAsync();
    }

    [Fact]
    public async Task CustomStreamSubclass_InAsyncMethod_ShouldReportDiagnostic()
    {
        // A user-defined Stream lives outside System.IO, so the namespace gate alone would miss it.
        // What makes the call blocking is that it *is* a Stream, not where the subclass is declared.
        var test =
            @"
using System.IO;
using System.Threading.Tasks;

namespace Custom
{
    public class MyStream : CustomStreamBase { }

    public abstract class CustomStreamBase : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => 0;
        public override long Position { get => 0; set { } }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => 0;
        public override long Seek(long offset, SeekOrigin origin) => 0;
        public override void SetLength(long value) { }
        public override void Write(byte[] buffer, int offset, int count) { }
    }

    public class TestClass
    {
        public async Task RunAsync(MyStream stream, byte[] buffer)
        {
            stream.{|#0:Read|}(buffer, 0, buffer.Length);
            await Task.Yield();
        }
    }
}";

        var t = Test(test);
        t.ExpectedDiagnostics.Add(
            new DiagnosticResult("CC028", DiagnosticSeverity.Warning)
                .WithLocation(0)
                .WithArguments("Read")
        );
        await t.RunAsync();
    }

    [Fact]
    public async Task MemoryStreamRead_ShouldNotReportDiagnostic()
    {
        // A MemoryStream is backed by a byte[]; its "blocking" read never leaves the CPU, and
        // ReadAsync just wraps the same synchronous work in a completed task. Flagging it would be
        // pure noise, so MemoryStream and its subclasses are excluded.
        var test =
            @"
using System.IO;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(MemoryStream stream, byte[] buffer)
    {
        stream.Read(buffer, 0, buffer.Length);
        await Task.Yield();
    }
}";

        await Test(test).RunAsync();
    }

    [Fact]
    public async Task MemoryStreamFlush_ShouldNotReportDiagnostic()
    {
        // MemoryStream does not override Flush, so the resolved symbol's declaring type is Stream.
        // The exclusion has to look at the receiver's own type, not just where the method is declared.
        var test =
            @"
using System.IO;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(MemoryStream stream)
    {
        stream.Flush();
        await Task.Yield();
    }
}";

        await Test(test).RunAsync();
    }

    [Fact]
    public async Task StreamRead_InSyncMethod_ShouldNotReportDiagnostic()
    {
        var test =
            @"
using System.IO;

public class TestClass
{
    public int Run(Stream stream, byte[] buffer)
    {
        return stream.Read(buffer, 0, buffer.Length);
    }
}";

        await Test(test).RunAsync();
    }

    [Fact]
    public async Task StreamSpanRead_ShouldNotReportDiagnostic()
    {
        // Read(Span<byte>) has no signature-compatible counterpart — ReadAsync takes Memory<byte> —
        // so rewriting it would not compile. CC028 stays quiet rather than emit a broken fix.
        var test =
            @"
using System;
using System.IO;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(Stream stream, byte[] buffer)
    {
        stream.Read(buffer.AsSpan());
        await Task.Yield();
    }
}";

        await Test(test).RunAsync();
    }

    [Fact]
    public async Task NonStreamLookalike_ShouldNotReportDiagnostic()
    {
        // Same method names, unrelated type. CC028 is symbol-gated, not name-gated.
        var test =
            @"
using System.Threading.Tasks;

public class Pipe
{
    public void CopyTo(Pipe other) { }
    public Task CopyToAsync(Pipe other) => Task.CompletedTask;
}

public class TestClass
{
    public async Task RunAsync(Pipe a, Pipe b)
    {
        a.CopyTo(b);
        await Task.Yield();
    }
}";

        await Test(test).RunAsync();
    }
}
