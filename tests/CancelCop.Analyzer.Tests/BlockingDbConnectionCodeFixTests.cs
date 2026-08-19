using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace CancelCop.Analyzer.Tests;

/// <summary>
/// CC045 fixer: the rewritten code is compiled by the harness, so these pin
/// that <c>Open()</c> becomes a valid <c>await OpenAsync</c> — with a token,
/// without one, and that the rewrite is withheld where <c>await</c> would
/// not compile.
/// </summary>
public class BlockingDbConnectionCodeFixTests
{
    private static CSharpCodeFixTest<
        BlockingDbConnectionAnalyzer,
        BlockingDbConnectionCodeFixProvider,
        DefaultVerifier
    > CreateTest(string testCode, string fixedCode, params DiagnosticResult[] expected)
    {
        var test = new CSharpCodeFixTest<
            BlockingDbConnectionAnalyzer,
            BlockingDbConnectionCodeFixProvider,
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
        new DiagnosticResult("CC045", DiagnosticSeverity.Warning)
            .WithLocation(location)
            .WithArguments("Open");

    [Fact]
    public async Task Open_WithTokenInScope_FlowsTheToken()
    {
        var test =
            @"
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        connection.{|#0:Open|}();
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
    public async Task RunAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        await connection.OpenAsync(cancellationToken);
        await Task.Yield();
    }
}";

        await CreateTest(test, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task Open_WithoutTokenInScope_StillCompiles()
    {
        var test =
            @"
using System.Data.Common;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(DbConnection connection)
    {
        connection.{|#0:Open|}();
        await Task.Yield();
    }
}";

        var fixedCode =
            @"
using System.Data.Common;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(DbConnection connection)
    {
        await connection.OpenAsync();
        await Task.Yield();
    }
}";

        await CreateTest(test, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task Open_OnPropertyReceiver_KeepsTheReceiver()
    {
        var test =
            @"
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

public class Host
{
    public DbConnection Connection { get; set; } = null!;
}

public class TestClass
{
    public async Task RunAsync(Host host, CancellationToken cancellationToken)
    {
        host.Connection.{|#0:Open|}();
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
    public DbConnection Connection { get; set; } = null!;
}

public class TestClass
{
    public async Task RunAsync(Host host, CancellationToken cancellationToken)
    {
        await host.Connection.OpenAsync(cancellationToken);
        await Task.Yield();
    }
}";

        await CreateTest(test, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task Open_NullConditional_ReportsWithoutOfferingAFix()
    {
        var source =
            @"
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(DbConnection? connection, CancellationToken cancellationToken)
    {
        connection?.{|#0:Open|}();
        await Task.Yield();
    }
}";

        await CreateTest(source, source, Expected()).RunAsync();
    }

    [Fact]
    public async Task Open_InsideLock_ReportsWithoutOfferingAFix()
    {
        var source =
            @"
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    private readonly object _gate = new object();

    public async Task RunAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            connection.{|#0:Open|}();
        }

        await Task.Yield();
    }
}";

        await CreateTest(source, source, Expected()).RunAsync();
    }

    [Fact]
    public async Task Open_WithTriviaInsideMemberAccess_KeepsTheComment()
    {
        var test =
            @"
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        connection /* pooled */ .{|#0:Open|}();
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
    public async Task RunAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        await connection /* pooled */ .OpenAsync(cancellationToken);
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
    public async Task RunAsync(DbConnection connection, CancellationToken @event)
    {
        connection.{|#0:Open|}();
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
    public async Task RunAsync(DbConnection connection, CancellationToken @event)
    {
        await connection.OpenAsync(@event);
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
    public async Task<int> RunAsync(DbConnection connection, int[] data, CancellationToken cancellationToken)
    {
        await Task.Yield();
        Span<int> span = data.AsSpan();
        connection.{|#0:Open|}();
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
    public async Task RunAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        Func<string, Task> f = async (cancellationToken) =>
        {
            connection.{|#0:Open|}();
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
    public async Task RunAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        Func<string, Task> f = async (cancellationToken) =>
        {
            await connection.OpenAsync();
            await Task.Yield();
        };

        await f(""x"");
    }
}";

        await CreateTest(test, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task ChainedConditionalAccess_ReportsWithoutOfferingAFix()
    {
        var source =
            @"
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

public class Host
{
    public DbConnection Connection { get; set; } = null!;
}

public class TestClass
{
    public async Task RunAsync(Host? host, CancellationToken cancellationToken)
    {
        host?.Connection.{|#0:Open|}();
        await Task.Yield();
    }
}";

        await CreateTest(source, source, Expected()).RunAsync();
    }

    [Fact]
    public async Task OpenAsyncOverrideOnProvider_StillFixes()
    {
        // SqlConnection and other providers override OpenAsync. The rewritten
        // call binds to that override, not DbConnection.OpenAsync's original
        // definition — the reachability check must walk OverriddenMethod.
        var test =
            @"
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

public class ProviderConnection : DbConnection
{
    public override string ConnectionString { get; set; } = """";
    public override string Database => """";
    public override string DataSource => """";
    public override string ServerVersion => """";
    public override ConnectionState State => ConnectionState.Closed;
    public override void ChangeDatabase(string databaseName) { }
    public override void Close() { }
    public override void Open() { }
    public override Task OpenAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => null!;
    protected override DbCommand CreateDbCommand() => null!;
}

public class TestClass
{
    public async Task RunAsync(ProviderConnection connection, CancellationToken cancellationToken)
    {
        connection.{|#0:Open|}();
        await Task.Yield();
    }
}";

        var fixedCode =
            @"
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

public class ProviderConnection : DbConnection
{
    public override string ConnectionString { get; set; } = """";
    public override string Database => """";
    public override string DataSource => """";
    public override string ServerVersion => """";
    public override ConnectionState State => ConnectionState.Closed;
    public override void ChangeDatabase(string databaseName) { }
    public override void Close() { }
    public override void Open() { }
    public override Task OpenAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => null!;
    protected override DbCommand CreateDbCommand() => null!;
}

public class TestClass
{
    public async Task RunAsync(ProviderConnection connection, CancellationToken cancellationToken)
    {
        await connection.OpenAsync(cancellationToken);
        await Task.Yield();
    }
}";

        await CreateTest(test, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task ExtraOpenAsyncIntOverload_NullConditional_StillReportsWithoutAFix()
    {
        // An unrelated OpenAsync(int) is arity-compatible with the token-taking
        // rewrite. ReachesCounterpart must keep scanning to the framework method.
        var source =
            @"
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

public class ExtraConnection : DbConnection
{
    public override string ConnectionString { get; set; } = """";
    public override string Database => """";
    public override string DataSource => """";
    public override string ServerVersion => """";
    public override ConnectionState State => ConnectionState.Closed;
    public override void ChangeDatabase(string databaseName) { }
    public override void Close() { }
    public override void Open() { }
    public int OpenAsync(int timeout) => 0;
    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => null!;
    protected override DbCommand CreateDbCommand() => null!;
}

public class TestClass
{
    public async Task RunAsync(ExtraConnection? connection, CancellationToken cancellationToken)
    {
        connection?.{|#0:Open|}();
        await Task.Yield();
    }
}";

        await CreateTest(source, source, Expected()).RunAsync();
    }

    [Fact]
    public async Task SubclassHidingBothOpenAsyncOverloads_ShouldNotReportDiagnostic()
    {
        // If neither OpenAsync shape is reachable, the diagnostic's "use OpenAsync"
        // claim is false. CC030 stays quiet in the same situation.
        var test =
            @"
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

public class HiddenConnection : DbConnection
{
    public override string ConnectionString { get; set; } = """";
    public override string Database => """";
    public override string DataSource => """";
    public override string ServerVersion => """";
    public override ConnectionState State => ConnectionState.Closed;
    public override void ChangeDatabase(string databaseName) { }
    public override void Close() { }
    public override void Open() { }
    public new int OpenAsync() => 0;
    public new int OpenAsync(CancellationToken cancellationToken) => 0;
    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => null!;
    protected override DbCommand CreateDbCommand() => null!;
}

public class TestClass
{
    public async Task RunAsync(HiddenConnection connection, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        connection.Open();
        await Task.Yield();
    }
}";

        var t = new CSharpAnalyzerTest<BlockingDbConnectionAnalyzer, DefaultVerifier>
        {
            TestCode = test,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        await t.RunAsync();
    }

    [Fact]
    public async Task FixAll_TwoOpens_BothBecomeAwaitOpenAsync()
    {
        var test =
            @"
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(DbConnection a, DbConnection b, CancellationToken cancellationToken)
    {
        a.{|#0:Open|}();
        b.{|#1:Open|}();
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
    public async Task RunAsync(DbConnection a, DbConnection b, CancellationToken cancellationToken)
    {
        await a.OpenAsync(cancellationToken);
        await b.OpenAsync(cancellationToken);
        await Task.Yield();
    }
}";

        await CreateTest(test, fixedCode, Expected(0), Expected(1)).RunAsync();
    }
}
