using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace CancelCop.Analyzer.Tests;

/// <summary>
/// CC047 fixer: rewritten code is compiled by the harness.
/// <c>ExecuteNonQuery()</c> → <c>await ExecuteNonQueryAsync</c>.
/// </summary>
public class BlockingDbNonQueryCodeFixTests
{
    private static CSharpCodeFixTest<
        BlockingDbNonQueryAnalyzer,
        BlockingDbNonQueryCodeFixProvider,
        DefaultVerifier
    > CreateTest(string testCode, string fixedCode, params DiagnosticResult[] expected)
    {
        var test = new CSharpCodeFixTest<
            BlockingDbNonQueryAnalyzer,
            BlockingDbNonQueryCodeFixProvider,
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
        new DiagnosticResult("CC047", DiagnosticSeverity.Warning)
            .WithLocation(location)
            .WithArguments("ExecuteNonQuery");

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
    public async Task ExecuteNonQuery_WithTokenInScope_FlowsTheToken()
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
        command.{|#0:ExecuteNonQuery|}();
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
        await command.ExecuteNonQueryAsync(cancellationToken);
        await Task.Yield();
    }
}";

        await CreateTest(test, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task ExecuteNonQuery_WithoutTokenInScope_StillCompiles()
    {
        var test =
            @"
using System.Data.Common;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(DbCommand command)
    {
        command.{|#0:ExecuteNonQuery|}();
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
        await command.ExecuteNonQueryAsync();
        await Task.Yield();
    }
}";

        await CreateTest(test, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task ExecuteNonQuery_NullConditional_HoistsToIfNotNullExecuteNonQueryAsync()
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
        command?.{|#0:ExecuteNonQuery|}();
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
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await Task.Yield();
    }
}";

        await CreateTest(source, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task ChainedConditionalExecuteNonQuery_HoistsWithSplicedCommand()
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
        host?.Command.{|#0:ExecuteNonQuery|}();
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
            await host.Command.ExecuteNonQueryAsync(cancellationToken);
        }
        await Task.Yield();
    }
}";

        await CreateTest(source, fixedCode, Expected()).RunAsync();
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
    public async Task<string> RunAsync(DbCommand command, CancellationToken cancellationToken)
    {
        return command.{|#0:ExecuteNonQuery|}().ToString();
    }
}";

        var fixedCode =
            @"
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task<string> RunAsync(DbCommand command, CancellationToken cancellationToken)
    {
        return (await command.ExecuteNonQueryAsync(cancellationToken)).ToString();
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
    public async Task<string> RunAsync(DbCommand command, CancellationToken cancellationToken)
    {
        return command.{|#0:ExecuteNonQuery|}()!.ToString();
    }
}";

        var fixedCode =
            @"
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task<string> RunAsync(DbCommand command, CancellationToken cancellationToken)
    {
        return (await command.ExecuteNonQueryAsync(cancellationToken))!.ToString();
    }
}";

        await CreateTest(test, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task CallFromExecuteNonQueryAsyncOverride_ReportsWithoutOfferingAFix()
    {
        var source =
            MidCommandScaffold
            + @"
public class ProviderCommand : MidCommand
{
    public override async Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken)
    {
        return {|#0:ExecuteNonQuery|}();
    }
}";

        await CreateTest(source, source, Expected()).RunAsync();
    }

    [Fact]
    public async Task OtherCommandFromExecuteNonQueryAsyncOverride_StillFixes()
    {
        var test =
            MidCommandScaffold
            + @"
public class ProviderCommand : MidCommand
{
    public override async Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken)
    {
        DbCommand other = this;
        other.{|#0:ExecuteNonQuery|}();
        return 0;
    }
}";

        var fixedCode =
            MidCommandScaffold
            + @"
public class ProviderCommand : MidCommand
{
    public override async Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken)
    {
        DbCommand other = this;
        await other.ExecuteNonQueryAsync(cancellationToken);
        return 0;
    }
}";

        await CreateTest(test, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task HelperNamedExecuteNonQueryAsync_StillFixes()
    {
        // A same-named helper with extra parameters is not the framework
        // TAP entry point. Rewriting ExecuteNonQuery() still binds to
        // ExecuteNonQueryAsync(token) and does not recurse.
        var test =
            MidCommandScaffold
            + @"
public class ProviderCommand : MidCommand
{
    public async Task<int> ExecuteNonQueryAsync(string sql, CancellationToken cancellationToken)
    {
        CommandText = sql;
        return {|#0:ExecuteNonQuery|}();
    }
}";

        var fixedCode =
            MidCommandScaffold
            + @"
public class ProviderCommand : MidCommand
{
    public async Task<int> ExecuteNonQueryAsync(string sql, CancellationToken cancellationToken)
    {
        CommandText = sql;
        return await ExecuteNonQueryAsync(cancellationToken);
    }
}";

        await CreateTest(test, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task ExecuteNonQuery_InsideLock_ReportsWithoutOfferingAFix()
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
            command.{|#0:ExecuteNonQuery|}();
        }

        await Task.Yield();
    }
}";

        await CreateTest(source, source, Expected()).RunAsync();
    }

    [Fact]
    public async Task ExecuteNonQueryAsyncOverrideOnProvider_StillFixes()
    {
        var test =
            MidCommandScaffold
            + @"
public class ProviderCommand : MidCommand
{
    public override Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken)
        => Task.FromResult(0);
}

public class TestClass
{
    public async Task RunAsync(ProviderCommand command, CancellationToken cancellationToken)
    {
        command.{|#0:ExecuteNonQuery|}();
        await Task.Yield();
    }
}";

        var fixedCode =
            MidCommandScaffold
            + @"
public class ProviderCommand : MidCommand
{
    public override Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken)
        => Task.FromResult(0);
}

public class TestClass
{
    public async Task RunAsync(ProviderCommand command, CancellationToken cancellationToken)
    {
        await command.ExecuteNonQueryAsync(cancellationToken);
        await Task.Yield();
    }
}";

        await CreateTest(test, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task ExtraExecuteNonQueryAsyncIntOverload_NullConditional_HoistsToCancellableForm()
    {
        // The unrelated ExecuteNonQueryAsync(int) hider is skipped: the speculative rebind
        // selects the inherited framework ExecuteNonQueryAsync(CancellationToken).
        var source =
            MidCommandScaffold
            + @"
public class ExtraCommand : MidCommand
{
    public int ExecuteNonQueryAsync(int timeout) => 0;
}

public class TestClass
{
    public async Task RunAsync(ExtraCommand? command, CancellationToken cancellationToken)
    {
        command?.{|#0:ExecuteNonQuery|}();
        await Task.Yield();
    }
}";

        var fixedCode =
            MidCommandScaffold
            + @"
public class ExtraCommand : MidCommand
{
    public int ExecuteNonQueryAsync(int timeout) => 0;
}

public class TestClass
{
    public async Task RunAsync(ExtraCommand? command, CancellationToken cancellationToken)
    {
        if (command is not null)
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await Task.Yield();
    }
}";

await CreateTest(source, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task OptionalIntExecuteNonQueryAsync_AfterHidingFrameworkShapes_ShouldNotReport()
    {
        // A leftover Task<int> ExecuteNonQueryAsync(int timeout = 30) is
        // not the framework counterpart. Reporting or rewriting to it
        // would change meaning.
        var test =
            MidCommandScaffold
            + @"
public class HiddenCommand : MidCommand
{
    public new string ExecuteNonQueryAsync() => """";
    public new string ExecuteNonQueryAsync(CancellationToken cancellationToken) => """";
    public Task<int> ExecuteNonQueryAsync(int timeout = 30) => Task.FromResult(0);
}

public class TestClass
{
    public async Task RunAsync(HiddenCommand command, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        command.ExecuteNonQuery();
        await Task.Yield();
    }
}";

        var t = new CSharpAnalyzerTest<BlockingDbNonQueryAnalyzer, DefaultVerifier>
        {
            TestCode = test,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        await t.RunAsync();
    }

    [Fact]
    public async Task SubclassHidingBothExecuteNonQueryAsyncOverloads_ShouldNotReportDiagnostic()
    {
        var test =
            MidCommandScaffold
            + @"
public class HiddenCommand : MidCommand
{
    public new string ExecuteNonQueryAsync() => """";
    public new string ExecuteNonQueryAsync(CancellationToken cancellationToken) => """";
}

public class TestClass
{
    public async Task RunAsync(HiddenCommand command, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        command.ExecuteNonQuery();
        await Task.Yield();
    }
}";

        var t = new CSharpAnalyzerTest<BlockingDbNonQueryAnalyzer, DefaultVerifier>
        {
            TestCode = test,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        await t.RunAsync();
    }

    [Fact]
    public async Task ParameterlessHider_NoTokenInScope_ReportsWithoutOfferingAFix()
    {
        var source =
            MidCommandScaffold
            + @"
public class HiddenCommand : MidCommand
{
    public new string ExecuteNonQueryAsync() => """";
}

public class TestClass
{
    public async Task RunAsync(HiddenCommand command)
    {
        command.{|#0:ExecuteNonQuery|}();
        await Task.Yield();
    }
}";

        await CreateTest(source, source, Expected()).RunAsync();
    }

    [Fact]
    public async Task FixAll_TwoNonQueries_BothBecomeAwaitExecuteNonQueryAsync()
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
        a.{|#0:ExecuteNonQuery|}();
        b.{|#1:ExecuteNonQuery|}();
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
        await a.ExecuteNonQueryAsync(cancellationToken);
        await b.ExecuteNonQueryAsync(cancellationToken);
        await Task.Yield();
    }
}";

        await CreateTest(test, fixedCode, Expected(0), Expected(1)).RunAsync();
    }
}
