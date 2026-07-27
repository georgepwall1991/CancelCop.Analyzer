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

    [Fact]
    public async Task NonAwaitableAsyncOnSubclass_WithInheritedSyncMember_ShouldNotReportDiagnostic()
    {
        // CopyTo is inherited, so the *declaring* type is Stream — but the rewrite binds from the
        // receiver's type, where the subclass's own non-awaitable CopyToAsync wins. Resolution has to
        // start at the receiver, not the declaring type, or this emits `await ...` on an int (CS1061).
        var test =
            @"
using System.IO;
using System.Threading.Tasks;
" + StreamStub + @"

public class CustomStream : TestStreamBase
{
    public override int Read(byte[] buffer, int offset, int count) => 0;
    public override void Write(byte[] buffer, int offset, int count) { }
    public new int CopyToAsync(Stream destination) => 0;
}

public class TestClass
{
    public async Task RunAsync(CustomStream source, Stream destination)
    {
        source.CopyTo(destination);
        await Task.Yield();
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
    public async Task TypeIncompatibleSameArityOverload_StillReportsDiagnostic()
    {
        // The subclass declares `int ReadAsync(string, int, int)` — same arity, wrong types. A byte[]
        // call never binds to it, so it must not mask the inherited awaitable Stream.ReadAsync.
        // Deciding candidacy by argument count alone would suppress a valid diagnostic here.
        var test =
            @"
using System.IO;
using System.Threading;
using System.Threading.Tasks;
" + StreamStub + @"

public class CustomStream : TestStreamBase
{
    public override int Read(byte[] buffer, int offset, int count) => 0;
    public override void Write(byte[] buffer, int offset, int count) { }
    public int ReadAsync(string name, int offset, int count) => 0;
}

public class TestClass
{
    public async Task<int> RunAsync(CustomStream stream, byte[] buffer, CancellationToken token)
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
" + StreamStub + @"

public class CustomStream : TestStreamBase
{
    public override int Read(byte[] buffer, int offset, int count) => 0;
    public override void Write(byte[] buffer, int offset, int count) { }
    public int ReadAsync(string name, int offset, int count) => 0;
}

public class TestClass
{
    public async Task<int> RunAsync(CustomStream stream, byte[] buffer, CancellationToken token)
    {
        var read = await stream.ReadAsync(buffer, 0, buffer.Length, token);
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
    public async Task ReorderedNamedArguments_AreStillFixed()
    {
        // Named arguments may legally be reordered. The names still exist on the async counterpart,
        // so the rewrite binds — withholding the fix here would be a regression against the plain
        // File case that has always worked.
        var test =
            @"
using System.IO;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(string path, string text, CancellationToken cancellationToken)
    {
        File.{|#0:WriteAllText|}(contents: text, path: path);
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
        await File.WriteAllTextAsync(contents: text, path: path, cancellationToken: cancellationToken);
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

    [Fact]
    public async Task SubclassOwnOverload_NotAStreamPrimitive_ShouldNotReportDiagnostic()
    {
        // Write(string) is the subclass's own convenience overload, not the Stream primitive
        // Write(byte[], int, int). It is not known to block, so matching on the name alone would be
        // a false positive. Only members that resolve back to Stream qualify.
        var test =
            @"
using System.IO;
using System.Threading.Tasks;
" + StreamStub + @"

public class CustomStream : TestStreamBase
{
    public override int Read(byte[] buffer, int offset, int count) => 0;
    public override void Write(byte[] buffer, int offset, int count) { }
    public void Write(string text) { }
    public Task WriteAsync(string text) => Task.CompletedTask;
}

public class TestClass
{
    public async Task RunAsync(CustomStream stream, string text)
    {
        stream.Write(text);
        await Task.Yield();
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
    public async Task StaticAsyncCounterpart_ShouldNotReportDiagnostic()
    {
        // A static member cannot be called through an instance receiver (CS0176). The hiding static
        // ReadAsync wins name resolution, so no valid instance rewrite exists.
        var test =
            @"
using System.IO;
using System.Threading;
using System.Threading.Tasks;
" + StreamStub + @"

public class CustomStream : TestStreamBase
{
    public override int Read(byte[] buffer, int offset, int count) => 0;
    public override void Write(byte[] buffer, int offset, int count) { }
    public new static Task<int> ReadAsync(byte[] buffer, int offset, int count) => Task.FromResult(0);
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
    public async Task RenamedTokenParameterOnCounterpart_UsesThatNameInTheFix()
    {
        // The counterpart renames its token parameter to 'stop'. A named-argument call still gets a
        // fix, so the emitted token argument must use 'stop:' — hardcoding 'cancellationToken:'
        // would fail with CS1739.
        var test =
            @"
using System.IO;
using System.Threading;
using System.Threading.Tasks;
" + StreamStub + @"

public class CustomStream : TestStreamBase
{
    public override int Read(byte[] buffer, int offset, int count) => 0;
    public override void Write(byte[] buffer, int offset, int count) { }
    public new Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken stop) => Task.FromResult(0);
}

public class TestClass
{
    public async Task<int> RunAsync(CustomStream stream, byte[] bytes, CancellationToken token)
    {
        var read = stream.{|#0:Read|}(buffer: bytes, offset: 0, count: bytes.Length);
        await Task.Yield();
        return read;
    }
}";

        var fixedCode =
            @"
using System.IO;
using System.Threading;
using System.Threading.Tasks;
" + StreamStub + @"

public class CustomStream : TestStreamBase
{
    public override int Read(byte[] buffer, int offset, int count) => 0;
    public override void Write(byte[] buffer, int offset, int count) { }
    public new Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken stop) => Task.FromResult(0);
}

public class TestClass
{
    public async Task<int> RunAsync(CustomStream stream, byte[] bytes, CancellationToken token)
    {
        var read = await stream.ReadAsync(buffer: bytes, offset: 0, count: bytes.Length, stop: token);
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
    public async Task GenericAsyncCounterpart_FallsBackToTheInferableOverload()
    {
        // ReadAsync<T> matches by parameter types but introduces a type parameter nothing in the
        // argument list can infer, so flowing the token through it would emit CS0411. Rejecting it
        // as a token-taking counterpart falls back to the inherited tokenless overload, which the
        // named arguments still bind to — the fix compiles, it just does not carry the token.
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
    public override int Read(byte[] buffer, int offset, int count) => 0;
    public override void Write(byte[] buffer, int offset, int count) { }
    public Task<int> ReadAsync<T>(byte[] buffer, int offset, int count, CancellationToken stop) => Task.FromResult(0);
}

public class TestClass
{
    public async Task<int> RunAsync(CustomStream stream, byte[] bytes, CancellationToken token)
    {
        var read = stream.{|#0:Read|}(buffer: bytes, offset: 0, count: bytes.Length);
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
    public override int Read(byte[] buffer, int offset, int count) => 0;
    public override void Write(byte[] buffer, int offset, int count) { }
    public Task<int> ReadAsync<T>(byte[] buffer, int offset, int count, CancellationToken stop) => Task.FromResult(0);
}

public class TestClass
{
    public async Task<int> RunAsync(CustomStream stream, byte[] bytes, CancellationToken token)
    {
        var read = await stream.ReadAsync(buffer: bytes, offset: 0, count: bytes.Length);
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
    public async Task BroaderOverloadWinsByImplicitConversion_ShouldNotReportDiagnostic()
    {
        // ReadAsync(object, int, int) does not have parameter types equal to Read(byte[], int, int),
        // but a byte[] argument converts to object implicitly, so the subclass overload wins
        // resolution and `await` on its int result fails with CS1061. Only real binding catches this
        // — signature equality says the inherited awaitable overload would be used.
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
    public int ReadAsync(object buffer, int offset, int count) => 0;
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
    public async Task NamesReusedAtDifferentOrdinals_ReportsWithoutOfferingAFix()
    {
        // The override reuses the base names in the opposite order, so `count: n, offset: 0` is a
        // legal call meaning "read n bytes at 0". Copying those names onto the inherited ReadAsync,
        // where the ordinals are reversed, would compile and silently swap the two values. A fix
        // that quietly changes behaviour is worse than no fix.
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
    public override int Read(byte[] buffer, int count, int offset) => 0;
    public override void Write(byte[] buffer, int offset, int count) { }
}

public class TestClass
{
    public async Task<int> RunAsync(CustomStream stream, byte[] bytes, CancellationToken token)
    {
        var read = stream.{|#0:Read|}(buffer: bytes, count: bytes.Length, offset: 0);
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
}
