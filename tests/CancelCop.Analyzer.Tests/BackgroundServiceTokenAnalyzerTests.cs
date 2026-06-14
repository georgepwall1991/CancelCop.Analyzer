using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace CancelCop.Analyzer.Tests;

public class BackgroundServiceTokenAnalyzerTests
{
    private static CSharpAnalyzerTest<BackgroundServiceTokenAnalyzer, DefaultVerifier> CreateTest(
        string testCode, params DiagnosticResult[] expected)
    {
        var test = new CSharpAnalyzerTest<BackgroundServiceTokenAnalyzer, DefaultVerifier>
        {
            TestCode = testCode,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90.AddPackages(
                ImmutableArray.Create(new PackageIdentity("Microsoft.Extensions.Hosting.Abstractions", "9.0.0"))),
        };
        test.ExpectedDiagnostics.AddRange(expected);
        return test;
    }

    [Fact]
    public async Task ExecuteAsync_IgnoresStoppingToken_ShouldReportDiagnostic()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

public class Worker : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken {|#0:stoppingToken|})
    {
        await Task.Delay(1000);
    }
}";

        var expected = new DiagnosticResult("CC017", DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("stoppingToken");

        await CreateTest(test, expected).RunAsync();
    }

    [Fact]
    public async Task ExpressionBodiedExecuteAsync_IgnoresStoppingToken_ShouldReportDiagnostic()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

public class Worker : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken {|#0:stoppingToken|}) => RunForeverAsync();

    private Task RunForeverAsync() => Task.CompletedTask;
}";

        var expected = new DiagnosticResult("CC017", DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("stoppingToken");

        await CreateTest(test, expected).RunAsync();
    }

    [Fact]
    public async Task ExecuteAsync_ObservesStoppingToken_ShouldNotReportDiagnostic()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

public class Worker : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(1000, stoppingToken);
        }
    }
}";

        await CreateTest(test).RunAsync();
    }

    [Fact]
    public async Task NonBackgroundService_ExecuteAsync_ShouldNotReportDiagnostic()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;

public class NotAService
{
    public async Task ExecuteAsync(CancellationToken token)
    {
        await Task.Delay(1000);
    }
}";

        await CreateTest(test).RunAsync();
    }

    [Fact]
    public async Task ExecuteAsync_PassesTokenToHelper_ShouldNotReportDiagnostic()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

public class Worker : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RunLoopAsync(stoppingToken);
    }

    private Task RunLoopAsync(CancellationToken token) => Task.CompletedTask;
}";

        await CreateTest(test).RunAsync();
    }
}
