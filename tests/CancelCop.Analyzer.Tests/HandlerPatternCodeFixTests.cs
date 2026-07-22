using System.Collections.Immutable;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace CancelCop.Analyzer.Tests;

public class HandlerPatternCodeFixTests
{
    [Fact]
    public async Task MediatRHandler_WithoutToken_AddsTokenParameter()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;
using MediatR;

public class GetUserQuery : IRequest<string> { }

public class GetUserQueryHandler : IRequestHandler<GetUserQuery, string>
{
    public async Task<string> {|#0:Handle|}(GetUserQuery request)
    {
        await Task.Delay(100);
        return ""User"";
    }
}";

        var fixedCode = @"
using System.Threading;
using System.Threading.Tasks;
using MediatR;

public class GetUserQuery : IRequest<string> { }

public class GetUserQueryHandler : IRequestHandler<GetUserQuery, string>
{
    public async Task<string> Handle(GetUserQuery request, CancellationToken cancellationToken)
    {
        await Task.Delay(100);
        return ""User"";
    }
}";

        var expected = new DiagnosticResult("CC005A", Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("Handle");

        var test2 = new CSharpCodeFixTest<MediatRHandlerAnalyzer, HandlerPatternCodeFixProvider, DefaultVerifier>
        {
            TestCode = test,
            FixedCode = fixedCode,
            ReferenceAssemblies = Microsoft.CodeAnalysis.Testing.ReferenceAssemblies.Net.Net90
                .AddPackages(ImmutableArray.Create(new PackageIdentity("MediatR", "13.0.0"))),
            CompilerDiagnostics = CompilerDiagnostics.None,
        };

        test2.ExpectedDiagnostics.Add(expected);
        await test2.RunAsync();
    }

    [Fact]
    public async Task FixAll_TwoControllerActions_BothGetTokenParameter()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

public class UsersController : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> {|#0:GetUsers|}()
    {
        await Task.Delay(100);
        return Ok();
    }

    [HttpGet]
    public async Task<IActionResult> {|#1:GetAdmins|}()
    {
        await Task.Delay(100);
        return Ok();
    }
}";

        var fixedCode = @"
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

public class UsersController : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetUsers(CancellationToken cancellationToken)
    {
        await Task.Delay(100);
        return Ok();
    }

    [HttpGet]
    public async Task<IActionResult> GetAdmins(CancellationToken cancellationToken)
    {
        await Task.Delay(100);
        return Ok();
    }
}";

        var test2 = new CSharpCodeFixTest<ControllerAnalyzer, HandlerPatternCodeFixProvider, DefaultVerifier>
        {
            TestCode = test,
            FixedCode = fixedCode,
            ReferenceAssemblies = Microsoft.CodeAnalysis.Testing.ReferenceAssemblies.Net.Net90
                .AddPackages(ImmutableArray.Create(new PackageIdentity("Microsoft.AspNetCore.Mvc.Core", "2.2.5"))),
        };

        test2.ExpectedDiagnostics.Add(new DiagnosticResult("CC005B", Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
            .WithLocation(0).WithArguments("GetUsers"));
        test2.ExpectedDiagnostics.Add(new DiagnosticResult("CC005B", Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
            .WithLocation(1).WithArguments("GetAdmins"));
        await test2.RunAsync();
    }

    [Fact]
    public async Task ControllerAction_WithoutToken_AddsTokenParameter()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

public class UsersController : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> {|#0:GetUsers|}()
    {
        await Task.Delay(100);
        return Ok();
    }
}";

        var fixedCode = @"
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

public class UsersController : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetUsers(CancellationToken cancellationToken)
    {
        await Task.Delay(100);
        return Ok();
    }
}";

        var expected = new DiagnosticResult("CC005B", Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("GetUsers");

        var test2 = new CSharpCodeFixTest<ControllerAnalyzer, HandlerPatternCodeFixProvider, DefaultVerifier>
        {
            TestCode = test,
            FixedCode = fixedCode,
            ReferenceAssemblies = Microsoft.CodeAnalysis.Testing.ReferenceAssemblies.Net.Net90
                .AddPackages(ImmutableArray.Create(new PackageIdentity("Microsoft.AspNetCore.Mvc.Core", "2.2.5"))),
        };

        test2.ExpectedDiagnostics.Add(expected);
        await test2.RunAsync();
    }

