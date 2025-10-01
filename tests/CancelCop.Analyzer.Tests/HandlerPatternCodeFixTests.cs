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
}
