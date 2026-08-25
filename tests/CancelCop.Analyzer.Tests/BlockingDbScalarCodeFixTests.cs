using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace CancelCop.Analyzer.Tests;

/// <summary>
/// CC048 fixer: rewritten code is compiled by the harness.
/// <c>ExecuteScalar()</c> → <c>await ExecuteScalarAsync</c>.
/// </summary>
public class BlockingDbScalarCodeFixTests
{
    private static CSharpCodeFixTest<
        BlockingDbScalarAnalyzer,
        BlockingDbScalarCodeFixProvider,
        DefaultVerifier
    > CreateTest(string testCode, string fixedCode, params DiagnosticResult[] expected)
    {
        var test = new CSharpCodeFixTest<
            BlockingDbScalarAnalyzer,
            BlockingDbScalarCodeFixProvider,
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
        new DiagnosticResult("CC048", DiagnosticSeverity.Warning)
            .WithLocation(location)
            .WithArguments("ExecuteScalar");

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
    public async Task ExecuteScalar_WithTokenInScope_FlowsTheToken()
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
        command.{|#0:ExecuteScalar|}();
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
        await command.ExecuteScalarAsync(cancellationToken);
        await Task.Yield();
    }
}";

        await CreateTest(test, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task ExecuteScalar_WithoutTokenInScope_StillCompiles()
    {
        var test =
            @"
using System.Data.Common;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(DbCommand command)
    {
        command.{|#0:ExecuteScalar|}();
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
        await command.ExecuteScalarAsync();
        await Task.Yield();
    }
}";

        await CreateTest(test, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task ExecuteScalar_NullConditional_HoistsToIfNotNullExecuteScalarAsync()
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
        command?.{|#0:ExecuteScalar|}();
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
            await command.ExecuteScalarAsync(cancellationToken);
        }
        await Task.Yield();
    }
}";

        await CreateTest(source, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task ExecuteScalar_InsideLock_ReportsWithoutOfferingAFix()
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
            command.{|#0:ExecuteScalar|}();
        }

        await Task.Yield();
    }
}";

