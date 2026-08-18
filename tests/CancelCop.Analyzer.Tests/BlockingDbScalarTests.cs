using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace CancelCop.Analyzer.Tests;

/// <summary>
/// CC048: blocking <c>System.Data.Common.DbCommand.ExecuteScalar</c> in
/// async code. CC046 is ExecuteReader; CC047 is ExecuteNonQuery.
/// ExecuteScalar is abstract; overrides and <c>new</c> hiders match by
/// inheritance plus the framework shape.
/// </summary>
public class BlockingDbScalarTests
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
        new DiagnosticResult("CC048", DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("ExecuteScalar");

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
    public async Task ExecuteScalar_InAsyncMethod_IsMissedByEveryShippedRule()
    {
        // Empirical gap: ExecuteScalar parks a pool thread on a single-value
        // query. CC003 is EF Core; CC046/CC047 are ExecuteReader/NonQuery;
        // CC002 requires a token overload of the invoked method.
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
        command.{|#0:ExecuteScalar|}();
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
    public async Task ExecuteScalar_InAsyncLambda_ShouldReportDiagnostic()
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
            command.{|#0:ExecuteScalar|}();
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
    public async Task ExecuteScalar_InSyncLambdaInsideAsyncMethod_ShouldNotReportDiagnostic()
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
        Action exec = () => command.ExecuteScalar();
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
    public async Task ExecuteScalar_NullConditional_ShouldReportDiagnostic()
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
        command?.{|#0:ExecuteScalar|}();
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
    public async Task ExecuteScalar_InSyncMethod_ShouldNotReportDiagnostic()
    {
        var test =
            @"
using System.Data.Common;

public class Work
{
    public void Run(DbCommand command)
    {
        command.ExecuteScalar();
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
    public async Task ExecuteScalar_OnOverridingSubclass_ShouldReportDiagnostic()
    {
        var test =
            MidCommandScaffold
            + @"
public class Work
{
    public async Task RunAsync(MidCommand command, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        command.{|#0:ExecuteScalar|}();
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
    public async Task ExecuteScalar_HidingNewOnSubclass_ShouldReportDiagnostic()
    {
        var test =
            MidCommandScaffold
            + @"
public class HiddenCommand : MidCommand
{
    public new object ExecuteScalar() => null!;
}

public class Work
{
    public async Task RunAsync(HiddenCommand command, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        command.{|#0:ExecuteScalar|}();
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
    public async Task ExecuteScalar_StringReturnHider_ShouldReportDiagnostic()
    {
        // A more-derived return is still the blocking scalar wait.
        var test =
            MidCommandScaffold
            + @"
public class HiddenCommand : MidCommand
{
    public new string ExecuteScalar() => """";
}

public class Work
{
    public async Task RunAsync(HiddenCommand command, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        command.{|#0:ExecuteScalar|}();
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
    public async Task ExecuteScalar_CustomOverloadOnSubclass_ShouldNotReportDiagnostic()
    {
        var test =
            MidCommandScaffold
            + @"
public class OptionsCommand : MidCommand
{
    public object ExecuteScalar(int timeout) => null!;
}

public class Work
{
    public async Task RunAsync(OptionsCommand command, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        command.ExecuteScalar(30);
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
    public async Task ExecuteScalar_GenericHelperOnSubclass_ShouldNotReportDiagnostic()
    {
        var test =
            MidCommandScaffold
            + @"
public class HiddenCommand : MidCommand
{
    public object ExecuteScalar<T>() => null!;
}

public class Work
{
    public async Task RunAsync(HiddenCommand command, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        command.ExecuteScalar<int>();
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
    public async Task ExecuteScalar_StaticHelperOnSubclass_ShouldNotReportDiagnostic()
    {
        var test =
            MidCommandScaffold
            + @"
public class HiddenCommand : MidCommand
{
    public static new object ExecuteScalar() => null!;
}

public class Work
{
    public async Task RunAsync(HiddenCommand command, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        HiddenCommand.ExecuteScalar();
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
    public async Task ExecuteScalar_TaskDerivedReturnHider_ShouldNotReportDiagnostic()
    {
        var test =
            MidCommandScaffold
            + @"
public class MyTask : Task<object>
{
    public MyTask() : base(() => null!) { }
}

public class HiddenCommand : MidCommand
{
    public new MyTask ExecuteScalar() => new MyTask();
}

public class Work
{
    public async Task RunAsync(HiddenCommand command, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await command.ExecuteScalar();
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
    public async Task ExecuteScalar_AwaitableReturnHider_ShouldNotReportDiagnostic()
    {
        // A new Task<object> ExecuteScalar() is already async-shaped; it
        // is not the blocking framework wait.
        var test =
            MidCommandScaffold
            + @"
public class HiddenCommand : MidCommand
{
    public new Task<object> ExecuteScalar() => Task.FromResult<object>(null!);
}

public class Work
{
    public async Task RunAsync(HiddenCommand command, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await command.ExecuteScalar();
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
    public async Task ExecuteScalar_VoidReturnHider_ShouldNotReportDiagnostic()
    {
        var test =
            MidCommandScaffold
            + @"
public class HiddenCommand : MidCommand
{
    public new void ExecuteScalar() { }
}

public class Work
{
    public async Task RunAsync(HiddenCommand command, CancellationToken cancellationToken)
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

    [Fact]
    public async Task LookalikeExecuteScalar_ShouldNotReportDiagnostic()
    {
        var test =
            @"
using System.Threading.Tasks;

public class DbCommand
{
    public object ExecuteScalar() => null!;
}

public class Work
{
    public async Task RunAsync(DbCommand command, System.Threading.CancellationToken cancellationToken)
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

    [Fact]
    public async Task ExecuteScalar_OnIDbCommand_ShouldNotReportDiagnostic()
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

    [Fact]
    public async Task ExecuteScalarAsync_ShouldNotReportDiagnostic()
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
        await command.ExecuteScalarAsync(cancellationToken);
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
