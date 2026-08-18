using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace CancelCop.Analyzer.Tests;

/// <summary>
/// CC045: blocking <c>System.Data.Common.DbConnection.Open</c> in async code.
/// CC003 is EF Core only. CC028 is File/Stream. None of the shipped rules
/// see ADO.NET Open — verified empirically.
/// </summary>
public class BlockingDbConnectionTests
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
        new DiagnosticResult("CC045", DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("Open");

    [Fact]
    public async Task Open_InAsyncMethod_IsMissedByEveryShippedRule()
    {
        // Empirical gap: Open parks a pool thread on a database handshake.
        // CC003 is EF Core; CC002 requires a token overload of the invoked
        // method, and Open has none.
        var test =
            @"
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

public class Work
{
    public async Task RunAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        connection.{|#0:Open|}();
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
    public async Task Open_InAsyncLambda_ShouldReportDiagnostic()
    {
        var test =
            @"
using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

public class Work
{
    public void Run(DbConnection connection, CancellationToken cancellationToken)
    {
        Func<Task> work = async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            connection.{|#0:Open|}();
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
    public async Task Open_InSyncLambdaInsideAsyncMethod_ShouldNotReportDiagnostic()
    {
        var test =
            @"
using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

public class Work
{
    public async Task RunAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Action open = () => connection.Open();
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
    public async Task Open_NullConditional_ShouldReportDiagnostic()
    {
        var test =
            @"
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

public class Work
{
    public async Task RunAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        connection?.{|#0:Open|}();
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
    public async Task Open_InSyncMethod_ShouldNotReportDiagnostic()
    {
        var test =
            @"
using System.Data.Common;

public class Work
{
    public void Run(DbConnection connection)
    {
        connection.Open();
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
    public async Task Open_OnOverridingSubclass_ShouldReportDiagnostic()
    {
        var test =
            @"
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

public class MyConnection : DbConnection
{
    public override string ConnectionString { get; set; } = """";
    public override string Database => """";
    public override string DataSource => """";
    public override string ServerVersion => """";
    public override ConnectionState State => ConnectionState.Closed;
    public override void ChangeDatabase(string databaseName) { }
    public override void Close() { }
    public override void Open() { }
    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => null!;
    protected override DbCommand CreateDbCommand() => null!;
}

public class Work
{
    public async Task RunAsync(MyConnection connection, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        connection.{|#0:Open|}();
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
    public async Task Open_HidingNewOnSubclass_ShouldNotReportDiagnostic()
    {
        var test =
            @"
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

public class MidConnection : DbConnection
{
    public override string ConnectionString { get; set; } = """";
    public override string Database => """";
    public override string DataSource => """";
    public override string ServerVersion => """";
    public override ConnectionState State => ConnectionState.Closed;
    public override void ChangeDatabase(string databaseName) { }
    public override void Close() { }
    public override void Open() { }
    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => null!;
    protected override DbCommand CreateDbCommand() => null!;
}

public class HiddenConnection : MidConnection
{
    public new void Open() { }
}

public class Work
{
    public async Task RunAsync(HiddenConnection connection, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        connection.Open();
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
    public async Task LookalikeOpen_ShouldNotReportDiagnostic()
    {
        var test =
            @"
using System.Threading.Tasks;

public class DbConnection
{
    public void Open() { }
}

public class Work
{
    public async Task RunAsync(DbConnection connection, System.Threading.CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        connection.Open();
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