        await CreateTest(source, source, Expected()).RunAsync();
    }

    [Fact]
    public async Task NullForgivingOnResult_ParenthesizesAwait()
    {
        var test =
            @"
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task<object> RunAsync(DbCommand command, CancellationToken cancellationToken)
    {
        return command.{|#0:ExecuteScalar|}()!;
    }
}";

        var fixedCode =
            @"
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task<object> RunAsync(DbCommand command, CancellationToken cancellationToken)
    {
        return (await command.ExecuteScalarAsync(cancellationToken))!;
    }
}";

        await CreateTest(test, fixedCode, Expected()).RunAsync();
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
        return command.{|#0:ExecuteScalar|}()!.ToString();
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
        return (await command.ExecuteScalarAsync(cancellationToken))!.ToString();
    }
}";

        await CreateTest(test, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task ExecuteScalarAsyncOverrideOnProvider_StillFixes()
    {
        var test =
            MidCommandScaffold
            + @"
public class ProviderCommand : MidCommand
{
    public override Task<object> ExecuteScalarAsync(CancellationToken cancellationToken)
        => Task.FromResult<object>(null!);
}

public class TestClass
{
    public async Task RunAsync(ProviderCommand command, CancellationToken cancellationToken)
    {
        command.{|#0:ExecuteScalar|}();
        await Task.Yield();
    }
}";

        var fixedCode =
            MidCommandScaffold
            + @"
public class ProviderCommand : MidCommand
{
    public override Task<object> ExecuteScalarAsync(CancellationToken cancellationToken)
        => Task.FromResult<object>(null!);
}

public class TestClass
{
    public async Task RunAsync(ProviderCommand command, CancellationToken cancellationToken)
    {
        await command.ExecuteScalarAsync(cancellationToken);
        await Task.Yield();
    }
}";

        await CreateTest(test, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task ExplicitCastToString_StillFixes()
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
        return (string)command.{|#0:ExecuteScalar|}();
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
        return (string)await command.ExecuteScalarAsync(cancellationToken);
    }
}";

        await CreateTest(test, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task NarrowedStringScalar_Assigned_ReportsWithoutOfferingAFix()
    {
        // string ExecuteScalar() used as string cannot become
        // await ExecuteScalarAsync() if that binds to Task<object>.
        var source =
            MidCommandScaffold
            + @"
public class ProviderCommand : MidCommand
{
    public new string ExecuteScalar() => """";
}

public class TestClass
{
    public async Task<string> RunAsync(ProviderCommand command, CancellationToken cancellationToken)
    {
        string value = command.{|#0:ExecuteScalar|}();
        await Task.Yield();
        return value;
    }
}";

        await CreateTest(source, source, Expected()).RunAsync();
    }

    [Fact]
    public async Task UnrelatedParameterlessStringTap_ReturnStillFixes()
    {
        // A leftover Task<string> ExecuteScalarAsync() must not block
        // the token-taking Task<object> rewrite on a value use.
        var test =
            MidCommandScaffold
            + @"
public class ProviderCommand : MidCommand
{
    public new Task<string> ExecuteScalarAsync() => Task.FromResult(string.Empty);
}

public class TestClass
{
    public async Task<object> RunAsync(ProviderCommand command, CancellationToken cancellationToken)
    {
        return command.{|#0:ExecuteScalar|}();
    }
}";

        var fixedCode =
            MidCommandScaffold
            + @"
public class ProviderCommand : MidCommand
{
    public new Task<string> ExecuteScalarAsync() => Task.FromResult(string.Empty);
}

public class TestClass
{
    public async Task<object> RunAsync(ProviderCommand command, CancellationToken cancellationToken)
    {
        return await command.ExecuteScalarAsync(cancellationToken);
    }
}";

        await CreateTest(test, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task CovariantTaskStringHider_AsOverloadedArgument_FallsBackToParameterless()
    {
        // The token-taking Task<string> hider would retarget Log(object)
        // to Log(string). The parameterless framework TAP still returns
        // object, so the rewrite drops the token instead of changing
        // overload resolution.
        var test =
            MidCommandScaffold
            + @"
public class ProviderCommand : MidCommand
{
    public new Task<string> ExecuteScalarAsync(CancellationToken cancellationToken)
        => Task.FromResult(string.Empty);
}

public class TestClass
{
    public async Task RunAsync(ProviderCommand command, CancellationToken cancellationToken)
    {
        Log(command.{|#0:ExecuteScalar|}());
        await Task.Yield();
    }

    static void Log(object value) { }
    static void Log(string value) { }
}";

        var fixedCode =
            MidCommandScaffold
            + @"
public class ProviderCommand : MidCommand
{
    public new Task<string> ExecuteScalarAsync(CancellationToken cancellationToken)
        => Task.FromResult(string.Empty);
}

public class TestClass
{
    public async Task RunAsync(ProviderCommand command, CancellationToken cancellationToken)
    {
        Log(await command.ExecuteScalarAsync());
        await Task.Yield();
    }

    static void Log(object value) { }
    static void Log(string value) { }
}";

        await CreateTest(test, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task CovariantTaskStringHider_StillFixes()
    {
        var test =
            MidCommandScaffold
            + @"
public class ProviderCommand : MidCommand
{
    public new Task<string> ExecuteScalarAsync(CancellationToken cancellationToken)
        => Task.FromResult(string.Empty);
}

public class TestClass
{
    public async Task RunAsync(ProviderCommand command, CancellationToken cancellationToken)
    {
        command.{|#0:ExecuteScalar|}();
        await Task.Yield();
    }
}";

        var fixedCode =
            MidCommandScaffold
            + @"
public class ProviderCommand : MidCommand
{
    public new Task<string> ExecuteScalarAsync(CancellationToken cancellationToken)
        => Task.FromResult(string.Empty);
}

public class TestClass
{
    public async Task RunAsync(ProviderCommand command, CancellationToken cancellationToken)
    {
        await command.ExecuteScalarAsync(cancellationToken);
        await Task.Yield();
    }
}";

        await CreateTest(test, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task ProtectedHiders_NullConditional_StillReportsWithoutAFix()
    {
        var source =
            MidCommandScaffold
            + @"
public class HiddenCommand : MidCommand
{
    protected new Task<object> ExecuteScalarAsync() => Task.FromResult<object>(null!);
    protected new Task<object> ExecuteScalarAsync(CancellationToken cancellationToken)
        => Task.FromResult<object>(null!);
}

public class TestClass
{
    public async Task RunAsync(HiddenCommand? command, CancellationToken cancellationToken)
    {
        command?.{|#0:ExecuteScalar|}();
        await Task.Yield();
    }
}";

        await CreateTest(source, source, Expected()).RunAsync();
    }

    [Fact]
    public async Task ExtraExecuteScalarAsyncIntOverload_NullConditional_StillReportsWithoutAFix()
    {
        var source =
            MidCommandScaffold
            + @"
public class ExtraCommand : MidCommand
{
    public int ExecuteScalarAsync(int timeout) => 0;
}

public class TestClass
{
    public async Task RunAsync(ExtraCommand? command, CancellationToken cancellationToken)
    {
        command?.{|#0:ExecuteScalar|}();
        await Task.Yield();
    }
}";

        await CreateTest(source, source, Expected()).RunAsync();
    }

    [Fact]
    public async Task ProtectedHiders_ExternalCaller_StillFixesViaPublicBase()
    {
        // A protected `new` hider is not in the external lookup set, so it
        // does not hide the public framework TAP. The rewrite still binds.
        var test =
            MidCommandScaffold
            + @"
public class HiddenCommand : MidCommand
{
    protected new Task<object> ExecuteScalarAsync() => Task.FromResult<object>(null!);
    protected new Task<object> ExecuteScalarAsync(CancellationToken cancellationToken)
        => Task.FromResult<object>(null!);
}

public class TestClass
{
    public async Task RunAsync(HiddenCommand command, CancellationToken cancellationToken)
    {
        command.{|#0:ExecuteScalar|}();
        await Task.Yield();
    }
}";

        var fixedCode =
            MidCommandScaffold
            + @"
public class HiddenCommand : MidCommand
{
    protected new Task<object> ExecuteScalarAsync() => Task.FromResult<object>(null!);
    protected new Task<object> ExecuteScalarAsync(CancellationToken cancellationToken)
        => Task.FromResult<object>(null!);
}

public class TestClass
{
    public async Task RunAsync(HiddenCommand command, CancellationToken cancellationToken)
    {
        await command.ExecuteScalarAsync(cancellationToken);
        await Task.Yield();
    }
}";

        await CreateTest(test, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task SubclassHidingBothExecuteScalarAsyncOverloads_ShouldNotReportDiagnostic()
    {
        var test =
            MidCommandScaffold
            + @"
public class HiddenCommand : MidCommand
{
    public new int ExecuteScalarAsync() => 0;
    public new int ExecuteScalarAsync(CancellationToken cancellationToken) => 0;
}

public class TestClass
{
    public async Task RunAsync(HiddenCommand command, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        command.ExecuteScalar();
        await Task.Yield();
    }
}";

        var t = new CSharpAnalyzerTest<BlockingDbScalarAnalyzer, DefaultVerifier>
        {
            TestCode = test,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        await t.RunAsync();
    }

    [Fact]
    public async Task OptionalIntExecuteScalarAsync_AfterHidingFrameworkShapes_ShouldNotReport()
    {
        var test =
            MidCommandScaffold
            + @"
public class HiddenCommand : MidCommand
{
    public new int ExecuteScalarAsync() => 0;
    public new int ExecuteScalarAsync(CancellationToken cancellationToken) => 0;
    public Task<int> ExecuteScalarAsync(int timeout = 30) => Task.FromResult(0);
}

public class TestClass
{
    public async Task RunAsync(HiddenCommand command, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        command.ExecuteScalar();
        await Task.Yield();
    }
}";

        var t = new CSharpAnalyzerTest<BlockingDbScalarAnalyzer, DefaultVerifier>
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
    public new int ExecuteScalarAsync() => 0;
}

public class TestClass
{
    public async Task RunAsync(HiddenCommand command)
    {
        command.{|#0:ExecuteScalar|}();
        await Task.Yield();
    }
}";

        await CreateTest(source, source, Expected()).RunAsync();
    }

    [Fact]
    public async Task CallFromExecuteScalarAsyncOverride_ReportsWithoutOfferingAFix()
    {
        var source =
            MidCommandScaffold
            + @"
public class ProviderCommand : MidCommand
{
    public override async Task<object> ExecuteScalarAsync(CancellationToken cancellationToken)
    {
        return {|#0:ExecuteScalar|}();
    }
}";

        await CreateTest(source, source, Expected()).RunAsync();
    }

    [Fact]
    public async Task BaseExecuteScalarFromOverride_ReportsWithoutOfferingAFix()
    {
        var source =
            MidCommandScaffold
            + @"
public class ProviderCommand : MidCommand
{
    public override async Task<object> ExecuteScalarAsync(CancellationToken cancellationToken)
    {
        return base.{|#0:ExecuteScalar|}();
    }
}";

        await CreateTest(source, source, Expected()).RunAsync();
    }

    [Fact]
    public async Task OtherCommandAliasedFromThis_ReportsWithoutOfferingAFix()
    {
        // DbCommand other = this; is still this. Rewriting to
        // await other.ExecuteScalarAsync would re-enter this override.
        var source =
            MidCommandScaffold
            + @"
public class ProviderCommand : MidCommand
{
    public override async Task<object> ExecuteScalarAsync(CancellationToken cancellationToken)
    {
        DbCommand other = this;
        other.{|#0:ExecuteScalar|}();
        return 0;
    }
}";

        await CreateTest(source, source, Expected()).RunAsync();
    }

    [Fact]
    public async Task ThisAssignedThenCalled_ReportsWithoutOfferingAFix()
    {
        var source =
            MidCommandScaffold
            + @"
public class ProviderCommand : MidCommand
{
    public override async Task<object> ExecuteScalarAsync(CancellationToken cancellationToken)
    {
        DbCommand other;
        other = this;
        other.{|#0:ExecuteScalar|}();
        return 0;
    }
}";

        await CreateTest(source, source, Expected()).RunAsync();
    }

    [Fact]
    public async Task CastThisFromOverride_ReportsWithoutOfferingAFix()
    {
        var source =
            MidCommandScaffold
            + @"
public class ProviderCommand : MidCommand
{
    public override async Task<object> ExecuteScalarAsync(CancellationToken cancellationToken)
    {
        return ((DbCommand)this).{|#0:ExecuteScalar|}();
    }
}";

        await CreateTest(source, source, Expected()).RunAsync();
    }

    [Fact]
    public async Task TransitiveLocalAlias_ReportsWithoutOfferingAFix()
    {
        var source =
            MidCommandScaffold
            + @"
public class ProviderCommand : MidCommand
{
    public override async Task<object> ExecuteScalarAsync(CancellationToken cancellationToken)
    {
        DbCommand a = this;
        DbCommand b = a;
        return b.{|#0:ExecuteScalar|}();
    }
}";

        await CreateTest(source, source, Expected()).RunAsync();
    }

    [Fact]
    public async Task FieldAssignedThisInConstructor_ReportsWithoutOfferingAFix()
    {
        var source =
            MidCommandScaffold
            + @"
public class ProviderCommand : MidCommand
{
    private DbCommand _self;

    public ProviderCommand()
    {
        _self = this;
    }

    public override async Task<object> ExecuteScalarAsync(CancellationToken cancellationToken)
    {
        return _self.{|#0:ExecuteScalar|}();
    }
}";

        await CreateTest(source, source, Expected()).RunAsync();
    }

    [Fact]
    public async Task ThisDotFieldAssignedThis_ReportsWithoutOfferingAFix()
    {
        var source =
            MidCommandScaffold
            + @"
public class ProviderCommand : MidCommand
{
    private DbCommand _self;

    public ProviderCommand()
    {
        this._self = this;
    }

    public override async Task<object> ExecuteScalarAsync(CancellationToken cancellationToken)
    {
        return this._self.{|#0:ExecuteScalar|}();
    }
}";

        await CreateTest(source, source, Expected()).RunAsync();
    }

    [Fact]
    public async Task PropertyReturningThis_ReportsWithoutOfferingAFix()
    {
        var source =
            MidCommandScaffold
            + @"
public class ProviderCommand : MidCommand
{
    private DbCommand Self => this;

    public override async Task<object> ExecuteScalarAsync(CancellationToken cancellationToken)
    {
        return Self.{|#0:ExecuteScalar|}();
    }
}";

        await CreateTest(source, source, Expected()).RunAsync();
    }

    [Fact]
    public async Task LocalFunctionCapturingThisAlias_ReportsWithoutOfferingAFix()
    {
        var source =
            MidCommandScaffold
            + @"
public class ProviderCommand : MidCommand
{
    public override async Task<object> ExecuteScalarAsync(CancellationToken cancellationToken)
    {
        DbCommand other = this;
        async Task<object> Inner()
        {
            return other.{|#0:ExecuteScalar|}();
        }

        return await Inner();
    }
}";

        await CreateTest(source, source, Expected()).RunAsync();
    }

    [Fact]
    public async Task LambdaCapturingThisAlias_ReportsWithoutOfferingAFix()
    {
        var source =
            MidCommandScaffold
            + @"
public class ProviderCommand : MidCommand
{
    public override async Task<object> ExecuteScalarAsync(CancellationToken cancellationToken)
    {
        DbCommand other = this;
        var inner = async () => other.{|#0:ExecuteScalar|}();
        return await inner();
    }
}";

        await CreateTest(source, source, Expected()).RunAsync();
    }

    [Fact]
    public async Task OtherCommandParameterFromOverride_StillFixes()
    {
        var test =
            MidCommandScaffold
            + @"
public class ProviderCommand : MidCommand
{
    public override async Task<object> ExecuteScalarAsync(CancellationToken cancellationToken)
    {
        return Helper(this, cancellationToken);
    }

    private async Task<object> Helper(DbCommand other, CancellationToken cancellationToken)
    {
        return other.{|#0:ExecuteScalar|}();
    }
}";

        var fixedCode =
            MidCommandScaffold
            + @"
public class ProviderCommand : MidCommand
{
    public override async Task<object> ExecuteScalarAsync(CancellationToken cancellationToken)
    {
        return Helper(this, cancellationToken);
    }

    private async Task<object> Helper(DbCommand other, CancellationToken cancellationToken)
    {
        return await other.ExecuteScalarAsync(cancellationToken);
    }
}";

        await CreateTest(test, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task UnrelatedLocalFromOverride_StillFixes()
    {
        var test =
            MidCommandScaffold
            + @"
public class ProviderCommand : MidCommand
{
    public override async Task<object> ExecuteScalarAsync(CancellationToken cancellationToken)
    {
        DbCommand other = Connection.CreateCommand();
        return other.{|#0:ExecuteScalar|}();
    }
}";

        var fixedCode =
            MidCommandScaffold
            + @"
public class ProviderCommand : MidCommand
{
    public override async Task<object> ExecuteScalarAsync(CancellationToken cancellationToken)
    {
        DbCommand other = Connection.CreateCommand();
        return await other.ExecuteScalarAsync(cancellationToken);
    }
}";

        await CreateTest(test, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task FixAll_TwoScalars_BothBecomeAwaitExecuteScalarAsync()
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
        a.{|#0:ExecuteScalar|}();
        b.{|#1:ExecuteScalar|}();
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
        await a.ExecuteScalarAsync(cancellationToken);
        await b.ExecuteScalarAsync(cancellationToken);
        await Task.Yield();
    }
}";

        await CreateTest(test, fixedCode, Expected(0), Expected(1)).RunAsync();
    }

    [Fact]
    public async Task ConditionalExecuteScalar_HoistsToIfNotNullExecuteScalarAsync()
    {
        var test =
            @"
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(DbCommand? command, CancellationToken cancellationToken)
    {
        await Task.Yield();
        command?.{|#0:ExecuteScalar|}();
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
        await Task.Yield();
        if (command is not null)
        {
            await command.ExecuteScalarAsync(cancellationToken);
        }
        await Task.Yield();
    }
}";

        var expected = new DiagnosticResult("CC048", DiagnosticSeverity.Warning).WithLocation(0);
        await CreateTest(test, fixedCode, expected).RunAsync();
    }

    [Fact]
    public async Task ChainedConditionalExecuteScalar_HoistsWithSplicedCommand()
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
    public async Task RunAsync(Host? host, CancellationToken cancellationToken)
    {
        await Task.Yield();
        host?.Command.{|#0:ExecuteScalar|}();
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
        await Task.Yield();
        if (host is not null)
        {
            await host.Command.ExecuteScalarAsync(cancellationToken);
        }
        await Task.Yield();
    }
}";

        var expected = new DiagnosticResult("CC048", DiagnosticSeverity.Warning).WithLocation(0);
        await CreateTest(test, fixedCode, expected).RunAsync();
    }

    [Fact]
    public async Task ConditionalExecuteScalarAsReturnedValue_ReportsWithoutOfferingAFix()
    {
        // Only a whole null-conditional statement can be hoisted; as a returned value the
        // diagnostic stands without a rewrite.
        var test =
            @"
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task<object?> RunAsync(DbCommand? command)
    {
        await Task.Yield();
        return command?.{|#0:ExecuteScalar|}();
    }
}";

        var expected = new DiagnosticResult("CC048", DiagnosticSeverity.Warning).WithLocation(0);
        await CreateTest(test, test, expected).RunAsync();
    }
}
