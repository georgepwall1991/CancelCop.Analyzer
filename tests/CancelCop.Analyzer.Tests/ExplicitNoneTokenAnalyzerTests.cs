using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    CancelCop.Analyzer.ExplicitNoneTokenAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace CancelCop.Analyzer.Tests;

public class ExplicitNoneTokenAnalyzerTests
{
    private const string Harness = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    private Task DoAsync(CancellationToken token) => Task.CompletedTask;
    private void Consume(int value) { }
";

    [Fact]
    public async Task None_WhenTokenInScope_ShouldReportDiagnostic()
    {
        var test = Harness + @"
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await DoAsync({|#0:CancellationToken.None|});
    }
}";

        var expected = VerifyCS.Diagnostic("CC012")
            .WithLocation(0)
            .WithArguments("CancellationToken.None", "cancellationToken");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task DefaultLiteral_WhenTokenInScope_ShouldReportDiagnostic()
    {
        var test = Harness + @"
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await DoAsync({|#0:default|});
    }
}";

        var expected = VerifyCS.Diagnostic("CC012")
            .WithLocation(0)
            .WithArguments("default", "cancellationToken");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task DefaultTyped_WhenTokenInScope_ShouldReportDiagnostic()
    {
        var test = Harness + @"
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await DoAsync({|#0:default(CancellationToken)|});
    }
}";

        var expected = VerifyCS.Diagnostic("CC012")
            .WithLocation(0)
            .WithArguments("default", "cancellationToken");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task None_InTargetTypedNew_WhenTokenInScope_ShouldReportDiagnostic()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;

public class Worker
{
    public Worker(CancellationToken token) { }
}

public class TestClass
{
    public void Run(CancellationToken cancellationToken)
    {
        Worker w = new({|#0:CancellationToken.None|});
    }
}";

        var expected = VerifyCS.Diagnostic("CC012")
            .WithLocation(0)
            .WithArguments("CancellationToken.None", "cancellationToken");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task NamedDefaultArgument_WhenTokenInScope_ShouldReportDiagnostic()
    {
        var test = Harness + @"
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await DoAsync(token: {|#0:default|});
    }
}";

        var expected = VerifyCS.Diagnostic("CC012")
            .WithLocation(0)
            .WithArguments("default", "cancellationToken");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task None_WhenNoTokenInScope_ShouldNotReportDiagnostic()
    {
        var test = Harness + @"
    public async Task RunAsync()
    {
        await DoAsync(CancellationToken.None);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task RealToken_ShouldNotReportDiagnostic()
    {
        var test = Harness + @"
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await DoAsync(cancellationToken);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task DefaultToNonTokenParameter_ShouldNotReportDiagnostic()
    {
        var test = Harness + @"
    public void RunAsync(CancellationToken cancellationToken)
    {
        Consume(default);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
