using System.Collections.Immutable;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace CancelCop.Analyzer.Tests;

public class MinimalApiAnalyzerTests
{
    private static CSharpAnalyzerTest<MinimalApiAnalyzer, DefaultVerifier> CreateTest(string testCode, params DiagnosticResult[] expected)
    {
        var test = new CSharpAnalyzerTest<MinimalApiAnalyzer, DefaultVerifier>
        {
            TestCode = testCode,
            ReferenceAssemblies = Microsoft.CodeAnalysis.Testing.ReferenceAssemblies.Net.Net90
                .AddPackages(ImmutableArray.Create(
                    new PackageIdentity("Microsoft.AspNetCore.App.Ref", "9.0.0"))),
        };

        test.ExpectedDiagnostics.AddRange(expected);
        return test;
    }

    [Fact]
    public async Task MapGet_WithoutCancellationToken_ShouldReportDiagnostic()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;

public class Program
{
    public static void Main()
    {
        var app = WebApplication.Create();
        app.MapGet(""/users"", {|#0:async () => await GetUsersAsync()|});
    }

    private static async Task<string> GetUsersAsync()
    {
        await Task.Delay(100);
        return ""users"";
    }
}";

        var expected = new DiagnosticResult("CC005C", Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("MapGet");

        await CreateTest(test, expected).RunAsync();
    }

    [Fact]
    public async Task MapGet_WithCancellationToken_ShouldNotReportDiagnostic()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;

public class Program
{
    public static void Main()
    {
        var app = WebApplication.Create();
        app.MapGet(""/users"", async (CancellationToken ct) => await GetUsersAsync(ct));
    }

    private static async Task<string> GetUsersAsync(CancellationToken ct)
    {
        await Task.Delay(100, ct);
        return ""users"";
    }
}";

        await CreateTest(test).RunAsync();
    }

    [Fact]
    public async Task MapPost_WithoutCancellationToken_ShouldReportDiagnostic()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;

public class Program
{
    public static void Main()
    {
        var app = WebApplication.Create();
        app.MapPost(""/users"", {|#0:async (string name) => await CreateUserAsync(name)|});
    }

    private static async Task<string> CreateUserAsync(string name)
    {
        await Task.Delay(100);
        return name;
    }
}";

        var expected = new DiagnosticResult("CC005C", Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("MapPost");

        await CreateTest(test, expected).RunAsync();
    }

    [Fact]
    public async Task MapPut_WithCancellationToken_ShouldNotReportDiagnostic()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;

public class Program
{
    public static void Main()
    {
        var app = WebApplication.Create();
        app.MapPut(""/users/{id}"", async (int id, string name, CancellationToken ct) =>
            await UpdateUserAsync(id, name, ct));
    }

    private static async Task<string> UpdateUserAsync(int id, string name, CancellationToken ct)
    {
        await Task.Delay(100, ct);
        return name;
    }
}";

        await CreateTest(test).RunAsync();
    }

    [Fact]
    public async Task MapDelete_WithoutCancellationToken_ShouldReportDiagnostic()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;

public class Program
{
    public static void Main()
    {
        var app = WebApplication.Create();
        app.MapDelete(""/users/{id}"", {|#0:async (int id) => await DeleteUserAsync(id)|});
    }

    private static async Task DeleteUserAsync(int id)
    {
        await Task.Delay(100);
    }
}";

        var expected = new DiagnosticResult("CC005C", Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("MapDelete");

        await CreateTest(test, expected).RunAsync();
    }

    [Fact]
    public async Task MapPatch_WithoutCancellationToken_ShouldReportDiagnostic()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;

public class Program
{
    public static void Main()
    {
        var app = WebApplication.Create();
        app.MapPatch(""/users/{id}"", {|#0:async (int id) => await PatchUserAsync(id)|});
    }

    private static async Task PatchUserAsync(int id)
    {
        await Task.Delay(100);
    }
}";

        var expected = new DiagnosticResult("CC005C", Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("MapPatch");

        await CreateTest(test, expected).RunAsync();
    }

    [Fact]
    public async Task MapGet_SyncLambda_ShouldNotReportDiagnostic()
    {
        var test = @"
using Microsoft.AspNetCore.Builder;

public class Program
{
    public static void Main()
    {
        var app = WebApplication.Create();
        app.MapGet(""/users"", () => ""users"");
    }
}";

        await CreateTest(test).RunAsync();
    }

    [Fact]
    public async Task MapGet_WithMultipleParameters_WithoutCancellationToken_ShouldReportDiagnostic()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;

public class Program
{
    public static void Main()
    {
        var app = WebApplication.Create();
        app.MapGet(""/users/{id}"", {|#0:async (int id, string filter) =>
            await GetUserAsync(id, filter)|});
    }

    private static async Task<string> GetUserAsync(int id, string filter)
    {
        await Task.Delay(100);
        return ""user"";
    }
}";

        var expected = new DiagnosticResult("CC005C", Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("MapGet");

        await CreateTest(test, expected).RunAsync();
    }
}
