using System.Collections.Immutable;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace CancelCop.Analyzer.Tests;

public class MinimalApiCodeFixTests
{
    [Fact]
    public async Task MapGet_WithoutToken_AddsTokenParameter()
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

        var fixedCode = @"
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;

public class Program
{
    public static void Main()
    {
        var app = WebApplication.Create();
        app.MapGet(""/users"", async (CancellationToken cancellationToken) => await GetUsersAsync());
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

        var test2 = new CSharpCodeFixTest<MinimalApiAnalyzer, MinimalApiCodeFixProvider, DefaultVerifier>
        {
            TestCode = test,
            FixedCode = fixedCode,
            ReferenceAssemblies = Microsoft.CodeAnalysis.Testing.ReferenceAssemblies.Net.Net90
                .AddPackages(ImmutableArray.Create(new PackageIdentity("Microsoft.AspNetCore.App.Ref", "9.0.0"))),
        };

        test2.ExpectedDiagnostics.Add(expected);
        await test2.RunAsync();
    }

    [Fact]
    public async Task MapPost_WithExistingParameter_AddsTokenAsLastParameter()
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

        var fixedCode = @"
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;

public class Program
{
    public static void Main()
    {
        var app = WebApplication.Create();
        app.MapPost(""/users"", async (string name, CancellationToken cancellationToken) => await CreateUserAsync(name));
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

        var test2 = new CSharpCodeFixTest<MinimalApiAnalyzer, MinimalApiCodeFixProvider, DefaultVerifier>
        {
            TestCode = test,
            FixedCode = fixedCode,
            ReferenceAssemblies = Microsoft.CodeAnalysis.Testing.ReferenceAssemblies.Net.Net90
                .AddPackages(ImmutableArray.Create(new PackageIdentity("Microsoft.AspNetCore.App.Ref", "9.0.0"))),
        };

        test2.ExpectedDiagnostics.Add(expected);
        await test2.RunAsync();
    }

    [Fact]
    public async Task MapPut_WithMultipleParameters_AddsTokenAsLastParameter()
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
        app.MapPut(""/users/{id}"", {|#0:async (int id, string name) => await UpdateUserAsync(id, name)|});
    }

    private static async Task<string> UpdateUserAsync(int id, string name)
    {
        await Task.Delay(100);
        return name;
    }
}";

        var fixedCode = @"
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;

public class Program
{
    public static void Main()
    {
        var app = WebApplication.Create();
        app.MapPut(""/users/{id}"", async (int id, string name, CancellationToken cancellationToken) => await UpdateUserAsync(id, name));
    }

    private static async Task<string> UpdateUserAsync(int id, string name)
    {
        await Task.Delay(100);
        return name;
    }
}";

        var expected = new DiagnosticResult("CC005C", Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("MapPut");

        var test2 = new CSharpCodeFixTest<MinimalApiAnalyzer, MinimalApiCodeFixProvider, DefaultVerifier>
        {
            TestCode = test,
            FixedCode = fixedCode,
            ReferenceAssemblies = Microsoft.CodeAnalysis.Testing.ReferenceAssemblies.Net.Net90
                .AddPackages(ImmutableArray.Create(new PackageIdentity("Microsoft.AspNetCore.App.Ref", "9.0.0"))),
        };

        test2.ExpectedDiagnostics.Add(expected);
        await test2.RunAsync();
    }
}
