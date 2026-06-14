using System.Collections.Immutable;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace CancelCop.Analyzer.Tests;

public class MinimalApiCodeFixTests
{
    [Fact]
    public async Task FixAll_TwoEndpoints_BothGetTokenParameter()
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
        app.MapGet(""/admins"", {|#1:async () => await GetUsersAsync()|});
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
        app.MapGet(""/admins"", async (CancellationToken cancellationToken) => await GetUsersAsync());
    }

    private static async Task<string> GetUsersAsync()
    {
        await Task.Delay(100);
        return ""users"";
    }
}";

        var test2 = new CSharpCodeFixTest<MinimalApiAnalyzer, MinimalApiCodeFixProvider, DefaultVerifier>
        {
            TestCode = test,
            FixedCode = fixedCode,
            ReferenceAssemblies = Microsoft.CodeAnalysis.Testing.ReferenceAssemblies.Net.Net90
                .AddPackages(ImmutableArray.Create(new PackageIdentity("Microsoft.AspNetCore.App.Ref", "9.0.0"))),
        };

        test2.ExpectedDiagnostics.Add(new DiagnosticResult("CC005C", Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
            .WithLocation(0).WithArguments("MapGet"));
        test2.ExpectedDiagnostics.Add(new DiagnosticResult("CC005C", Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
            .WithLocation(1).WithArguments("MapGet"));
        await test2.RunAsync();
    }

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

    [Fact]
    public async Task MapGet_WithoutSystemThreadingUsing_AddsUsingAndCompiles()
    {
        // No 'using System.Threading;' — the fix must add it or the result is CS0246.
        var test = @"
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
    public async Task MapPost_WithCollidingParameterName_UsesNonCollidingName()
    {
        // A parameter already named 'cancellationToken' means the fix must pick another
        // name, otherwise CS0100 (duplicate parameter name).
        var test = @"
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;

public class Program
{
    public static void Main()
    {
        var app = WebApplication.Create();
        app.MapPost(""/users"", {|#0:async (string cancellationToken) => await CreateUserAsync(cancellationToken)|});
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
        app.MapPost(""/users"", async (string cancellationToken, CancellationToken ct) => await CreateUserAsync(cancellationToken));
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
    public async Task MapPost_WithBodyLocalNamedCt_AvoidsLocalNameCollision()
    {
        // The lambda already has a parameter 'cancellationToken' AND a body local 'ct',
        // so the fix must pick a name that collides with neither, otherwise CS0136.
        var test = @"
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;

public class Program
{
    public static void Main()
    {
        var app = WebApplication.Create();
        app.MapPost(""/users"", {|#0:async (string cancellationToken) =>
        {
            int ct = cancellationToken.Length;
            await CreateUserAsync(ct);
        }|});
    }

    private static async Task<string> CreateUserAsync(int id)
    {
        await Task.Delay(100);
        return ""user"";
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
        app.MapPost(""/users"", async (string cancellationToken, CancellationToken cancellationToken2) =>
        {
            int ct = cancellationToken.Length;
            await CreateUserAsync(ct);
        });
    }

    private static async Task<string> CreateUserAsync(int id)
    {
        await Task.Delay(100);
        return ""user"";
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
    public async Task MapGet_SimpleLambda_OffersNoFixInsteadOfMixingTypedAndUntyped()
    {
        // A simple (untyped) lambda cannot be fixed without either mixing typed/untyped
        // parameters (CS0748) or producing an unrecognized untyped token. The provider must
        // therefore offer NO fix (leaving the code unchanged) rather than emit broken code.
        var source = @"
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;

public class Program
{
    public static void Main()
    {
        var app = WebApplication.Create();
        app.MapGet(""/users"", {|#0:async id => await GetUserAsync(id)|});
    }

    private static async Task<string> GetUserAsync(int id)
    {
        await Task.Delay(100);
        return ""user"";
    }
}";

        var expected = new DiagnosticResult("CC005C", Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("MapGet");

        var test2 = new CSharpCodeFixTest<MinimalApiAnalyzer, MinimalApiCodeFixProvider, DefaultVerifier>
        {
            TestCode = source,
            // No fix is offered, so the code (and the diagnostic) is unchanged.
            FixedCode = source,
            ReferenceAssemblies = Microsoft.CodeAnalysis.Testing.ReferenceAssemblies.Net.Net90
                .AddPackages(ImmutableArray.Create(new PackageIdentity("Microsoft.AspNetCore.App.Ref", "9.0.0"))),
            // A bare untyped lambda is not a bindable minimal-API handler, so the surrounding
            // sample does not fully bind.
            CompilerDiagnostics = CompilerDiagnostics.None,
        };

        test2.ExpectedDiagnostics.Add(expected);
        await test2.RunAsync();
    }

    [Fact]
    public async Task MapGet_MethodGroup_AddsTokenToReferencedMethod()
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
        app.MapGet(""/users"", {|#0:GetUsersAsync|});
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
        app.MapGet(""/users"", GetUsersAsync);
    }

    private static async Task<string> GetUsersAsync(CancellationToken cancellationToken = default)
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
    public async Task MapGet_LocalFunctionMethodGroup_AddsTokenToLocalFunction()
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
        app.MapGet(""/users"", {|#0:GetUsersAsync|});

        static async Task<string> GetUsersAsync()
        {
            await Task.Delay(100);
            return ""users"";
        }
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
        app.MapGet(""/users"", GetUsersAsync);

        static async Task<string> GetUsersAsync(CancellationToken cancellationToken = default)
        {
            await Task.Delay(100);
            return ""users"";
        }
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
    public async Task MapGet_MethodGroupInsideRegistrationLambda_FixesMethodNotEnclosingLambda()
    {
        // The diagnostic sits on the method-group identifier inside a registration lambda; the
        // fix must rewrite the referenced method's declaration, not add a parameter to the
        // enclosing lambda.
        var test = @"
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

public static class Endpoints
{
    public static void Register(IEndpointRouteBuilder app)
    {
        Action register = () => app.MapGet(""/users"", {|#0:GetUsersAsync|});
        register();
    }

    private static async Task<string> GetUsersAsync()
    {
        await Task.Delay(100);
        return ""users"";
    }
}";

        var fixedCode = @"
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

public static class Endpoints
{
    public static void Register(IEndpointRouteBuilder app)
    {
        Action register = () => app.MapGet(""/users"", GetUsersAsync);
        register();
    }

    private static async Task<string> GetUsersAsync(CancellationToken cancellationToken = default)
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
    public async Task MapGet_VirtualMethodGroup_ReportsButOffersNoFix()
    {
        // Rewriting a virtual method's signature would orphan its overrides (CS0115), so the
        // diagnostic stands but no automatic fix is offered.
        var source = @"
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

public class UserHandlers
{
    public virtual async Task<string> Get()
    {
        await Task.Delay(100);
        return ""users"";
    }
}

public static class Endpoints
{
    public static void Register(IEndpointRouteBuilder app, UserHandlers handlers)
    {
        app.MapGet(""/users"", {|#0:handlers.Get|});
    }
}";

        var expected = new DiagnosticResult("CC005C", Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("MapGet");

        var test2 = new CSharpCodeFixTest<MinimalApiAnalyzer, MinimalApiCodeFixProvider, DefaultVerifier>
        {
            TestCode = source,
            FixedCode = source,
            ReferenceAssemblies = Microsoft.CodeAnalysis.Testing.ReferenceAssemblies.Net.Net90
                .AddPackages(ImmutableArray.Create(new PackageIdentity("Microsoft.AspNetCore.App.Ref", "9.0.0"))),
        };

        test2.ExpectedDiagnostics.Add(expected);
        await test2.RunAsync();
    }

    [Fact]
    public async Task MapGet_PartialMethodGroup_ReportsButOffersNoFix()
    {
        // A partial method's two declaration parts must keep matching signatures; rewriting one
        // part would not compile (CS8795/CS0759), so no automatic fix is offered.
        var source = @"
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;

public partial class Program
{
    public static partial Task<string> GetUsersAsync();

    public static partial async Task<string> GetUsersAsync()
    {
        await Task.Delay(100);
        return ""users"";
    }

    public static void Main()
    {
        var app = WebApplication.Create();
        app.MapGet(""/users"", {|#0:GetUsersAsync|});
    }
}";

        var expected = new DiagnosticResult("CC005C", Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("MapGet");

        var test2 = new CSharpCodeFixTest<MinimalApiAnalyzer, MinimalApiCodeFixProvider, DefaultVerifier>
        {
            TestCode = source,
            FixedCode = source,
            ReferenceAssemblies = Microsoft.CodeAnalysis.Testing.ReferenceAssemblies.Net.Net90
                .AddPackages(ImmutableArray.Create(new PackageIdentity("Microsoft.AspNetCore.App.Ref", "9.0.0"))),
        };

        test2.ExpectedDiagnostics.Add(expected);
        await test2.RunAsync();
    }

    [Fact]
    public async Task MapGet_TwoRoutesSameHandler_FixAllAddsParameterOnce()
    {
        // Two endpoints referencing the same handler produce two diagnostics whose fixes edit
        // the same declaration; the batch fixer must merge them into a single token parameter.
        var test = @"
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;

public class Program
{
    public static void Main()
    {
        var app = WebApplication.Create();
        app.MapGet(""/users"", {|#0:GetUsersAsync|});
        app.MapPost(""/users"", {|#1:GetUsersAsync|});
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
        app.MapGet(""/users"", GetUsersAsync);
        app.MapPost(""/users"", GetUsersAsync);
    }

    private static async Task<string> GetUsersAsync(CancellationToken cancellationToken = default)
    {
        await Task.Delay(100);
        return ""users"";
    }
}";

        var test2 = new CSharpCodeFixTest<MinimalApiAnalyzer, MinimalApiCodeFixProvider, DefaultVerifier>
        {
            TestCode = test,
            FixedCode = fixedCode,
            ReferenceAssemblies = Microsoft.CodeAnalysis.Testing.ReferenceAssemblies.Net.Net90
                .AddPackages(ImmutableArray.Create(new PackageIdentity("Microsoft.AspNetCore.App.Ref", "9.0.0"))),
        };

        test2.ExpectedDiagnostics.Add(new DiagnosticResult("CC005C", Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("MapGet"));
        test2.ExpectedDiagnostics.Add(new DiagnosticResult("CC005C", Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
            .WithLocation(1)
            .WithArguments("MapPost"));
        await test2.RunAsync();
    }
}
