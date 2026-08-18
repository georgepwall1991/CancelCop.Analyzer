using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace CancelCop.Analyzer.Tests;

/// <summary>
/// CC046: blocking <c>System.Data.Common.DbCommand.ExecuteReader</c> in async
/// code. CC003 is EF Core only. CC045 is <c>DbConnection.Open</c>.
/// <c>ExecuteReader</c> is not virtual — providers hide it with <c>new</c>
/// for a covariant reader — so the rule matches the inheritance chain, not
/// only <c>OverriddenMethod</c>.
/// </summary>
public class BlockingDbCommandTests
{
    private sealed class AllAnalyzersTest
        : CSharpAnalyzerTest<MissingCancellationTokenAnalyzer, DefaultVerifier>
    {
        protected override IEnumerable<DiagnosticAnalyzer> GetDiagnosticAnalyzers() =>
            typeof(MissingCancellationTokenAnalyzer)
                .Assembly.GetTypes()
                .Where(t => !t.IsAbstract && typeof(DiagnosticAnalyzer).IsAssignableFrom(t))
                .Select(t => (DiagnosticAnalyzer)Activator.CreateInstance(t)!);
    }

    private static DiagnosticResult Expected() =>
        new DiagnosticResult("CC046", DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("ExecuteReader");

    private const string MidCommandScaffold =
        @"
using System;
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
    public async Task ExecuteReader_InAsyncMethod_IsMissedByEveryShippedRule()
    {
        // Empirical gap: ExecuteReader parks a pool thread on a query.
        // CC003 is EF Core; CC045 is Open; CC002 requires a token overload
        // of the invoked method, and ExecuteReader has none.
        var test =
            @"
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

public class Work
{
    public async Task RunAsync(DbCommand command, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        command.{|#0:ExecuteReader|}();
        await Task.Yield();
    }
}";

        var t = new AllAnalyzersTest
        {
            TestCode = test,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        t.ExpectedDiagnostics.Add(Expected());
        await t.RunAsync();
    }

    [Fact]
    public async Task ExecuteReader_CommandBehavior_ShouldReportDiagnostic()
    {
        var test =
            @"
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

public class Work
{
    public async Task RunAsync(DbCommand command, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        command.{|#0:ExecuteReader|}(CommandBehavior.Default);
        await Task.Yield();
    }
}";

        var t = new AllAnalyzersTest
        {
            TestCode = test,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        t.ExpectedDiagnostics.Add(Expected());
        await t.RunAsync();
    }

    [Fact]
    public async Task ExecuteReader_InAsyncLambda_ShouldReportDiagnostic()
    {
        var test =
            @"
using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

public class Work
{
    public void Run(DbCommand command, CancellationToken cancellationToken)
    {
        Func<Task> work = async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            command.{|#0:ExecuteReader|}();
            await Task.Yield();
        };
    }
}";

        var t = new AllAnalyzersTest
        {
            TestCode = test,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        t.ExpectedDiagnostics.Add(Expected());
        await t.RunAsync();
    }

    [Fact]
    public async Task ExecuteReader_InSyncLambdaInsideAsyncMethod_ShouldNotReportDiagnostic()
    {
        var test =
            @"
using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

public class Work
{
    public async Task RunAsync(DbCommand command, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Action read = () => command.ExecuteReader();
        await Task.Yield();
    }
}";

        var t = new AllAnalyzersTest
        {
            TestCode = test,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        await t.RunAsync();
    }

    [Fact]
    public async Task ExecuteReader_NullConditional_ShouldReportDiagnostic()
    {
        var test =
            @"
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

public class Work
{
    public async Task RunAsync(DbCommand command, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        command?.{|#0:ExecuteReader|}();
        await Task.Yield();
    }
}";

        var t = new AllAnalyzersTest
        {
            TestCode = test,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        t.ExpectedDiagnostics.Add(Expected());
        await t.RunAsync();
    }

    [Fact]
    public async Task ExecuteReader_InSyncMethod_ShouldNotReportDiagnostic()
    {
        var test =
            @"
using System.Data.Common;

public class Work
{
    public void Run(DbCommand command)
    {
        command.ExecuteReader();
    }
}";

        var t = new AllAnalyzersTest
        {
            TestCode = test,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        await t.RunAsync();
    }

    [Fact]
    public async Task ExecuteReader_HidingNewOnSubclass_ShouldReportDiagnostic()
    {
        // Providers (SqlCommand, NpgsqlCommand, …) hide ExecuteReader with
        // `new` so they can return a covariant reader. That is still the
        // blocking ADO.NET wait — unlike a look-alike type.
        var test =
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

public class HiddenCommand : MidCommand
{
    public new DbDataReader ExecuteReader() => null!;
}

public class Work
{
    public async Task RunAsync(HiddenCommand command, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        command.{|#0:ExecuteReader|}();
        await Task.Yield();
    }
}";

        var t = new AllAnalyzersTest
        {
            TestCode = test,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        t.ExpectedDiagnostics.Add(Expected());
        await t.RunAsync();
    }

    [Fact]
    public async Task ExecuteReader_CovariantReaderHider_ShouldReportDiagnostic()
    {
        // SqlCommand/NpgsqlCommand return a more-derived reader via `new`.
        var test =
            MidCommandScaffold
            + @"
public class CovariantReader : DbDataReader
{
    public override int Depth => 0;
    public override int FieldCount => 0;
    public override bool HasRows => false;
    public override bool IsClosed => true;
    public override int RecordsAffected => 0;
    public override object this[int ordinal] => null!;
    public override object this[string name] => null!;
    public override bool GetBoolean(int ordinal) => false;
    public override byte GetByte(int ordinal) => 0;
    public override long GetBytes(int ordinal, long dataOffset, byte[] buffer, int bufferOffset, int length) => 0;
    public override char GetChar(int ordinal) => '\0';
    public override long GetChars(int ordinal, long dataOffset, char[] buffer, int bufferOffset, int length) => 0;
    public override string GetDataTypeName(int ordinal) => """";
    public override DateTime GetDateTime(int ordinal) => default;
    public override decimal GetDecimal(int ordinal) => 0;
    public override double GetDouble(int ordinal) => 0;
    public override System.Collections.IEnumerator GetEnumerator() => System.Array.Empty<object>().GetEnumerator();
    public override Type GetFieldType(int ordinal) => typeof(object);
    public override float GetFloat(int ordinal) => 0;
    public override Guid GetGuid(int ordinal) => default;
    public override short GetInt16(int ordinal) => 0;
    public override int GetInt32(int ordinal) => 0;
    public override long GetInt64(int ordinal) => 0;
    public override string GetName(int ordinal) => """";
    public override int GetOrdinal(string name) => -1;
    public override string GetString(int ordinal) => """";
    public override object GetValue(int ordinal) => null!;
    public override int GetValues(object[] values) => 0;
    public override bool IsDBNull(int ordinal) => true;
    public override bool NextResult() => false;
    public override bool Read() => false;
}

public class HiddenCommand : MidCommand
{
    public new CovariantReader ExecuteReader() => null!;
}

public class Work
{
    public async Task RunAsync(HiddenCommand command, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        command.{|#0:ExecuteReader|}();
        await Task.Yield();
    }
}";

        var t = new AllAnalyzersTest
        {
            TestCode = test,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        t.ExpectedDiagnostics.Add(Expected());
        await t.RunAsync();
    }

    [Fact]
    public async Task ExecuteReader_NonReaderReturnHider_ShouldNotReportDiagnostic()
    {
        var test =
            MidCommandScaffold
            + @"
public class HiddenCommand : MidCommand
{
    public new int ExecuteReader() => 0;
}

public class Work
{
    public async Task RunAsync(HiddenCommand command, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        command.ExecuteReader();
        await Task.Yield();
    }
}";

        var t = new AllAnalyzersTest
        {
            TestCode = test,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        await t.RunAsync();
    }

    [Fact]
    public async Task ExecuteReader_CustomOptionsOverloadOnSubclass_ShouldNotReportDiagnostic()
    {
        // A provider-specific helper is not DbCommand.ExecuteReader / ExecuteReader(CommandBehavior).
        var test =
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

public class OptionsCommand : MidCommand
{
    public DbDataReader ExecuteReader(int fetchSize) => null!;
}

public class Work
{
    public async Task RunAsync(OptionsCommand command, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        command.ExecuteReader(32);
        await Task.Yield();
    }
}";

        var t = new AllAnalyzersTest
        {
            TestCode = test,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        await t.RunAsync();
    }

    [Fact]
    public async Task ExecuteReader_StaticHelperOnSubclass_ShouldNotReportDiagnostic()
    {
        var test =
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

    public static DbDataReader ExecuteReader(MidCommand command) => null!;
}

public class Work
{
    public async Task RunAsync(MidCommand command, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        MidCommand.ExecuteReader(command);
        await Task.Yield();
    }
}";

        var t = new AllAnalyzersTest
        {
            TestCode = test,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        await t.RunAsync();
    }

    [Fact]
    public async Task LookalikeExecuteReader_ShouldNotReportDiagnostic()
    {
        var test =
            @"
using System.Threading.Tasks;

public class DbCommand
{
    public void ExecuteReader() { }
}

public class Work
{
    public async Task RunAsync(DbCommand command, System.Threading.CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        command.ExecuteReader();
        await Task.Yield();
    }
}";

        var t = new AllAnalyzersTest
        {
            TestCode = test,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        await t.RunAsync();
    }

    [Fact]
    public async Task ExecuteReader_OnIDbCommand_ShouldNotReportDiagnostic()
    {
        // IDbCommand is not claimed — same as CC045 staying quiet on
        // IDbConnection. The interface has no ExecuteReaderAsync.
        var test =
            @"
using System.Data;
using System.Threading;
using System.Threading.Tasks;

public class Work
{
    public async Task RunAsync(IDbCommand command, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        command.ExecuteReader();
        await Task.Yield();
    }
}";

        var t = new AllAnalyzersTest
        {
            TestCode = test,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        await t.RunAsync();
    }

    [Fact]
    public async Task ExecuteReaderAsync_ShouldNotReportDiagnostic()
    {
        var test =
            @"
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

public class Work
{
    public async Task RunAsync(DbCommand command, CancellationToken cancellationToken)
    {
        await command.ExecuteReaderAsync(cancellationToken);
    }
}";

        var t = new AllAnalyzersTest
        {
            TestCode = test,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        await t.RunAsync();
    }

    [Fact]
    public async Task ExecuteNonQuery_ShouldNotReportDiagnostic()
    {
        // Sibling deferred — one method per iteration, matching CC043/CC044.
        var test =
            @"
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

public class Work
{
    public async Task RunAsync(DbCommand command, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        command.ExecuteNonQuery();
        await Task.Yield();
    }
}";

        var t = new AllAnalyzersTest
        {
            TestCode = test,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        await t.RunAsync();
    }
}
