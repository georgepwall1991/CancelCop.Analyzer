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
    public async Task StaticallyImportedNone_WhenTokenInScope_ShouldReportDiagnostic()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;
using static System.Threading.CancellationToken;

public class TestClass
{
    private Task DoAsync(CancellationToken token) => Task.CompletedTask;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await DoAsync({|#0:None|});
    }
}";

        var expected = VerifyCS.Diagnostic("CC012")
            .WithLocation(0)
            .WithArguments("CancellationToken.None", "cancellationToken");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task None_InAsyncLocalFunction_WhenTokenInScope_ShouldReportDiagnostic()
    {
        // The token is in scope of a nested async local function via the shared scope walk, so an
        // explicit None bound to a token parameter there is flagged too.
        var test = Harness + @"
    public void Configure(CancellationToken cancellationToken)
    {
        async Task RunAsync()
        {
            await DoAsync({|#0:CancellationToken.None|});
        }

        _ = RunAsync();
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
    public async Task ParenthesizedNoneAndDefaults_WhenTokenInScope_ShouldReportDiagnostics()
    {
        var test = Harness + @"
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await DoAsync({|#0:(CancellationToken.None)|});
        await DoAsync({|#1:((default(CancellationToken)))|});
        await DoAsync({|#2:(default)|});
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(
            test,
            VerifyCS.Diagnostic("CC012").WithLocation(0)
                .WithArguments("CancellationToken.None", "cancellationToken"),
            VerifyCS.Diagnostic("CC012").WithLocation(1)
                .WithArguments("default", "cancellationToken"),
            VerifyCS.Diagnostic("CC012").WithLocation(2)
                .WithArguments("default", "cancellationToken"));
    }

    [Fact]
    public async Task None_InExplicitNew_WhenTokenInScope_ShouldReportDiagnostic()
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
        var w = new Worker({|#0:CancellationToken.None|});
    }
}";

        var expected = VerifyCS.Diagnostic("CC012")
            .WithLocation(0)
            .WithArguments("CancellationToken.None", "cancellationToken");

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
    public async Task CustomTokenPropertyNamedNone_ShouldNotReportDiagnostic()
    {
        var test = Harness + @"
    private sealed class TokenProvider
    {
        public CancellationToken None => new(canceled: true);
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await DoAsync(new TokenProvider().None);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task StaticallyImportedCustomTokenPropertyNamedNone_ShouldNotReportDiagnostic()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;
using static TokenProvider;

public static class TokenProvider
{
    public static CancellationToken None => default;
}

public class TestClass
{
    private Task DoAsync(CancellationToken token) => Task.CompletedTask;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await DoAsync(None);
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
