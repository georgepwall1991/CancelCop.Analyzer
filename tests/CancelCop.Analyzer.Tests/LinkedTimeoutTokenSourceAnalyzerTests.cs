using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    CancelCop.Analyzer.LinkedTimeoutTokenSourceAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace CancelCop.Analyzer.Tests;

public class LinkedTimeoutTokenSourceAnalyzerTests
{
    private const string Harness = @"
using System;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    private Task DoAsync(CancellationToken token) => Task.CompletedTask;
";

    [Fact]
    public async Task TimeSpanCtor_WhenTokenInScope_ShouldReportDiagnostic()
    {
        var test = Harness + @"
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var cts = {|#0:new CancellationTokenSource(TimeSpan.FromSeconds(30))|};
        await DoAsync(cts.Token);
    }
}";

        var expected = VerifyCS.Diagnostic("CC029")
            .WithLocation(0)
            .WithArguments("cancellationToken");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task IntMillisecondsCtor_WhenTokenInScope_ShouldReportDiagnostic()
    {
        var test = Harness + @"
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var cts = {|#0:new CancellationTokenSource(5000)|};
        await DoAsync(cts.Token);
    }
}";

        var expected = VerifyCS.Diagnostic("CC029")
            .WithLocation(0)
            .WithArguments("cancellationToken");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task CancelAfter_OnParameterlessLocal_WhenTokenInScope_ShouldReportDiagnostic()
    {
        var test = Harness + @"
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var cts = new CancellationTokenSource();
        cts.{|#0:CancelAfter|}(TimeSpan.FromSeconds(30));
        await DoAsync(cts.Token);
    }
}";

        var expected = VerifyCS.Diagnostic("CC029")
            .WithLocation(0)
            .WithArguments("cancellationToken");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task NoTokenInScope_TimeSpanCtor_ShouldNotReport()
    {
        var test = Harness + @"
    public async Task RunAsync()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await DoAsync(cts.Token);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task CreateLinkedTokenSource_WithCancelAfter_ShouldNotReport()
    {
        var test = Harness + @"
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(TimeSpan.FromSeconds(5));
        await DoAsync(linked.Token);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ParameterlessWithoutCancelAfter_ShouldNotReport()
    {
        // Not a timeout source; CC014 owns disposal of the plain CTS.
        var test = Harness + @"
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var cts = new CancellationTokenSource();
        await DoAsync(cts.Token);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task CancelAfter_OnLinkedSource_ShouldNotReport()
    {
        var test = Harness + @"
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(TimeSpan.FromSeconds(5));
        await DoAsync(linked.Token);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TimeoutCtor_InsideLocalFunction_WhenTokenInScope_ShouldReport()
    {
        var test = Harness + @"
    public void Configure(CancellationToken cancellationToken)
    {
        async Task RunAsync()
        {
            using var cts = {|#0:new CancellationTokenSource(TimeSpan.FromSeconds(1))|};
            await DoAsync(cts.Token);
        }

        _ = RunAsync();
    }
}";

        var expected = VerifyCS.Diagnostic("CC029")
            .WithLocation(0)
            .WithArguments("cancellationToken");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task LookalikeType_ShouldNotReport()
    {
        var test = @"
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Other
{
    public class CancellationTokenSource
    {
        public CancellationTokenSource(TimeSpan delay) { }
        public CancellationToken Token => default;
    }
}

public class TestClass
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var cts = new Other.CancellationTokenSource(TimeSpan.FromSeconds(1));
        await Task.Delay(1, cancellationToken);
        _ = cts.Token;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TargetTypedNew_TimeSpan_WhenTokenInScope_ShouldReport()
    {
        var test = Harness + @"
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using CancellationTokenSource cts = {|#0:new(TimeSpan.FromSeconds(1))|};
        await DoAsync(cts.Token);
    }
}";

        var expected = VerifyCS.Diagnostic("CC029")
            .WithLocation(0)
            .WithArguments("cancellationToken");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task TimeoutCtor_ThenCancelAfter_ReportsOnlyOnCreation()
    {
        // Creation already establishes a timeout; do not double-report on CancelAfter.
        var test = Harness + @"
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var cts = {|#0:new CancellationTokenSource(TimeSpan.FromSeconds(30))|};
        cts.CancelAfter(TimeSpan.FromSeconds(10));
        await DoAsync(cts.Token);
    }
}";

        var expected = VerifyCS.Diagnostic("CC029")
            .WithLocation(0)
            .WithArguments("cancellationToken");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task NonUsing_TimeSpanCtor_WhenTokenInScope_ShouldReport()
    {
        var test = Harness + @"
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var cts = {|#0:new CancellationTokenSource(TimeSpan.FromSeconds(30))|};
        await DoAsync(cts.Token);
        cts.Dispose();
    }
}";

        var expected = VerifyCS.Diagnostic("CC029")
            .WithLocation(0)
            .WithArguments("cancellationToken");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }
}
