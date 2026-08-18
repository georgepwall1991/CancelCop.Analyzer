using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace CancelCop.Analyzer.Tests;

/// <summary>
/// CC047: blocking <c>System.Data.Common.DbCommand.ExecuteNonQuery</c> in
/// async code. CC046 is ExecuteReader only. ExecuteNonQuery is abstract,
/// but the rule still matches inheritance plus the framework shape so
/// <c>new</c> hiders report and generic/custom helpers stay quiet.
/// </summary>
public class BlockingDbNonQueryTests
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
        new DiagnosticResult("CC047", DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("ExecuteNonQuery");

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
    public async Task ExecuteNonQuery_InAsyncMethod_IsMissedByEveryShippedRule()
    {
        // Empirical gap: ExecuteNonQuery parks a pool thread on a command.
        // CC003 is EF Core; CC046 is ExecuteReader; CC002 requires a token
        // overload of the invoked method, and ExecuteNonQuery has none.
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
        command.{|#0:ExecuteNonQuery|}();
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
    public async Task ExecuteNonQuery_InAsyncLambda_ShouldReportDiagnostic()
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
            command.{|#0:ExecuteNonQuery|}();
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
    public async Task ExecuteNonQuery_InSyncLambdaInsideAsyncMethod_ShouldNotReportDiagnostic()
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
        Action exec = () => command.ExecuteNonQuery();
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
    public async Task ExecuteNonQuery_NullConditional_ShouldReportDiagnostic()
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
        command?.{|#0:ExecuteNonQuery|}();
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
    public async Task ExecuteNonQuery_InSyncMethod_ShouldNotReportDiagnostic()
    {
        var test =
            @"
using System.Data.Common;

public class Work
{
    public void Run(DbCommand command)
    {
        command.ExecuteNonQuery();
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
    public async Task ExecuteNonQuery_OnOverridingSubclass_ShouldReportDiagnostic()
    {
        var test =
            MidCommandScaffold
            + @"
public class Work
{
    public async Task RunAsync(MidCommand command, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        command.{|#0:ExecuteNonQuery|}();
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
    public async Task ExecuteNonQuery_HidingNewOnSubclass_ShouldReportDiagnostic()
    {
        var test =
            MidCommandScaffold
            + @"
public class HiddenCommand : MidCommand
{
    public new int ExecuteNonQuery() => 0;
}

public class Work
{
    public async Task RunAsync(HiddenCommand command, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        command.{|#0:ExecuteNonQuery|}();
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
    public async Task ExecuteNonQuery_CustomOverloadOnSubclass_ShouldNotReportDiagnostic()
    {
        var test =
            MidCommandScaffold
            + @"
public class OptionsCommand : MidCommand
{
    public int ExecuteNonQuery(int timeout) => 0;
}

public class Work
{
    public async Task RunAsync(OptionsCommand command, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        command.ExecuteNonQuery(30);
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
    public async Task ExecuteNonQuery_StaticHelperOnSubclass_ShouldNotReportDiagnostic()
    {
        var test =
            MidCommandScaffold
            + @"
public class HiddenCommand : MidCommand
{
    public static new int ExecuteNonQuery() => 0;
}

public class Work
{
    public async Task RunAsync(HiddenCommand command, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        HiddenCommand.ExecuteNonQuery();
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
    public async Task ExecuteNonQuery_NonIntReturnHider_ShouldNotReportDiagnostic()
    {
        var test =
            MidCommandScaffold
            + @"
public class HiddenCommand : MidCommand
{
    public new long ExecuteNonQuery() => 0;
}

public class Work
{
    public async Task RunAsync(HiddenCommand command, CancellationToken cancellationToken)
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

    [Fact]
    public async Task ExecuteNonQuery_GenericHelperOnSubclass_ShouldNotReportDiagnostic()
    {
        var test =
            MidCommandScaffold
            + @"
public class HiddenCommand : MidCommand
{
    public int ExecuteNonQuery<T>() => 0;
}

public class Work
{
    public async Task RunAsync(HiddenCommand command, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        command.ExecuteNonQuery<int>();
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
    public async Task LookalikeExecuteNonQuery_ShouldNotReportDiagnostic()
    {
        var test =
            @"
using System.Threading.Tasks;

public class DbCommand
{
    public int ExecuteNonQuery() => 0;
}

public class Work
{
    public async Task RunAsync(DbCommand command, System.Threading.CancellationToken cancellationToken)
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

    [Fact]
    public async Task ExecuteNonQuery_OnIDbCommand_ShouldNotReportDiagnostic()
    {
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

    [Fact]
    public async Task ExecuteNonQueryAsync_ShouldNotReportDiagnostic()
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
        await command.ExecuteNonQueryAsync(cancellationToken);
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
    public async Task ExecuteScalar_ShouldNotReportDiagnostic()
    {
        // Sibling deferred — one method per iteration.
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
        command.ExecuteScalar();
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
