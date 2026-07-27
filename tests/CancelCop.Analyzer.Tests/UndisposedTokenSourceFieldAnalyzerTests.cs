using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace CancelCop.Analyzer.Tests;

/// <summary>
/// CC033: a <c>CancellationTokenSource</c> field the declaring type creates and never disposes.
/// CC014 covers the local case, where a <c>using</c> declaration is a mechanical fix; a field's
/// lifetime is the object's, so the resolution is a design change and the rule is analyzer-only.
/// </summary>
public class UndisposedTokenSourceFieldAnalyzerTests
{
    private static CSharpAnalyzerTest<UndisposedTokenSourceFieldAnalyzer, DefaultVerifier> Test(
        string source
    ) => new() { TestCode = source, ReferenceAssemblies = ReferenceAssemblies.Net.Net90 };

    private static DiagnosticResult Expected(string field) =>
        new DiagnosticResult("CC033", DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments(field);

    [Fact]
    public async Task FieldInitializedAndNeverDisposed_ShouldReportDiagnostic()
    {
        var test =
            @"
using System.Threading;

public class Worker
{
    private readonly CancellationTokenSource {|#0:_cts|} = new CancellationTokenSource();

    public CancellationToken Token => _cts.Token;
}";

        var t = Test(test);
        t.ExpectedDiagnostics.Add(Expected("_cts"));
        await t.RunAsync();
    }

    [Fact]
    public async Task FieldAssignedInConstructorAndNeverDisposed_ShouldReportDiagnostic()
    {
        var test =
            @"
using System.Threading;

public class Worker
{
    private CancellationTokenSource {|#0:_cts|};

    public Worker()
    {
        _cts = new CancellationTokenSource();
    }
}";

        var t = Test(test);
        t.ExpectedDiagnostics.Add(Expected("_cts"));
        await t.RunAsync();
    }

    [Fact]
    public async Task LinkedSourceFieldNeverDisposed_ShouldReportDiagnostic()
    {
        // A linked source is the worse leak: undisposed, it stays attached to its parent's callback
        // list, so a long-lived parent accumulates every child ever created.
        var test =
            @"
using System.Threading;

public class Worker
{
    private CancellationTokenSource {|#0:_cts|};

    public void Start(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    }
}";

        var t = Test(test);
        t.ExpectedDiagnostics.Add(Expected("_cts"));
        await t.RunAsync();
    }

    [Fact]
    public async Task FieldDisposed_ShouldNotReportDiagnostic()
    {
        var test =
            @"
using System;
using System.Threading;

public class Worker : IDisposable
{
    private readonly CancellationTokenSource _cts = new CancellationTokenSource();

    public void Dispose()
    {
        _cts.Dispose();
    }
}";

        await Test(test).RunAsync();
    }

    [Fact]
    public async Task FieldDisposedFromAsyncDisposalPath_ShouldNotReportDiagnostic()
    {
        // CancellationTokenSource has no DisposeAsync of its own, so an IAsyncDisposable owner
        // disposes it synchronously from DisposeAsync. Disposal is not confined to a Dispose method.
        var test =
            @"
using System;
using System.Threading;
using System.Threading.Tasks;

public class Worker : IAsyncDisposable
{
    private readonly CancellationTokenSource _cts = new CancellationTokenSource();

    public ValueTask DisposeAsync()
    {
        _cts.Dispose();
        return default;
    }
}";

        await Test(test).RunAsync();
    }

    [Fact]
    public async Task InjectedField_ShouldNotReportDiagnostic()
    {
        // The type did not create the source, so it does not own it — disposing someone else's
        // source is a bug, so a rule that demanded it would be actively harmful.
        var test =
            @"
using System.Threading;

public class Worker
{
    private readonly CancellationTokenSource _cts;

    public Worker(CancellationTokenSource cts)
    {
        _cts = cts;
    }
}";

        await Test(test).RunAsync();
    }

    [Fact]
    public async Task FieldReturned_ShouldNotReportDiagnostic()
    {
        // Once the source is handed out, who disposes it is no longer decidable from this type
        // alone. CC014 makes the same conservative call for locals.
        var test =
            @"
using System.Threading;

public class Worker
{
    private readonly CancellationTokenSource _cts = new CancellationTokenSource();

    public CancellationTokenSource Source() => _cts;
}";

        await Test(test).RunAsync();
    }

    [Fact]
    public async Task FieldPassedAsArgument_ShouldNotReportDiagnostic()
    {
        var test =
            @"
using System.Threading;

public class Worker
{
    private readonly CancellationTokenSource _cts = new CancellationTokenSource();

    public void Register()
    {
        Track(_cts);
    }

    private static void Track(CancellationTokenSource cts) { }
}";

        await Test(test).RunAsync();
    }

    [Fact]
    public async Task StaticField_ShouldNotReportDiagnostic()
    {
        // A static source lives for the process, which is typically deliberate.
        var test =
            @"
using System.Threading;

public class Worker
{
    private static readonly CancellationTokenSource Shutdown = new CancellationTokenSource();

    public static CancellationToken Token => Shutdown.Token;
}";

        await Test(test).RunAsync();
    }

    [Fact]
    public async Task NonTokenSourceField_ShouldNotReportDiagnostic()
    {
        var test =
            @"
using System;

public class Worker : IDisposable
{
    private readonly IDisposable _other = null!;

    public void Dispose() { }
}";

        await Test(test).RunAsync();
    }

    [Fact]
    public async Task LookalikeTokenSourceField_ShouldNotReportDiagnostic()
    {
        // Same name, different namespace. CC033 is symbol-gated.
        var test =
            @"
namespace Custom
{
    public class CancellationTokenSource { }

    public class Worker
    {
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
    }
}";

        await Test(test).RunAsync();
    }

    [Fact]
    public async Task DisposedInPartialDeclaration_ShouldNotReportDiagnostic()
    {
        // Creation and disposal routinely live in different members, and for a partial type in
        // different files — analysing the whole symbol rather than one declaration is what makes
        // "never disposed" answerable at all.
        var test =
            @"
using System;
using System.Threading;

public partial class Worker
{
    private readonly CancellationTokenSource _cts = new CancellationTokenSource();
}

public partial class Worker : IDisposable
{
    public void Dispose()
    {
        _cts.Dispose();
    }
}";

        await Test(test).RunAsync();
    }

    [Fact]
    public async Task ImplicitNewInitializer_ShouldReportDiagnostic()
    {
        // `= new();` is the common modern spelling and is a different syntax node from an explicit
        // object creation, so a rule that only looked for the explicit form missed a real leak.
        var test =
            @"
using System.Threading;

public class Worker
{
    private readonly CancellationTokenSource {|#0:_cts|} = new();

    public CancellationToken Token => _cts.Token;
}";

        var t = Test(test);
        t.ExpectedDiagnostics.Add(Expected("_cts"));
        await t.RunAsync();
    }

    [Fact]
    public async Task DisposedThroughThis_ShouldNotReportDiagnostic()
    {
        // `this._cts` puts the identifier in the *name* position of a member access, so reading its
        // immediate parent looks at the access instead of past it.
        var test =
            @"
using System;
using System.Threading;

public class Worker : IDisposable
{
    private readonly CancellationTokenSource _cts = new CancellationTokenSource();

    public void Dispose()
    {
        this._cts.Dispose();
    }
}";

        await Test(test).RunAsync();
    }

    [Fact]
    public async Task DisposedNullConditionally_ShouldNotReportDiagnostic()
    {
        var test =
            @"
using System;
using System.Threading;

public class Worker : IDisposable
{
    private CancellationTokenSource? _cts = new CancellationTokenSource();

    public void Dispose()
    {
        _cts?.Dispose();
    }
}";

        await Test(test).RunAsync();
    }

    [Fact]
    public async Task ReturnedThroughThis_ShouldNotReportDiagnostic()
    {
        var test =
            @"
using System.Threading;

public class Worker
{
    private readonly CancellationTokenSource _cts = new CancellationTokenSource();

    public CancellationTokenSource Source()
    {
        return this._cts;
    }
}";

        await Test(test).RunAsync();
    }

    [Fact]
    public async Task DisposeMethodGroupCaptured_ShouldReportDiagnostic()
    {
        // Naming the method is not calling it, so the source is still never disposed.
        var test =
            @"
using System;
using System.Threading;

public class Worker
{
    private readonly CancellationTokenSource {|#0:_cts|} = new CancellationTokenSource();

    public Action Cleanup() => _cts.Dispose;
}";

        var t = Test(test);
        t.ExpectedDiagnostics.Add(Expected("_cts"));
        await t.RunAsync();
    }

    [Fact]
    public async Task ParenthesizedCreation_ShouldReportDiagnostic()
    {
        // Parentheses are compile-time only; the source is still created and still leaked.
        var test =
            @"
using System.Threading;

public class Worker
{
    private CancellationTokenSource {|#0:_cts|};

    public Worker()
    {
        _cts = (new CancellationTokenSource());
    }
}";

        var t = Test(test);
        t.ExpectedDiagnostics.Add(Expected("_cts"));
        await t.RunAsync();
    }

    [Fact]
    public async Task DisposedThroughCompileTimeWrappers_ShouldNotReportDiagnostic()
    {
        // `(_cts!).Dispose()` disposes exactly as much as `_cts.Dispose()`.
        var test =
            @"
using System;
using System.Threading;

public class Worker : IDisposable
{
    private CancellationTokenSource? _cts = new CancellationTokenSource();

    public void Dispose()
    {
        (_cts!).Dispose();
    }
}";

        await Test(test).RunAsync();
    }

    [Fact]
    public async Task ExtensionMethodNamedDispose_ShouldReportDiagnostic()
    {
        // An extension called Dispose is free to do nothing at all, so accepting the spelling would
        // exonerate a real leak. CancellationTokenSource has no instance DisposeAsync, which means
        // every _cts.DisposeAsync() is such an extension.
        var test =
            @"
using System.Threading;

public static class Extensions
{
    public static void Dispose(this CancellationTokenSource source, int unused) { }
    public static void DisposeAsync(this CancellationTokenSource source) { }
}

public class Worker
{
    private readonly CancellationTokenSource {|#0:_cts|} = new CancellationTokenSource();

    public void Cleanup()
    {
        _cts.DisposeAsync();
    }
}";

        var t = Test(test);
        t.ExpectedDiagnostics.Add(Expected("_cts"));
        await t.RunAsync();
    }

    [Fact]
    public async Task DisposedThroughALocalAlias_ShouldNotReportDiagnostic()
    {
        // Disposal routinely goes through a snapshot. Copying the field into another location makes
        // ownership undecidable from this type alone, which is the same reason escape by return or
        // argument exonerates.
        var test =
            @"
using System;
using System.Threading;

public class Worker : IDisposable
{
    private readonly CancellationTokenSource _cts = new CancellationTokenSource();

    public void Dispose()
    {
        var source = _cts;
        source.Dispose();
    }
}";

        await Test(test).RunAsync();
    }

    [Fact]
    public async Task CopiedToAnotherField_ShouldNotReportDiagnostic()
    {
        var test =
            @"
using System.Threading;

public class Worker
{
    private readonly CancellationTokenSource _cts = new CancellationTokenSource();
    private CancellationTokenSource? _alias;

    public void Alias()
    {
        _alias = _cts;
    }
}";

        await Test(test).RunAsync();
    }
}