    [Fact]
    public async Task ControllerAction_AcceptVerbsWithoutToken_AddsTokenParameter()
    {
        var test = @"
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

public class UsersController : ControllerBase
{
    [AcceptVerbs(""GET"")]
    public async Task<IActionResult> {|#0:GetUsers|}()
    {
        await Task.Delay(100);
        return Ok();
    }
}";

        var fixedCode = @"
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

public class UsersController : ControllerBase
{
    [AcceptVerbs(""GET"")]
    public async Task<IActionResult> GetUsers(CancellationToken cancellationToken)
    {
        await Task.Delay(100);
        return Ok();
    }
}";

        var expected = new DiagnosticResult("CC005B", Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("GetUsers");

        var test2 = new CSharpCodeFixTest<ControllerAnalyzer, HandlerPatternCodeFixProvider, DefaultVerifier>
        {
            TestCode = test,
            FixedCode = fixedCode,
            ReferenceAssemblies = Microsoft.CodeAnalysis.Testing.ReferenceAssemblies.Net.Net90
                .AddPackages(ImmutableArray.Create(new PackageIdentity("Microsoft.AspNetCore.Mvc.Core", "2.2.5"))),
        };

        test2.ExpectedDiagnostics.Add(expected);
        await test2.RunAsync();
    }

    [Fact]
    public async Task ControllerAction_WithExistingParameters_AddsTokenAsLastParameter()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

public class UsersController : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> {|#0:CreateUser|}([FromBody] string user)
    {
        await Task.Delay(100);
        return Ok();
    }
}";

        var fixedCode = @"
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

public class UsersController : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> CreateUser([FromBody] string user, CancellationToken cancellationToken)
    {
        await Task.Delay(100);
        return Ok();
    }
}";

        var expected = new DiagnosticResult("CC005B", Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("CreateUser");

        var test2 = new CSharpCodeFixTest<ControllerAnalyzer, HandlerPatternCodeFixProvider, DefaultVerifier>
        {
            TestCode = test,
            FixedCode = fixedCode,
            ReferenceAssemblies = Microsoft.CodeAnalysis.Testing.ReferenceAssemblies.Net.Net90
                .AddPackages(ImmutableArray.Create(new PackageIdentity("Microsoft.AspNetCore.Mvc.Core", "2.2.5"))),
        };

        test2.ExpectedDiagnostics.Add(expected);
        await test2.RunAsync();
    }

    [Fact]
    public async Task ControllerAction_WithoutSystemThreadingUsing_AddsUsingAndCompiles()
    {
        // No 'using System.Threading;' present — the fix must add it, otherwise CS0246.
        var test = @"
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

public class UsersController : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> {|#0:GetUsers|}()
    {
        await Task.Delay(100);
        return Ok();
    }
}";

        var fixedCode = @"
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

public class UsersController : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetUsers(CancellationToken cancellationToken)
    {
        await Task.Delay(100);
        return Ok();
    }
}";

        var expected = new DiagnosticResult("CC005B", Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("GetUsers");

        var test2 = new CSharpCodeFixTest<ControllerAnalyzer, HandlerPatternCodeFixProvider, DefaultVerifier>
        {
            TestCode = test,
            FixedCode = fixedCode,
            ReferenceAssemblies = Microsoft.CodeAnalysis.Testing.ReferenceAssemblies.Net.Net90
                .AddPackages(ImmutableArray.Create(new PackageIdentity("Microsoft.AspNetCore.Mvc.Core", "2.2.5"))),
        };

        test2.ExpectedDiagnostics.Add(expected);
        await test2.RunAsync();
    }

    [Fact]
    public async Task ControllerAction_WithOptionalParameter_AddsTokenWithDefaultAndCompiles()
    {
        // A required token appended after an optional parameter would be CS1737;
        // the fix must give the token '= default' so it stays last and compiles.
        var test = @"
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

public class UsersController : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> {|#0:GetUsers|}([FromQuery] int page = 1)
    {
        await Task.Delay(100);
        return Ok();
    }
}";

        var fixedCode = @"
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

public class UsersController : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetUsers([FromQuery] int page = 1, CancellationToken cancellationToken = default)
    {
        await Task.Delay(100);
        return Ok();
    }
}";

        var expected = new DiagnosticResult("CC005B", Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("GetUsers");

        var test2 = new CSharpCodeFixTest<ControllerAnalyzer, HandlerPatternCodeFixProvider, DefaultVerifier>
        {
            TestCode = test,
            FixedCode = fixedCode,
            ReferenceAssemblies = Microsoft.CodeAnalysis.Testing.ReferenceAssemblies.Net.Net90
                .AddPackages(ImmutableArray.Create(new PackageIdentity("Microsoft.AspNetCore.Mvc.Core", "2.2.5"))),
        };

        test2.ExpectedDiagnostics.Add(expected);
        await test2.RunAsync();
    }

    [Fact]
    public async Task ControllerAction_WithGlobalUsing_InsertsUsingAfterGlobalBlock()
    {
        // A 'global using' must precede all non-global usings (CS8915). The inserted
        // 'using System.Threading;' must therefore go AFTER the global-using block even
        // though it sorts alphabetically before the trailing global using.
        var test = @"
global using System.Xml;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

public class UsersController : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> {|#0:GetUsers|}()
    {
        await Task.Delay(100);
        return Ok();
    }
}";

        var fixedCode = @"
global using System.Xml;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

public class UsersController : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetUsers(CancellationToken cancellationToken)
    {
        await Task.Delay(100);
        return Ok();
    }
}";

        var expected = new DiagnosticResult("CC005B", Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("GetUsers");

        var test2 = new CSharpCodeFixTest<ControllerAnalyzer, HandlerPatternCodeFixProvider, DefaultVerifier>
        {
            TestCode = test,
            FixedCode = fixedCode,
            ReferenceAssemblies = Microsoft.CodeAnalysis.Testing.ReferenceAssemblies.Net.Net90
                .AddPackages(ImmutableArray.Create(new PackageIdentity("Microsoft.AspNetCore.Mvc.Core", "2.2.5"))),
        };

        test2.ExpectedDiagnostics.Add(expected);
        await test2.RunAsync();
    }

    [Fact]
    public async Task ControllerAction_WithAliasUsingOnly_AddsRealUsingAndCompiles()
    {
        // An alias using of System.Threading does NOT import the namespace, so the
        // unqualified CancellationToken would be CS0246 unless a real using is added.
        var test = @"
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using Threading = System.Threading;

public class UsersController : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> {|#0:GetUsers|}()
    {
        await Task.Delay(100);
        return Ok();
    }
}";

        var fixedCode = @"
using Microsoft.AspNetCore.Mvc;
using System.Threading;
using System.Threading.Tasks;
using Threading = System.Threading;

public class UsersController : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetUsers(CancellationToken cancellationToken)
    {
        await Task.Delay(100);
        return Ok();
    }
}";

        var expected = new DiagnosticResult("CC005B", Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("GetUsers");

        var test2 = new CSharpCodeFixTest<ControllerAnalyzer, HandlerPatternCodeFixProvider, DefaultVerifier>
        {
            TestCode = test,
            FixedCode = fixedCode,
            ReferenceAssemblies = Microsoft.CodeAnalysis.Testing.ReferenceAssemblies.Net.Net90
                .AddPackages(ImmutableArray.Create(new PackageIdentity("Microsoft.AspNetCore.Mvc.Core", "2.2.5"))),
        };

        test2.ExpectedDiagnostics.Add(expected);
        await test2.RunAsync();
    }

    [Fact]
    public async Task ControllerAction_WithCollidingParameterName_UsesNonCollidingName()
    {
        // A parameter already named 'cancellationToken' means the fix must pick another
        // name, otherwise CS0100 (duplicate parameter name).
        var test = @"
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

public class UsersController : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> {|#0:GetUsers|}(string cancellationToken)
    {
        await Task.Delay(100);
        return Ok();
    }
}";

        var fixedCode = @"
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

public class UsersController : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetUsers(string cancellationToken, CancellationToken ct)
    {
        await Task.Delay(100);
        return Ok();
    }
}";

        var expected = new DiagnosticResult("CC005B", Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("GetUsers");

        var test2 = new CSharpCodeFixTest<ControllerAnalyzer, HandlerPatternCodeFixProvider, DefaultVerifier>
        {
            TestCode = test,
            FixedCode = fixedCode,
            ReferenceAssemblies = Microsoft.CodeAnalysis.Testing.ReferenceAssemblies.Net.Net90
                .AddPackages(ImmutableArray.Create(new PackageIdentity("Microsoft.AspNetCore.Mvc.Core", "2.2.5"))),
        };

        test2.ExpectedDiagnostics.Add(expected);
        await test2.RunAsync();
    }
}
