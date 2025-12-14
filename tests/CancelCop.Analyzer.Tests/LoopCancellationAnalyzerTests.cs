using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    CancelCop.Analyzer.LoopCancellationAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace CancelCop.Analyzer.Tests;

public class LoopCancellationAnalyzerTests
{
    [Fact]
    public async Task ForEachLoop_WithoutCancellationCheck_ShouldReportDiagnostic()
    {
        var test = @"
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task ProcessAsync(List<int> items, CancellationToken cancellationToken)
    {
        {|#0:foreach|} (var item in items)
        {
            await Task.Delay(100, cancellationToken);
        }
    }
}";

        var expected = VerifyCS.Diagnostic("CC009")
            .WithLocation(0)
            .WithArguments("cancellationToken");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task ForLoop_WithoutCancellationCheck_ShouldReportDiagnostic()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task ProcessAsync(int count, CancellationToken ct)
    {
        {|#0:for|} (int i = 0; i < count; i++)
        {
            await Task.Delay(100, ct);
        }
    }
}";

        var expected = VerifyCS.Diagnostic("CC009")
            .WithLocation(0)
            .WithArguments("ct");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task WhileLoop_WithoutCancellationCheck_ShouldReportDiagnostic()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task ProcessAsync(CancellationToken token)
    {
        int i = 0;
        {|#0:while|} (i < 10)
        {
            await Task.Delay(100, token);
            i++;
        }
    }
}";

        var expected = VerifyCS.Diagnostic("CC009")
            .WithLocation(0)
            .WithArguments("token");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task DoWhileLoop_WithoutCancellationCheck_ShouldReportDiagnostic()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task ProcessAsync(CancellationToken cancellationToken)
    {
        int i = 0;
        {|#0:do|}
        {
            await Task.Delay(100, cancellationToken);
            i++;
        } while (i < 10);
    }
}";

        var expected = VerifyCS.Diagnostic("CC009")
            .WithLocation(0)
            .WithArguments("cancellationToken");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task ForEachLoop_WithThrowIfCancellationRequested_ShouldNotReportDiagnostic()
    {
        var test = @"
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task ProcessAsync(List<int> items, CancellationToken cancellationToken)
    {
        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(100, cancellationToken);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ForLoop_WithIsCancellationRequestedCheck_ShouldNotReportDiagnostic()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task ProcessAsync(int count, CancellationToken ct)
    {
        for (int i = 0; i < count; i++)
        {
            if (ct.IsCancellationRequested)
                break;
            await Task.Delay(100, ct);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task Loop_InMethodWithoutCancellationToken_ShouldNotReportDiagnostic()
    {
        var test = @"
using System.Collections.Generic;
using System.Threading.Tasks;

public class TestClass
{
    public async Task ProcessAsync(List<int> items)
    {
        foreach (var item in items)
        {
            await Task.Delay(100);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task NestedLoops_BothWithoutCancellationCheck_ShouldReportTwoDiagnostics()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task ProcessAsync(int rows, int cols, CancellationToken ct)
    {
        {|#0:for|} (int i = 0; i < rows; i++)
        {
            {|#1:for|} (int j = 0; j < cols; j++)
            {
                await Task.Delay(100, ct);
            }
        }
    }
}";

        var expected1 = VerifyCS.Diagnostic("CC009")
            .WithLocation(0)
            .WithArguments("ct");

        var expected2 = VerifyCS.Diagnostic("CC009")
            .WithLocation(1)
            .WithArguments("ct");

        await VerifyCS.VerifyAnalyzerAsync(test, expected1, expected2);
    }

    [Fact]
    public async Task NestedLoops_OuterHasCheck_InnerWithoutCheck_ShouldReportOneDiagnostic()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task ProcessAsync(int rows, int cols, CancellationToken ct)
    {
        for (int i = 0; i < rows; i++)
        {
            ct.ThrowIfCancellationRequested();
            {|#0:for|} (int j = 0; j < cols; j++)
            {
                await Task.Delay(100, ct);
            }
        }
    }
}";

        var expected = VerifyCS.Diagnostic("CC009")
            .WithLocation(0)
            .WithArguments("ct");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task Loop_InLocalFunction_WithToken_ShouldReportDiagnostic()
    {
        var test = @"
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task ProcessAsync()
    {
        async Task LocalAsync(List<int> items, CancellationToken ct)
        {
            {|#0:foreach|} (var item in items)
            {
                await Task.Delay(100, ct);
            }
        }

        await LocalAsync(new List<int>(), CancellationToken.None);
    }
}";

        var expected = VerifyCS.Diagnostic("CC009")
            .WithLocation(0)
            .WithArguments("ct");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task Loop_InLocalFunction_OuterMethodHasToken_ShouldReportDiagnostic()
    {
        var test = @"
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task ProcessAsync(CancellationToken cancellationToken)
    {
        void LocalSync(List<int> items)
        {
            {|#0:foreach|} (var item in items)
            {
                // Process item
            }
        }

        LocalSync(new List<int>());
        await Task.CompletedTask;
    }
}";

        var expected = VerifyCS.Diagnostic("CC009")
            .WithLocation(0)
            .WithArguments("cancellationToken");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task EmptyLoop_WithoutCancellationCheck_ShouldReportDiagnostic()
    {
        var test = @"
using System.Threading;

public class TestClass
{
    public void Process(int count, CancellationToken ct)
    {
        {|#0:for|} (int i = 0; i < count; i++)
        {
        }
    }
}";

        var expected = VerifyCS.Diagnostic("CC009")
            .WithLocation(0)
            .WithArguments("ct");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task SyncMethod_WithToken_LoopWithoutCheck_ShouldReportDiagnostic()
    {
        var test = @"
using System.Collections.Generic;
using System.Threading;

public class TestClass
{
    public void Process(List<int> items, CancellationToken cancellationToken)
    {
        {|#0:foreach|} (var item in items)
        {
            // Process item synchronously
        }
    }
}";

        var expected = VerifyCS.Diagnostic("CC009")
            .WithLocation(0)
            .WithArguments("cancellationToken");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }
}
