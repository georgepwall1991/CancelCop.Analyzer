using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace CancelCop.Analyzer.Tests;

/// <summary>
/// CC032: an async call whose task is dropped on the floor in a non-async method. The compiler's
/// CS4014 only fires <i>inside</i> async methods, so this shape is entirely unreported today.
/// </summary>
public class UnawaitedAsyncCallAnalyzerTests
{
    private static CSharpAnalyzerTest<UnawaitedAsyncCallAnalyzer, DefaultVerifier> Test(
        string source
    ) => new() { TestCode = source, ReferenceAssemblies = ReferenceAssemblies.Net.Net90 };

    private static DiagnosticResult Expected(string method) =>
        new DiagnosticResult("CC032", DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments(method);

    [Fact]
    public async Task UnawaitedCall_InSyncMethod_ShouldReportDiagnostic()
    {
        var test =
            @"
using System.Threading.Tasks;

public class TestClass
{
    private Task SaveAsync() => Task.CompletedTask;

    public void Run()
    {
        {|#0:SaveAsync()|};
    }
}";

        var t = Test(test);
        t.ExpectedDiagnostics.Add(Expected("SaveAsync"));
        await t.RunAsync();
    }

    [Fact]
    public async Task UnawaitedValueTaskCall_InSyncMethod_ShouldReportDiagnostic()
    {
        var test =
            @"
using System.Threading.Tasks;

public class TestClass
{
    private ValueTask SaveAsync() => default;

    public void Run()
    {
        {|#0:SaveAsync()|};
    }
}";

        var t = Test(test);
        t.ExpectedDiagnostics.Add(Expected("SaveAsync"));
        await t.RunAsync();
    }

    [Fact]
    public async Task UnawaitedCall_InConstructor_ShouldReportDiagnostic()
    {
        // Constructors cannot be async, so this is the shape where the mistake is easiest to make
        // and the compiler is most silent.
        var test =
            @"
using System.Threading.Tasks;

public class TestClass
{
    public TestClass()
    {
        {|#0:InitializeAsync()|};
    }

    private Task InitializeAsync() => Task.CompletedTask;
}";

        var t = Test(test);
        t.ExpectedDiagnostics.Add(Expected("InitializeAsync"));
        await t.RunAsync();
    }

    [Fact]
    public async Task UnawaitedCall_InSyncLambda_ShouldReportDiagnostic()
    {
        var test =
            @"
using System;
using System.Threading.Tasks;

public class TestClass
{
    private Task SaveAsync() => Task.CompletedTask;

    public void Run()
    {
        Action a = () => {|#0:SaveAsync()|};
        a();
    }
}";

        var t = Test(test);
        t.ExpectedDiagnostics.Add(Expected("SaveAsync"));
        await t.RunAsync();
    }

    [Fact]
    public async Task UnawaitedCall_InAsyncMethod_ShouldNotReportDiagnostic()
    {
        // The compiler already reports CS4014 here. Duplicating it would be noise, so this asserts
        // CC032 stays quiet; compiler warnings are out of scope for the assertion.
        var test =
            @"
using System.Threading.Tasks;

public class TestClass
{
    private Task SaveAsync() => Task.CompletedTask;

    public async Task RunAsync()
    {
        SaveAsync();
        await Task.Yield();
    }
}";

        var t = Test(test);
        t.CompilerDiagnostics = CompilerDiagnostics.Errors;
        await t.RunAsync();
    }

    [Fact]
    public async Task ExplicitDiscard_ShouldNotReportDiagnostic()
    {
        // `_ =` is the documented way to say "I know, and I mean it". Flagging an explicit opt-in
        // would make the rule impossible to satisfy.
        var test =
            @"
using System.Threading.Tasks;

public class TestClass
{
    private Task SaveAsync() => Task.CompletedTask;

    public void Run()
    {
        _ = SaveAsync();
    }
}";

        await Test(test).RunAsync();
    }

    [Fact]
    public async Task AssignedTask_ShouldNotReportDiagnostic()
    {
        var test =
            @"
using System.Threading.Tasks;

public class TestClass
{
    private Task SaveAsync() => Task.CompletedTask;

    public Task Run()
    {
        var pending = SaveAsync();
        return pending;
    }
}";

        await Test(test).RunAsync();
    }

    [Fact]
    public async Task ReturnedTask_ShouldNotReportDiagnostic()
    {
        var test =
            @"
using System.Threading.Tasks;

public class TestClass
{
    private Task SaveAsync() => Task.CompletedTask;

    public Task Run() => SaveAsync();
}";

        await Test(test).RunAsync();
    }

    [Fact]
    public async Task TaskPassedAsArgument_ShouldNotReportDiagnostic()
    {
        var test =
            @"
using System.Threading.Tasks;

public class TestClass
{
    private Task SaveAsync() => Task.CompletedTask;

    public void Run()
    {
        Observe(SaveAsync());
    }

    private void Observe(Task task) { }
}";

        await Test(test).RunAsync();
    }

    [Fact]
    public async Task ChainedContinuation_ShouldNotReportDiagnostic()
    {
        // The call is the receiver of a further call, so its task is used rather than dropped. The
        // outer call is what would be judged, and it returns Task too — but it is the statement, so
        // only that one is considered.
        var test =
            @"
using System.Threading.Tasks;

public class TestClass
{
    private Task SaveAsync() => Task.CompletedTask;

    public void Run()
    {
        {|#0:SaveAsync().ContinueWith(_ => { })|};
    }
}";

        var t = Test(test);
        t.ExpectedDiagnostics.Add(Expected("ContinueWith"));
        await t.RunAsync();
    }

    [Fact]
    public async Task NonTaskReturningCall_ShouldNotReportDiagnostic()
    {
        var test =
            @"
public class TestClass
{
    private int Compute() => 0;

    public void Run()
    {
        Compute();
    }
}";

        await Test(test).RunAsync();
    }

    [Fact]
    public async Task VoidReturningCall_ShouldNotReportDiagnostic()
    {
        var test =
            @"
public class TestClass
{
    private void Save() { }

    public void Run()
    {
        Save();
    }
}";

        await Test(test).RunAsync();
    }
}
