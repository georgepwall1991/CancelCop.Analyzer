using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace CancelCop.Analyzer.Tests;

/// <summary>
/// CC035: an <i>empty</i> <c>catch (OperationCanceledException)</c>. CC019 covers broad catches
/// (<c>catch</c> / <c>catch (Exception)</c>) that swallow cancellation among everything else; a
/// clause naming the cancellation type explicitly is outside its scope, and an empty body is not
/// handling the cancellation — it is discarding it.
/// </summary>
public class SilentlySwallowedCancellationTests
{
    private static CSharpAnalyzerTest<SilentlySwallowedCancellationAnalyzer, DefaultVerifier> Test(
        string source
    ) => new() { TestCode = source, ReferenceAssemblies = ReferenceAssemblies.Net.Net90 };

    private static DiagnosticResult Expected(string type = "OperationCanceledException") =>
        new DiagnosticResult("CC035", DiagnosticSeverity.Info).WithLocation(0).WithArguments(type);

    [Fact]
    public async Task EmptyCatch_ShouldReportDiagnostic()
    {
        var test =
            @"
using System;
using System.Threading;
using System.Threading.Tasks;

public class Worker
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(1000, cancellationToken);
        }
        {|#0:catch|} (OperationCanceledException)
        {
        }
    }
}";

        var t = Test(test);
        t.ExpectedDiagnostics.Add(Expected());
        await t.RunAsync();
    }

    [Fact]
    public async Task EmptyCatchOfTaskCanceledException_ShouldReportDiagnostic()
    {
        // TaskCanceledException derives from OperationCanceledException and discards the same signal.
        var test =
            @"
using System;
using System.Threading;
using System.Threading.Tasks;

public class Worker
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(1000, cancellationToken);
        }
        {|#0:catch|} (TaskCanceledException)
        {
        }
    }
}";

        var t = Test(test);
        t.ExpectedDiagnostics.Add(Expected("TaskCanceledException"));
        await t.RunAsync();
    }

    [Fact]
    public async Task EmptyCatchWithNamedVariable_ShouldReportDiagnostic()
    {
        var test =
            @"
using System;
using System.Threading;
using System.Threading.Tasks;

public class Worker
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(1000, cancellationToken);
        }
        {|#0:catch|} (OperationCanceledException ex)
        {
        }
    }
}";

        var t = Test(test);
        t.ExpectedDiagnostics.Add(Expected());
        await t.RunAsync();
    }

    [Fact]
    public async Task CatchWithARethrow_ShouldNotReportDiagnostic()
    {
        var test =
            @"
using System;
using System.Threading;
using System.Threading.Tasks;

public class Worker
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(1000, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
    }
}";

        await Test(test).RunAsync();
    }

    [Fact]
    public async Task CatchThatLogs_ShouldNotReportDiagnostic()
    {
        // Stopping quietly is a real pattern at a boundary, and such handlers do something — log,
        // set state, break a loop. Any statement means the author considered the case.
        var test =
            @"
using System;
using System.Threading;
using System.Threading.Tasks;

public class Worker
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(1000, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine(""stopping"");
        }
    }
}";

        await Test(test).RunAsync();
    }

    [Fact]
    public async Task CatchWithAFilter_ShouldNotReportDiagnostic()
    {
        // A filter means the author reasoned about which cancellations to handle.
        var test =
            @"
using System;
using System.Threading;
using System.Threading.Tasks;

public class Worker
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(1000, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }
}";

        await Test(test).RunAsync();
    }

    [Fact]
    public async Task BroadCatch_ShouldNotReportDiagnostic()
    {
        // A broad catch is CC019's finding; reporting it here too would double up on one clause.
        var test =
            @"
using System;
using System.Threading;
using System.Threading.Tasks;

public class Worker
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(1000, cancellationToken);
        }
        catch (Exception)
        {
        }

        try
        {
            await Task.Delay(1000, cancellationToken);
        }
        catch
        {
        }
    }
}";

        await Test(test).RunAsync();
    }

    [Fact]
    public async Task EmptyCatchOfAnUnrelatedException_ShouldNotReportDiagnostic()
    {
        var test =
            @"
using System;
using System.Threading;
using System.Threading.Tasks;

public class Worker
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(1000, cancellationToken);
        }
        catch (InvalidOperationException)
        {
        }
    }
}";

        await Test(test).RunAsync();
    }

    [Fact]
    public async Task LookalikeCancellationException_ShouldNotReportDiagnostic()
    {
        // Same name, different namespace. CC035 is symbol-gated.
        var test =
            @"
using System;

namespace Custom
{
    public class OperationCanceledException : Exception { }

    public class Worker
    {
        public void Run()
        {
            try
            {
                Console.WriteLine(""work"");
            }
            catch (OperationCanceledException)
            {
            }
        }
    }
}";

        await Test(test).RunAsync();
    }

    [Fact]
    public async Task EmptyCatchWithAnExplanatoryComment_ShouldNotReportDiagnostic()
    {
        // Waiting until cancelled is idiomatic, and the note is what distinguishes a considered
        // discard from a silent one — which is what this rule is named for.
        var test =
            @"
using System;
using System.Threading;
using System.Threading.Tasks;

public class IdleHost
{
    public async Task WaitForShutdownAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
        catch (TaskCanceledException)
        {
            // expected on shutdown
        }
    }
}";

        await Test(test).RunAsync();
    }

    [Fact]
    public async Task SourceDefinedSystemOperationCanceledException_ShouldNotReportDiagnostic()
    {
        // Source can declare its own System.OperationCanceledException, and the catch binds to that
        // one — matching on namespace and name alone would report an unrelated type.
        var test =
            @"
namespace System
{
    public class MyBase : Exception { }
}

namespace Probe
{
    public class OperationCanceledException : System.MyBase { }

    public class Worker
    {
        public void Run()
        {
            try
            {
                System.Console.WriteLine(""work"");
            }
            catch (OperationCanceledException)
            {
            }
        }
    }
}";

        await Test(test).RunAsync();
    }
}
