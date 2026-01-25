using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    CancelCop.Analyzer.ConfigureAwaitAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace CancelCop.Analyzer.Tests;

public class ConfigureAwaitAnalyzerTests
{
    // Note: CC012 is disabled by default, so we need to enable it in tests

    [Fact]
    public async Task AwaitWithoutConfigureAwait_WhenEnabled_ShouldReportDiagnostic()
    {
        var test = @"
using System.Threading.Tasks;

public class TestClass
{
    public async Task ProcessAsync()
    {
        {|#0:await|} Task.Delay(1000);
    }
}";

        var expected = VerifyCS.Diagnostic("CC012")
            .WithLocation(0);

        await new CSharpAnalyzerTest<ConfigureAwaitAnalyzer, DefaultVerifier>
        {
            TestCode = test,
            ExpectedDiagnostics = { expected },
            SolutionTransforms =
            {
                (solution, projectId) =>
                {
                    var project = solution.GetProject(projectId);
                    var compilationOptions = project!.CompilationOptions!
                        .WithSpecificDiagnosticOptions(
                            new[] { new KeyValuePair<string, ReportDiagnostic>("CC012", ReportDiagnostic.Info) });
                    return solution.WithProjectCompilationOptions(projectId, compilationOptions);
                }
            }
        }.RunAsync();
    }

    [Fact]
    public async Task AwaitWithConfigureAwaitFalse_ShouldNotReportDiagnostic()
    {
        var test = @"
using System.Threading.Tasks;

public class TestClass
{
    public async Task ProcessAsync()
    {
        await Task.Delay(1000).ConfigureAwait(false);
    }
}";

        await new CSharpAnalyzerTest<ConfigureAwaitAnalyzer, DefaultVerifier>
        {
            TestCode = test,
            SolutionTransforms =
            {
                (solution, projectId) =>
                {
                    var project = solution.GetProject(projectId);
                    var compilationOptions = project!.CompilationOptions!
                        .WithSpecificDiagnosticOptions(
                            new[] { new KeyValuePair<string, ReportDiagnostic>("CC012", ReportDiagnostic.Info) });
                    return solution.WithProjectCompilationOptions(projectId, compilationOptions);
                }
            }
        }.RunAsync();
    }

    [Fact]
    public async Task AwaitWithConfigureAwaitTrue_ShouldNotReportDiagnostic()
    {
        var test = @"
using System.Threading.Tasks;

public class TestClass
{
    public async Task ProcessAsync()
    {
        await Task.Delay(1000).ConfigureAwait(true);
    }
}";

        await new CSharpAnalyzerTest<ConfigureAwaitAnalyzer, DefaultVerifier>
        {
            TestCode = test,
            SolutionTransforms =
            {
                (solution, projectId) =>
                {
                    var project = solution.GetProject(projectId);
                    var compilationOptions = project!.CompilationOptions!
                        .WithSpecificDiagnosticOptions(
                            new[] { new KeyValuePair<string, ReportDiagnostic>("CC012", ReportDiagnostic.Info) });
                    return solution.WithProjectCompilationOptions(projectId, compilationOptions);
                }
            }
        }.RunAsync();
    }

    [Fact]
    public async Task ByDefault_ShouldNotReportDiagnostic()
    {
        // Since CC012 is disabled by default, no diagnostics should be reported
        var test = @"
using System.Threading.Tasks;

public class TestClass
{
    public async Task ProcessAsync()
    {
        await Task.Delay(1000);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task MultipleAwaits_WhenEnabled_ShouldReportMultipleDiagnostics()
    {
        var test = @"
using System.Threading.Tasks;

public class TestClass
{
    public async Task ProcessAsync()
    {
        {|#0:await|} Task.Delay(500);
        {|#1:await|} Task.Delay(1000);
    }
}";

        var expected1 = VerifyCS.Diagnostic("CC012").WithLocation(0);
        var expected2 = VerifyCS.Diagnostic("CC012").WithLocation(1);

        await new CSharpAnalyzerTest<ConfigureAwaitAnalyzer, DefaultVerifier>
        {
            TestCode = test,
            ExpectedDiagnostics = { expected1, expected2 },
            SolutionTransforms =
            {
                (solution, projectId) =>
                {
                    var project = solution.GetProject(projectId);
                    var compilationOptions = project!.CompilationOptions!
                        .WithSpecificDiagnosticOptions(
                            new[] { new KeyValuePair<string, ReportDiagnostic>("CC012", ReportDiagnostic.Info) });
                    return solution.WithProjectCompilationOptions(projectId, compilationOptions);
                }
            }
        }.RunAsync();
    }
}
