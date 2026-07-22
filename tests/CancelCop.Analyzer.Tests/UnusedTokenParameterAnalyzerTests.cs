using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    CancelCop.Analyzer.UnusedTokenParameterAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace CancelCop.Analyzer.Tests;

public class UnusedTokenParameterAnalyzerTests
{
    [Fact]
    public async Task AsyncMethod_UnusedToken_ShouldReportDiagnostic()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(CancellationToken {|#0:cancellationToken|})
    {
        await Task.Delay(1000);
    }
}";

        var expected = VerifyCS.Diagnostic("CC016").WithLocation(0).WithArguments("cancellationToken");
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task AsyncMethod_TokenOnlyInNameof_ShouldReportDiagnostic()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(CancellationToken {|#0:cancellationToken|})
    {
        _ = nameof(cancellationToken);
        await Task.Delay(1000);
    }
}";

        var expected = VerifyCS.Diagnostic("CC016").WithLocation(0).WithArguments("cancellationToken");
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task AsyncMethod_TokenOnlyOverwritten_ShouldReportDiagnostic()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(CancellationToken {|#0:cancellationToken|})
    {
        cancellationToken = default;
        await Task.Delay(1);
    }
}";

        var expected = VerifyCS.Diagnostic("CC016").WithLocation(0).WithArguments("cancellationToken");
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task AsyncMethod_TokenOnlyPassedAsOut_ShouldReportDiagnostic()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(CancellationToken {|#0:cancellationToken|})
    {
        Reset(out cancellationToken);
        await Task.Delay(1);
    }

    private static void Reset(out CancellationToken token) => token = default;
}";

        var expected = VerifyCS.Diagnostic("CC016").WithLocation(0).WithArguments("cancellationToken");
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task AsyncMethod_TokensPassedAsRefAndIn_ShouldNotReportDiagnostic()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(CancellationToken cancellationToken, CancellationToken otherToken)
    {
        Observe(ref cancellationToken, in otherToken);
        await Task.Delay(1);
    }

    private static void Observe(ref CancellationToken first, in CancellationToken second)
    {
        _ = first.CanBeCanceled;
        _ = second.CanBeCanceled;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsyncMethod_TokenReadOnAssignmentRightHandSide_ShouldNotReportDiagnostic()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        cancellationToken = Preserve(cancellationToken);
        await Task.Delay(1);
    }

    private static CancellationToken Preserve(CancellationToken token) => token;
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsyncIterator_EnumeratorCancellationToken_NotReferencedInBody_ShouldNotReportDiagnostic()
    {
        // A [EnumeratorCancellation] token is delivered to the async-iterator enumerator (it receives a
        // consumer's WithCancellation token), so it is observed even though the body never references
        // it. CC016 must not flag it as dead.
        var test = @"
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async IAsyncEnumerable<int> GetAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.Yield();
        yield return 1;
        yield return 2;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AsyncMethod_UsedToken_ShouldNotReportDiagnostic()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(1000, cancellationToken);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task SyncMethod_UnusedToken_ShouldNotReportDiagnostic()
    {
        var test = @"
using System.Threading;

public class TestClass
{
    public void Run(CancellationToken cancellationToken)
    {
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task SyncMethod_AwaitOnlyInsideNestedLambda_ShouldNotReportDiagnostic()
    {
        var test = @"
using System;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public void Configure(CancellationToken cancellationToken)
    {
        Func<Task> work = async () => await Task.Delay(1000);
        _ = work;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task InterfaceImplementation_UnusedToken_ShouldNotReportDiagnostic()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;

public interface IRunner
{
    Task RunAsync(CancellationToken cancellationToken);
}

public class TestClass : IRunner
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(1000);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TokenUsedInConstructorArgument_ShouldNotReportDiagnostic()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;

public class Holder
{
    public Holder(CancellationToken token) { }
}

public class TestClass
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var holder = new Holder(cancellationToken);
        await Task.Delay(1000);
        _ = holder;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TokenUsedInsideLambda_ShouldNotReportDiagnostic()
    {
        var test = @"
using System;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        Action a = () => cancellationToken.ThrowIfCancellationRequested();
        a();
        await Task.Delay(1000);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task LocalFunction_UnusedToken_ShouldReportDiagnostic()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public void Outer()
    {
        async Task RunAsync(CancellationToken {|#0:cancellationToken|})
        {
            await Task.Delay(1000);
        }

        _ = RunAsync(default);
    }
}";

        var expected = VerifyCS.Diagnostic("CC016").WithLocation(0).WithArguments("cancellationToken");
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }
}
