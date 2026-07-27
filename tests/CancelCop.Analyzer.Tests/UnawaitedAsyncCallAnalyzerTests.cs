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

    [Fact]
    public async Task ExpressionBodiedConstructor_ShouldReportDiagnostic()
    {
        // An expression-bodied member has no statement to anchor on, and a constructor cannot be
        // async, so CS4014 is silent here too.
        var test =
            @"
using System.Threading.Tasks;

public class TestClass
{
    public TestClass() => {|#0:InitializeAsync()|};

    private Task InitializeAsync() => Task.CompletedTask;
}";

        var t = Test(test);
        t.ExpectedDiagnostics.Add(Expected("InitializeAsync"));
        await t.RunAsync();
    }

    [Fact]
    public async Task ExpressionBodiedVoidMethod_ShouldReportDiagnostic()
    {
        var test =
            @"
using System.Threading.Tasks;

public class TestClass
{
    public void Run() => {|#0:SaveAsync()|};

    private Task SaveAsync() => Task.CompletedTask;
}";

        var t = Test(test);
        t.ExpectedDiagnostics.Add(Expected("SaveAsync"));
        await t.RunAsync();
    }

    [Fact]
    public async Task ExpressionBodiedTaskReturningMethod_ShouldNotReportDiagnostic()
    {
        // Already covered by ReturnedTask, but pinned separately because the arrow-clause path has
        // its own void check that could regress independently.
        var test =
            @"
using System.Threading.Tasks;

public class TestClass
{
    public Task Run() => SaveAsync();

    private Task SaveAsync() => Task.CompletedTask;
}";

        await Test(test).RunAsync();
    }

    [Fact]
    public async Task NullConditionalCall_ShouldReportDiagnostic()
    {
        // `worker?.StartAsync();` is an invocation wrapped in a conditional access, so a check for a
        // bare InvocationExpression misses it — yet the task is discarded exactly the same.
        var test =
            @"
using System.Threading.Tasks;

public class Worker
{
    public Task StartAsync() => Task.CompletedTask;
}

public class TestClass
{
    private Worker? _worker;

    public void Run()
    {
        {|#0:_worker?.StartAsync()|};
    }
}";

        var t = Test(test);
        t.ExpectedDiagnostics.Add(Expected("StartAsync"));
        await t.RunAsync();
    }

    [Fact]
    public async Task DiscardedConfiguredAwaitable_ShouldReportDiagnostic()
    {
        // ConfigureAwait returns ConfiguredTaskAwaitable rather than a Task, so a Task-only check
        // lets this through — but the underlying task is discarded identically, and the shape looks
        // await-adjacent enough that the omission is easy to miss.
        var test =
            @"
using System.Threading.Tasks;

public class TestClass
{
    private Task SaveAsync() => Task.CompletedTask;

    public void Run()
    {
        {|#0:SaveAsync().ConfigureAwait(false)|};
    }
}";

        var t = Test(test);
        t.ExpectedDiagnostics.Add(Expected("ConfigureAwait"));
        await t.RunAsync();
    }

    [Fact]
    public async Task DiscardedConfiguredValueTaskAwaitable_ShouldReportDiagnostic()
    {
        var test =
            @"
using System.Threading.Tasks;

public class TestClass
{
    private ValueTask SaveAsync() => default;

    public void Run()
    {
        {|#0:SaveAsync().ConfigureAwait(false)|};
    }
}";

        var t = Test(test);
        t.ExpectedDiagnostics.Add(Expected("ConfigureAwait"));
        await t.RunAsync();
    }

    [Fact]
    public async Task DiscardedTaskSubclass_ShouldReportDiagnostic()
    {
        // A Task subclass is still awaitable and still dropped, but an exact-name check sees only
        // the derived name.
        var test =
            @"
using System.Threading.Tasks;

public class DerivedTask : Task
{
    public DerivedTask() : base(() => { }) { }
}

public class TestClass
{
    private DerivedTask StartAsync() => new DerivedTask();

    public void Run()
    {
        {|#0:StartAsync()|};
    }
}";

        var t = Test(test);
        t.ExpectedDiagnostics.Add(Expected("StartAsync"));
        await t.RunAsync();
    }

    [Fact]
    public async Task DiscardedConstrainedTypeParameter_ShouldReportDiagnostic()
    {
        // A type parameter carries its awaitability through constraints rather than a base type.
        var test =
            @"
using System.Threading.Tasks;

public class TestClass
{
    private T StartAsync<T>() where T : Task => default!;

    public void Run()
    {
        {|#0:StartAsync<Task>()|};
    }
}";

        var t = Test(test);
        t.ExpectedDiagnostics.Add(Expected("StartAsync"));
        await t.RunAsync();
    }

    [Fact]
    public async Task DiscardedAwaiter_ShouldReportDiagnostic()
    {
        // GetAwaiter() starts the work and throws the awaiter away; the compiler never reports this
        // in either context.
        var test =
            @"
using System.Threading.Tasks;

public class TestClass
{
    private Task SaveAsync() => Task.CompletedTask;

    public void Run()
    {
        {|#0:SaveAsync().GetAwaiter()|};
    }
}";

        var t = Test(test);
        t.ExpectedDiagnostics.Add(Expected("GetAwaiter"));
        await t.RunAsync();
    }

    [Fact]
    public async Task ExpressionTreeLambda_ShouldNotReportDiagnostic()
    {
        // An expression tree's body is data, not code — it never runs, so nothing is discarded.
        var test =
            @"
using System;
using System.Linq.Expressions;
using System.Threading.Tasks;

public class TestClass
{
    private Task SaveAsync() => Task.CompletedTask;

    public void Run()
    {
        Expression<Action> e = () => SaveAsync();
    }
}";

        await Test(test).RunAsync();
    }

    [Fact]
    public async Task DiscardedAwaiter_InAsyncMethod_StillReportsDiagnostic()
    {
        // CS4014 says nothing about a discarded awaiter in any context, so suppressing CC032 inside
        // async methods would leave this shape unreported by both.
        var test =
            @"
using System.Threading.Tasks;

public class TestClass
{
    private Task SaveAsync() => Task.CompletedTask;

    public async Task RunAsync()
    {
        {|#0:SaveAsync().GetAwaiter()|};
        await Task.Yield();
    }
}";

        var t = Test(test);
        t.ExpectedDiagnostics.Add(Expected("GetAwaiter"));
        await t.RunAsync();
    }

    [Fact]
    public async Task NestedLookalikeAwaiterType_ShouldNotReportDiagnostic()
    {
        // A nested type reports its *outer* type's namespace, so `Outer.TaskAwaiter` declared in
        // System.Runtime.CompilerServices satisfies a namespace-and-name check while being entirely
        // unrelated to async execution. The framework types are top level.
        var test =
            @"
namespace System.Runtime.CompilerServices
{
    public class Outer
    {
        public struct TaskAwaiter { }
    }
}

public class TestClass
{
    private System.Runtime.CompilerServices.Outer.TaskAwaiter Make() => default;

    public void Run()
    {
        Make();
    }
}";

        await Test(test).RunAsync();
    }

    [Fact]
    public async Task DiscardedConfiguredAwaiter_ShouldReportDiagnostic()
    {
        // ConfiguredTaskAwaitable.ConfiguredTaskAwaiter is genuinely nested, so the top-level rule
        // that keeps user lookalikes out would have excluded it too. Neither CS4014 nor anything
        // else reports this chain.
        var test =
            @"
using System.Threading.Tasks;

public class TestClass
{
    private Task SaveAsync() => Task.CompletedTask;

    public void Run()
    {
        {|#0:SaveAsync().ConfigureAwait(false).GetAwaiter()|};
    }
}";

        var t = Test(test);
        t.ExpectedDiagnostics.Add(Expected("GetAwaiter"));
        await t.RunAsync();
    }

    [Fact]
    public async Task UserTaskSubclassNamedTaskAwaiter_DefersToTheCompilerInAsyncCode()
    {
        // The type is a Task, so CS4014 does report it. Classifying it as an awaiter by name alone
        // would emit CC032 alongside the compiler warning, which the rule promises not to do.
        var test =
            @"
using System.Threading.Tasks;

public class TaskAwaiter : Task
{
    public TaskAwaiter() : base(() => { }) { }
}

public class TestClass
{
    private TaskAwaiter StartAsync() => new TaskAwaiter();

    public async Task RunAsync()
    {
        StartAsync();
        await Task.Yield();
    }
}";

        var t = Test(test);
        t.CompilerDiagnostics = CompilerDiagnostics.Errors;
        await t.RunAsync();
    }
}
