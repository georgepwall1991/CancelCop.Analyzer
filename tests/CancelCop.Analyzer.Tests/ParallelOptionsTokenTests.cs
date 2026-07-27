using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace CancelCop.Analyzer.Tests;

/// <summary>
/// CC034: <c>ParallelOptions.CancellationToken</c> is the only way to cancel a <c>Parallel</c> loop,
/// and it is set through an object initializer rather than an argument — so CC002, which matches
/// calls with token-accepting overloads, has nothing to see. <c>Parallel.ForEach</c> has no
/// token-taking overload at all.
/// </summary>
public class ParallelOptionsTokenTests
{
    private static CSharpAnalyzerTest<ParallelOptionsTokenAnalyzer, DefaultVerifier> Test(
        string source
    ) => new() { TestCode = source, ReferenceAssemblies = ReferenceAssemblies.Net.Net90 };

    private static DiagnosticResult Expected(string token = "cancellationToken") =>
        new DiagnosticResult("CC034", DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments(token);

    [Fact]
    public async Task InitializerWithoutToken_ShouldReportDiagnostic()
    {
        var test =
            @"
using System.Threading;
using System.Threading.Tasks;

public class Runner
{
    public void Run(int[] items, CancellationToken cancellationToken)
    {
        var options = {|#0:new ParallelOptions { MaxDegreeOfParallelism = 4 }|};
        Parallel.ForEach(items, options, i => { });
    }
}";

        var t = Test(test);
        t.ExpectedDiagnostics.Add(Expected());
        await t.RunAsync();
    }

    [Fact]
    public async Task NoInitializer_ShouldReportDiagnostic()
    {
        var test =
            @"
using System.Threading;
using System.Threading.Tasks;

public class Runner
{
    public void Run(int[] items, CancellationToken cancellationToken)
    {
        var options = {|#0:new ParallelOptions()|};
        Parallel.ForEach(items, options, i => { });
    }
}";

        var t = Test(test);
        t.ExpectedDiagnostics.Add(Expected());
        await t.RunAsync();
    }

    [Fact]
    public async Task InlineOptions_ShouldReportDiagnostic()
    {
        var test =
            @"
using System.Threading;
using System.Threading.Tasks;

public class Runner
{
    public void Run(int[] items, CancellationToken cancellationToken)
    {
        Parallel.ForEach(items, {|#0:new ParallelOptions { MaxDegreeOfParallelism = 4 }|}, i => { });
    }
}";

        var t = Test(test);
        t.ExpectedDiagnostics.Add(Expected());
        await t.RunAsync();
    }

    [Fact]
    public async Task ImplicitCreation_ShouldReportDiagnostic()
    {
        var test =
            @"
using System.Threading;
using System.Threading.Tasks;

public class Runner
{
    public void Run(int[] items, CancellationToken cancellationToken)
    {
        ParallelOptions options = {|#0:new() { MaxDegreeOfParallelism = 4 }|};
        Parallel.ForEach(items, options, i => { });
    }
}";

        var t = Test(test);
        t.ExpectedDiagnostics.Add(Expected());
        await t.RunAsync();
    }

    [Fact]
    public async Task TokenSetInInitializer_ShouldNotReportDiagnostic()
    {
        var test =
            @"
using System.Threading;
using System.Threading.Tasks;

public class Runner
{
    public void Run(int[] items, CancellationToken cancellationToken)
    {
        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = 4,
            CancellationToken = cancellationToken,
        };
        Parallel.ForEach(items, options, i => { });
    }
}";

        await Test(test).RunAsync();
    }

    [Fact]
    public async Task TokenAssignedAfterwards_ShouldNotReportDiagnostic()
    {
        // Setting the property afterwards is equally correct, and common when the options are built
        // up conditionally.
        var test =
            @"
using System.Threading;
using System.Threading.Tasks;

public class Runner
{
    public void Run(int[] items, bool limit, CancellationToken cancellationToken)
    {
        var options = new ParallelOptions();
        if (limit)
            options.MaxDegreeOfParallelism = 4;
        options.CancellationToken = cancellationToken;
        Parallel.ForEach(items, options, i => { });
    }
}";

        await Test(test).RunAsync();
    }

    [Fact]
    public async Task NoTokenInScope_ShouldNotReportDiagnostic()
    {
        // With nothing to suggest, the rule stays quiet — the same gate CC002 and CC012 apply.
        var test =
            @"
using System.Threading.Tasks;

public class Runner
{
    public void Run(int[] items)
    {
        var options = new ParallelOptions { MaxDegreeOfParallelism = 4 };
        Parallel.ForEach(items, options, i => { });
    }
}";

        await Test(test).RunAsync();
    }

    [Fact]
    public async Task TokenFromEnclosingLambda_ShouldReportDiagnostic()
    {
        // The scope walk is shared with CC002, so a token on an enclosing lambda counts.
        var test =
            @"
using System;
using System.Threading;
using System.Threading.Tasks;

public class Runner
{
    public void Run(int[] items)
    {
        Action<CancellationToken> run = ct =>
        {
            var options = {|#0:new ParallelOptions { MaxDegreeOfParallelism = 4 }|};
            Parallel.ForEach(items, options, i => { });
        };
    }
}";

        var t = Test(test);
        t.ExpectedDiagnostics.Add(Expected("ct"));
        await t.RunAsync();
    }

    [Fact]
    public async Task LookalikeParallelOptions_ShouldNotReportDiagnostic()
    {
        // Same name, different namespace. CC034 is symbol-gated.
        var test =
            @"
using System.Threading;

namespace Custom
{
    public class ParallelOptions
    {
        public int MaxDegreeOfParallelism { get; set; }
    }

    public class Runner
    {
        public void Run(CancellationToken cancellationToken)
        {
            var options = new ParallelOptions { MaxDegreeOfParallelism = 4 };
        }
    }
}";

        await Test(test).RunAsync();
    }

    [Fact]
    public async Task TokenAssignedAfterTheLoop_ShouldReportDiagnostic()
    {
        // Too late: the loop it was passed to already ran uncancellable.
        var test =
            @"
using System.Threading;
using System.Threading.Tasks;

public class Runner
{
    public void Run(int[] items, CancellationToken cancellationToken)
    {
        var options = {|#0:new ParallelOptions()|};
        Parallel.ForEach(items, options, i => { });
        options.CancellationToken = cancellationToken;
    }
}";

        var t = Test(test);
        t.ExpectedDiagnostics.Add(Expected());
        await t.RunAsync();
    }

    [Fact]
    public async Task TokenAssignedInsideALambda_ShouldReportDiagnostic()
    {
        // An assignment inside a nested function may never run at all.
        var test =
            @"
using System;
using System.Threading;
using System.Threading.Tasks;

public class Runner
{
    public void Run(int[] items, CancellationToken cancellationToken)
    {
        var options = {|#0:new ParallelOptions()|};
        Action configure = () => options.CancellationToken = cancellationToken;
        Parallel.ForEach(items, options, i => { });
    }
}";

        var t = Test(test);
        t.ExpectedDiagnostics.Add(Expected());
        await t.RunAsync();
    }

    [Fact]
    public async Task DefaultTokenInInitializer_ShouldReportDiagnostic()
    {
        // Satisfying the property with a token that cancels nothing leaves the loop exactly as
        // uncancellable. CC012 only covers these spellings as invocation arguments.
        var test =
            @"
using System.Threading;
using System.Threading.Tasks;

public class Runner
{
    public void Run(int[] items, CancellationToken cancellationToken)
    {
        var options = {|#0:new ParallelOptions { CancellationToken = default }|};
        Parallel.ForEach(items, options, i => { });
    }
}";

        var t = Test(test);
        t.ExpectedDiagnostics.Add(Expected());
        await t.RunAsync();
    }

    [Fact]
    public async Task CancellationTokenNoneInInitializer_ShouldReportDiagnostic()
    {
        var test =
            @"
using System.Threading;
using System.Threading.Tasks;

public class Runner
{
    public void Run(int[] items, CancellationToken cancellationToken)
    {
        var options = {|#0:new ParallelOptions { CancellationToken = CancellationToken.None }|};
        Parallel.ForEach(items, options, i => { });
    }
}";

        var t = Test(test);
        t.ExpectedDiagnostics.Add(Expected());
        await t.RunAsync();
    }

    [Fact]
    public async Task ConstructedEmptyTokenInInitializer_ShouldReportDiagnostic()
    {
        // `new CancellationToken()` is the constructed spelling of a token that cancels nothing.
        var test =
            @"
using System.Threading;
using System.Threading.Tasks;

public class Runner
{
    public void Run(int[] items, CancellationToken cancellationToken)
    {
        var options = {|#0:new ParallelOptions { CancellationToken = new CancellationToken() }|};
        Parallel.ForEach(items, options, i => { });
    }
}";

        var t = Test(test);
        t.ExpectedDiagnostics.Add(Expected());
        await t.RunAsync();
    }

    [Fact]
    public async Task DeferredUseInsideALambda_ShouldNotReportDiagnostic()
    {
        // The lambda runs whenever it is invoked, so the argument inside it does not bound when the
        // token must be assigned — the assignment here always happens before the loop executes.
        var test =
            @"
using System;
using System.Threading;
using System.Threading.Tasks;

public class Runner
{
    public void Run(int[] items, CancellationToken cancellationToken)
    {
        var options = new ParallelOptions();
        Action run = () => Parallel.ForEach(items, options, i => { });
        options.CancellationToken = cancellationToken;
        run();
    }
}";

        await Test(test).RunAsync();
    }

    [Fact]
    public async Task ConditionalTokenAssignment_ShouldReportDiagnostic()
    {
        // On the path where the condition is false the loop is still uncancellable, which is the
        // finding rather than an exemption.
        var test =
            @"
using System.Threading;
using System.Threading.Tasks;

public class Runner
{
    public void Run(int[] items, bool configure, CancellationToken cancellationToken)
    {
        var options = {|#0:new ParallelOptions()|};
        if (configure)
            options.CancellationToken = cancellationToken;
        Parallel.ForEach(items, options, i => { });
    }
}";

        var t = Test(test);
        t.ExpectedDiagnostics.Add(Expected());
        await t.RunAsync();
    }

    [Fact]
    public async Task CreatedByAssignmentToAnExistingLocal_ShouldNotReportDiagnostic()
    {
        // The options are created by an assignment rather than a declaration, so resolving the
        // target only from declarators missed that the token is set before the loop.
        var test =
            @"
using System.Threading;
using System.Threading.Tasks;

public class Runner
{
    public void Run(int[] items, CancellationToken cancellationToken)
    {
        ParallelOptions options;
        options = new ParallelOptions();
        options.CancellationToken = cancellationToken;
        Parallel.ForEach(items, options, i => { });
    }
}";

        await Test(test).RunAsync();
    }
}
