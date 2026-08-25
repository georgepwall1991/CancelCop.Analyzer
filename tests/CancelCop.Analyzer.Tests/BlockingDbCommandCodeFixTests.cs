using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace CancelCop.Analyzer.Tests;

/// <summary>
/// CC046 fixer: rewritten code is compiled by the harness.
/// <c>ExecuteReader()</c> → <c>await ExecuteReaderAsync</c>; a
/// <c>CommandBehavior</c> argument is preserved.
/// </summary>
public class BlockingDbCommandCodeFixTests
{
    private static CSharpCodeFixTest<
        BlockingDbCommandAnalyzer,
        BlockingDbCommandCodeFixProvider,
        DefaultVerifier
    > CreateTest(string testCode, string fixedCode, params DiagnosticResult[] expected)
    {
        var test = new CSharpCodeFixTest<
            BlockingDbCommandAnalyzer,
            BlockingDbCommandCodeFixProvider,
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

    private static DiagnosticResult Expected(int location = 0) =>
        new DiagnosticResult("CC046", DiagnosticSeverity.Warning)
            .WithLocation(location)
            .WithArguments("ExecuteReader");

    private const string MidCommandScaffold =
        @"
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

public class MidCommand : DbCommand
{
    public override string CommandText { get; set; } = """";
    public override int CommandTimeout { get; set; }
    public override CommandType CommandType { get; set; }
    public override bool DesignTimeVisible { get; set; }
    public override UpdateRowSource UpdatedRowSource { get; set; }
    protected override DbConnection DbConnection { get; set; } = null!;
    protected override DbParameterCollection DbParameterCollection => null!;
    protected override DbTransaction DbTransaction { get; set; } = null!;
    public override void Cancel() { }
    public override int ExecuteNonQuery() => 0;
    public override object ExecuteScalar() => null!;
    public override void Prepare() { }
    protected override DbParameter CreateDbParameter() => null!;
    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => null!;
}
";

    [Fact]
    public async Task ExecuteReader_WithTokenInScope_FlowsTheToken()
    {
        var test =
            @"
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(DbCommand command, CancellationToken cancellationToken)
    {
        command.{|#0:ExecuteReader|}();
        await Task.Yield();
    }
}";

        var fixedCode =
            @"
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(DbCommand command, CancellationToken cancellationToken)
    {
        await command.ExecuteReaderAsync(cancellationToken);
        await Task.Yield();
    }
}";

        await CreateTest(test, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task ExecuteReader_CommandBehavior_PreservesTheArgument()
    {
        var test =
            @"
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(DbCommand command, CancellationToken cancellationToken)
    {
        command.{|#0:ExecuteReader|}(CommandBehavior.SingleRow);
        await Task.Yield();
    }
}";

        var fixedCode =
            @"
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(DbCommand command, CancellationToken cancellationToken)
    {
        await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
        await Task.Yield();
    }
}";

        await CreateTest(test, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task ExecuteReader_NamedCommandBehavior_NamesTheTokenArgument()
    {
        var test =
            @"
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(DbCommand command, CancellationToken cancellationToken)
    {
        command.{|#0:ExecuteReader|}(behavior: CommandBehavior.SingleRow);
        await Task.Yield();
    }
}";

        var fixedCode =
            @"
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(DbCommand command, CancellationToken cancellationToken)
    {
        await command.ExecuteReaderAsync(behavior: CommandBehavior.SingleRow, cancellationToken: cancellationToken);
        await Task.Yield();
    }
}";

        await CreateTest(test, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task ExecuteReader_NamedBehavior_UsesHiderTokenParameterName()
    {
        // A provider hider may rename the token parameter. The named
        // rewrite must use that name, not DbCommand's cancellationToken.
        var test =
            MidCommandScaffold
            + @"
public class ProviderCommand : MidCommand
{
    public new int ExecuteReaderAsync(CommandBehavior behavior) => 0;
    public new Task<DbDataReader> ExecuteReaderAsync(CommandBehavior behavior, CancellationToken token)
        => Task.FromResult<DbDataReader>(null!);
}

public class TestClass
{
    public async Task RunAsync(ProviderCommand command, CancellationToken cancellationToken)
    {
        command.{|#0:ExecuteReader|}(behavior: CommandBehavior.SingleRow);
        await Task.Yield();
    }
}";

        var fixedCode =
            MidCommandScaffold
            + @"
public class ProviderCommand : MidCommand
{
    public new int ExecuteReaderAsync(CommandBehavior behavior) => 0;
    public new Task<DbDataReader> ExecuteReaderAsync(CommandBehavior behavior, CancellationToken token)
        => Task.FromResult<DbDataReader>(null!);
}

public class TestClass
{
    public async Task RunAsync(ProviderCommand command, CancellationToken cancellationToken)
    {
        await command.ExecuteReaderAsync(behavior: CommandBehavior.SingleRow, token: cancellationToken);
        await Task.Yield();
    }
}";

        await CreateTest(test, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task ExecuteReader_WithoutTokenInScope_StillCompiles()
    {
        var test =
            @"
using System.Data.Common;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(DbCommand command)
    {
        command.{|#0:ExecuteReader|}();
        await Task.Yield();
    }
}";

        var fixedCode =
            @"
using System.Data.Common;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(DbCommand command)
    {
        await command.ExecuteReaderAsync();
        await Task.Yield();
    }
}";

        await CreateTest(test, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task ExecuteReader_CommandBehavior_WithoutToken_StillCompiles()
    {
        var test =
            @"
using System.Data;
using System.Data.Common;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(DbCommand command)
    {
        command.{|#0:ExecuteReader|}(CommandBehavior.SingleRow);
        await Task.Yield();
    }
}";

        var fixedCode =
            @"
using System.Data;
using System.Data.Common;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(DbCommand command)
    {
        await command.ExecuteReaderAsync(CommandBehavior.SingleRow);
        await Task.Yield();
    }
}";

        await CreateTest(test, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task ExecuteReader_OnPropertyReceiver_KeepsTheReceiver()
    {
        var test =
            @"
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

public class Host
{
    public DbCommand Command { get; set; } = null!;
}

public class TestClass
{
    public async Task RunAsync(Host host, CancellationToken cancellationToken)
    {
        host.Command.{|#0:ExecuteReader|}();
        await Task.Yield();
    }
}";

        var fixedCode =
            @"
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

public class Host
{
    public DbCommand Command { get; set; } = null!;
}

public class TestClass
{
    public async Task RunAsync(Host host, CancellationToken cancellationToken)
    {
        await host.Command.ExecuteReaderAsync(cancellationToken);
        await Task.Yield();
    }
}";

        await CreateTest(test, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task ExecuteReader_NullConditional_HoistsToIfNotNullExecuteReaderAsync()
    {
        var source =
            @"
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(DbCommand? command, CancellationToken cancellationToken)
    {
        command?.{|#0:ExecuteReader|}();
        await Task.Yield();
    }
}";

        var fixedCode =
            @"
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(DbCommand? command, CancellationToken cancellationToken)
    {
        if (command is not null)
        {
            await command.ExecuteReaderAsync(cancellationToken);
        }
        await Task.Yield();
    }
}";

        await CreateTest(source, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task ExecuteReader_InsideLock_ReportsWithoutOfferingAFix()
    {
        var source =
            @"
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    private readonly object _gate = new object();

    public async Task RunAsync(DbCommand command, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            command.{|#0:ExecuteReader|}();
        }

        await Task.Yield();
    }
}";

        await CreateTest(source, source, Expected()).RunAsync();
    }

    [Fact]
    public async Task ExecuteReader_WithTriviaInsideMemberAccess_KeepsTheComment()
    {
        var test =
            @"
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(DbCommand command, CancellationToken cancellationToken)
    {
        command /* pooled */ .{|#0:ExecuteReader|}();
        await Task.Yield();
    }
}";

        var fixedCode =
            @"
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(DbCommand command, CancellationToken cancellationToken)
    {
        await command /* pooled */ .ExecuteReaderAsync(cancellationToken);
        await Task.Yield();
    }
}";

        await CreateTest(test, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task KeywordEscapedTokenName_IsReEscapedInTheFix()
    {
        var test =
            @"
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(DbCommand command, CancellationToken @event)
    {
        command.{|#0:ExecuteReader|}();
        await Task.Yield();
    }
}";

        var fixedCode =
            @"
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(DbCommand command, CancellationToken @event)
    {
        await command.ExecuteReaderAsync(@event);
        await Task.Yield();
    }
}";

        await CreateTest(test, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task AwaitWouldSpanARefStructLocal_ReportsWithoutOfferingAFix()
    {
        var source =
            @"
using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task<int> RunAsync(DbCommand command, int[] data, CancellationToken cancellationToken)
    {
        await Task.Yield();
        Span<int> span = data.AsSpan();
        command.{|#0:ExecuteReader|}();
        return span[0];
    }
}";

        await CreateTest(source, source, Expected()).RunAsync();
    }

    [Fact]
    public async Task ShadowedTokenName_FallsBackToTheParameterlessForm()
    {
        var test =
            @"
using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(DbCommand command, CancellationToken cancellationToken)
    {
        Func<string, Task> f = async (cancellationToken) =>
        {
            command.{|#0:ExecuteReader|}();
            await Task.Yield();
        };

        await f(""x"");
    }
}";

        var fixedCode =
            @"
using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(DbCommand command, CancellationToken cancellationToken)
    {
        Func<string, Task> f = async (cancellationToken) =>
        {
            await command.ExecuteReaderAsync();
            await Task.Yield();
        };

        await f(""x"");
    }
}";

        await CreateTest(test, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task ChainedConditionalAccess_HoistsToIfNotNullExecuteReaderAsync()
    {
        var source =
            @"
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

public class Host
{
    public DbCommand Command { get; set; } = null!;
}

public class TestClass
{
    public async Task RunAsync(Host? host, CancellationToken cancellationToken)
    {
        host?.Command.{|#0:ExecuteReader|}();
        await Task.Yield();
    }
}";

        var fixedCode =
            @"
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

public class Host
{
    public DbCommand Command { get; set; } = null!;
}

public class TestClass
{
    public async Task RunAsync(Host? host, CancellationToken cancellationToken)
    {
        if (host is not null)
        {
            await host.Command.ExecuteReaderAsync(cancellationToken);
        }
        await Task.Yield();
    }
}";

        await CreateTest(source, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task ExecuteReaderAsArgumentInsideUnrelatedConditionalAccess_IsFixed()
    {
        // The ExecuteReader call is an argument, not the WhenNotNull branch, so
        // await is legal: host?.Use(await command.ExecuteReaderAsync(...)).
        var test =
            @"
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

public class Host
{
    public void Use(DbDataReader reader) { }
}

public class TestClass
{
    public async Task RunAsync(Host? host, DbCommand command, CancellationToken cancellationToken)
    {
        host?.Use(command.{|#0:ExecuteReader|}());
        await Task.Yield();
    }
}";

        var fixedCode =
            @"
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

public class Host
{
    public void Use(DbDataReader reader) { }
}

public class TestClass
{
    public async Task RunAsync(Host? host, DbCommand command, CancellationToken cancellationToken)
    {
        host?.Use(await command.ExecuteReaderAsync(cancellationToken));
        await Task.Yield();
    }
}";

        await CreateTest(test, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task ExecuteReaderAsyncHiderOnProvider_StillFixes()
    {
        // DbCommand.ExecuteReaderAsync(CancellationToken) is not virtual
        // (only the CommandBehavior+token form is). Providers such as
        // SqlCommand hide it with `new` for a covariant Task<SqlDataReader>.
        // The rewrite must still bind and flow the token.
        var test =
            MidCommandScaffold
            + @"
public class ProviderCommand : MidCommand
{
    public new Task<DbDataReader> ExecuteReaderAsync(CancellationToken cancellationToken)
        => Task.FromResult<DbDataReader>(null!);
}

public class TestClass
{
    public async Task RunAsync(ProviderCommand command, CancellationToken cancellationToken)
    {
        command.{|#0:ExecuteReader|}();
        await Task.Yield();
    }
}";

        var fixedCode =
            MidCommandScaffold
            + @"
public class ProviderCommand : MidCommand
{
    public new Task<DbDataReader> ExecuteReaderAsync(CancellationToken cancellationToken)
        => Task.FromResult<DbDataReader>(null!);
}

public class TestClass
{
    public async Task RunAsync(ProviderCommand command, CancellationToken cancellationToken)
    {
        await command.ExecuteReaderAsync(cancellationToken);
        await Task.Yield();
    }
}";

        await CreateTest(test, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task InternalExecuteReaderAsyncHider_StillFixes()
    {
        // A same-assembly internal TAP hider is reachable. Speculative bind
        // is the authority — do not require DeclaredAccessibility.Public.
        var test =
            MidCommandScaffold
            + @"
public class ProviderCommand : MidCommand
{
    internal new Task<DbDataReader> ExecuteReaderAsync(CancellationToken cancellationToken)
        => Task.FromResult<DbDataReader>(null!);
}

public class TestClass
{
    public async Task RunAsync(ProviderCommand command, CancellationToken cancellationToken)
    {
        command.{|#0:ExecuteReader|}();
        await Task.Yield();
    }
}";

        var fixedCode =
            MidCommandScaffold
            + @"
public class ProviderCommand : MidCommand
{
    internal new Task<DbDataReader> ExecuteReaderAsync(CancellationToken cancellationToken)
        => Task.FromResult<DbDataReader>(null!);
}

public class TestClass
{
    public async Task RunAsync(ProviderCommand command, CancellationToken cancellationToken)
    {
        await command.ExecuteReaderAsync(cancellationToken);
        await Task.Yield();
    }
}";

        await CreateTest(test, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task ExtraExecuteReaderAsyncIntOverload_NullConditional_HoistsToCancellableForm()
    {
        // An unrelated ExecuteReaderAsync(int) is arity-compatible with the
        // token-taking rewrite. ReachesCounterpart must keep scanning to the
        // framework method.
        var source =
            MidCommandScaffold
            + @"
public class ExtraCommand : MidCommand
{
    public int ExecuteReaderAsync(int timeout) => 0;
}

public class TestClass
{
    public async Task RunAsync(ExtraCommand? command, CancellationToken cancellationToken)
    {
        command?.{|#0:ExecuteReader|}();
        await Task.Yield();
    }
}";

        var fixedCode =
            MidCommandScaffold
            + @"
public class ExtraCommand : MidCommand
{
    public int ExecuteReaderAsync(int timeout) => 0;
}

public class TestClass
{
    public async Task RunAsync(ExtraCommand? command, CancellationToken cancellationToken)
    {
        if (command is not null)
        {
            await command.ExecuteReaderAsync(cancellationToken);
        }
        await Task.Yield();
    }
}";

        await CreateTest(source, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task ParameterlessHider_WithTokenInScope_FlowsTheToken()
    {
        var test =
            MidCommandScaffold
            + @"
public class HiddenCommand : MidCommand
{
    public new int ExecuteReaderAsync() => 0;
}

public class TestClass
{
    public async Task RunAsync(HiddenCommand command, CancellationToken cancellationToken)
    {
        command.{|#0:ExecuteReader|}();
        await Task.Yield();
    }
}";

        var fixedCode =
            MidCommandScaffold
            + @"
public class HiddenCommand : MidCommand
{
    public new int ExecuteReaderAsync() => 0;
}

public class TestClass
{
    public async Task RunAsync(HiddenCommand command, CancellationToken cancellationToken)
    {
        await command.ExecuteReaderAsync(cancellationToken);
        await Task.Yield();
    }
}";

        await CreateTest(test, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task ParameterlessHider_NoTokenInScope_ReportsWithoutOfferingAFix()
    {
        // Only the token-taking TAP form is reachable. There is no token to
        // flow, so the diagnostic stays and the fixer is withheld rather
        // than rewriting to the unusable parameterless hider.
        var source =
            MidCommandScaffold
            + @"
public class HiddenCommand : MidCommand
{
    public new int ExecuteReaderAsync() => 0;
}

public class TestClass
{
    public async Task RunAsync(HiddenCommand command)
    {
        command.{|#0:ExecuteReader|}();
        await Task.Yield();
    }
}";

        await CreateTest(source, source, Expected()).RunAsync();
    }

    [Fact]
    public async Task SubclassHidingExecuteReaderAsyncWithTaskInt_ShouldNotReportDiagnostic()
    {
        // A TAP-shaped hider that does not return a reader is not
        // ExecuteReaderAsync. Reporting would advertise a false counterpart
        // and the rewrite would change the call's meaning.
        var test =
            MidCommandScaffold
            + @"
public class HiddenCommand : MidCommand
{
    public new Task<int> ExecuteReaderAsync() => Task.FromResult(0);
    public new Task<int> ExecuteReaderAsync(CancellationToken cancellationToken) => Task.FromResult(0);
    public new Task<int> ExecuteReaderAsync(CommandBehavior behavior) => Task.FromResult(0);
    public new Task<int> ExecuteReaderAsync(CommandBehavior behavior, CancellationToken cancellationToken) => Task.FromResult(0);
}

public class TestClass
{
    public async Task RunAsync(HiddenCommand command, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        command.ExecuteReader();
        await Task.Yield();
    }
}";

        var t = new CSharpAnalyzerTest<BlockingDbCommandAnalyzer, DefaultVerifier>
        {
            TestCode = test,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        await t.RunAsync();
    }

    [Fact]
    public async Task SubclassHidingAllExecuteReaderAsyncOverloads_ShouldNotReportDiagnostic()
    {
        // If no ExecuteReaderAsync shape is reachable, the diagnostic's
        // "use ExecuteReaderAsync" claim is false. Stay quiet.
        var test =
            MidCommandScaffold
            + @"
public class HiddenCommand : MidCommand
{
    public new int ExecuteReaderAsync() => 0;
    public new int ExecuteReaderAsync(CancellationToken cancellationToken) => 0;
    public new int ExecuteReaderAsync(CommandBehavior behavior) => 0;
    public new int ExecuteReaderAsync(CommandBehavior behavior, CancellationToken cancellationToken) => 0;
}

public class TestClass
{
    public async Task RunAsync(HiddenCommand command, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        command.ExecuteReader();
        await Task.Yield();
    }
}";

        var t = new CSharpAnalyzerTest<BlockingDbCommandAnalyzer, DefaultVerifier>
        {
            TestCode = test,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        await t.RunAsync();
    }

    [Fact]
    public async Task SubclassHidingAllExecuteReaderAsyncOverloads_NullConditional_ShouldNotReportDiagnostic()
    {
        // Same hiders via `?.`: ReachesCounterpart must not walk past a
        // same-signature `new` hider to the framework method.
        var test =
            MidCommandScaffold
            + @"
public class HiddenCommand : MidCommand
{
    public new int ExecuteReaderAsync() => 0;
    public new int ExecuteReaderAsync(CancellationToken cancellationToken) => 0;
    public new int ExecuteReaderAsync(CommandBehavior behavior) => 0;
    public new int ExecuteReaderAsync(CommandBehavior behavior, CancellationToken cancellationToken) => 0;
}

public class TestClass
{
    public async Task RunAsync(HiddenCommand? command, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        command?.ExecuteReader();
        await Task.Yield();
    }
}";

        var t = new CSharpAnalyzerTest<BlockingDbCommandAnalyzer, DefaultVerifier>
        {
            TestCode = test,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        await t.RunAsync();
    }

    [Fact]
    public async Task FixAll_TwoReaders_BothBecomeAwaitExecuteReaderAsync()
    {
        var test =
            @"
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(DbCommand a, DbCommand b, CancellationToken cancellationToken)
    {
        a.{|#0:ExecuteReader|}();
        b.{|#1:ExecuteReader|}();
        await Task.Yield();
    }
}";

        var fixedCode =
            @"
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(DbCommand a, DbCommand b, CancellationToken cancellationToken)
    {
        await a.ExecuteReaderAsync(cancellationToken);
        await b.ExecuteReaderAsync(cancellationToken);
        await Task.Yield();
    }
}";

        await CreateTest(test, fixedCode, Expected(0), Expected(1)).RunAsync();
    }

    [Fact]
    public async Task ResultUsedAsReceiver_ParenthesizesAwait()
    {
        var test =
            @"
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(DbCommand command, CancellationToken cancellationToken)
    {
        command.{|#0:ExecuteReader|}().Dispose();
        await Task.Yield();
    }
}";

        var fixedCode =
            @"
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(DbCommand command, CancellationToken cancellationToken)
    {
        (await command.ExecuteReaderAsync(cancellationToken)).Dispose();
        await Task.Yield();
    }
}";

        await CreateTest(test, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task ResultUsedAsReceiver_ThroughNullForgiving_ParenthesizesAwait()
    {
        var test =
            @"
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(DbCommand command, CancellationToken cancellationToken)
    {
        command.{|#0:ExecuteReader|}()!.Dispose();
        await Task.Yield();
    }
}";

        var fixedCode =
            @"
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(DbCommand command, CancellationToken cancellationToken)
    {
        (await command.ExecuteReaderAsync(cancellationToken))!.Dispose();
        await Task.Yield();
    }
}";

        await CreateTest(test, fixedCode, Expected()).RunAsync();
    }
}
