using System.Collections.Immutable;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.CodeAnalysis.Testing.Verifiers;
using Xunit;

namespace CancelCop.Analyzer.Tests;

public class ControllerAnalyzerTests
{
    private static CSharpAnalyzerTest<ControllerAnalyzer, XUnitVerifier> CreateTest(string testCode, params DiagnosticResult[] expected)
    {
        var test = new CSharpAnalyzerTest<ControllerAnalyzer, XUnitVerifier>
        {
            TestCode = testCode,
            ReferenceAssemblies = Microsoft.CodeAnalysis.Testing.ReferenceAssemblies.Net.Net90
                .AddPackages(ImmutableArray.Create(
                    new PackageIdentity("Microsoft.AspNetCore.Mvc.Core", "2.2.5"))),
        };

        test.ExpectedDiagnostics.AddRange(expected);
        return test;
    }

    [Fact]
    public async Task ControllerAction_HttpGet_WithoutCancellationToken_ShouldReportDiagnostic()
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

        var expected = new DiagnosticResult("CC005B", Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("GetUsers");

        await CreateTest(test, expected).RunAsync();
    }

    [Fact]
    public async Task ControllerAction_HttpGet_WithCancellationToken_ShouldNotReportDiagnostic()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

public class UsersController : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetUsers(CancellationToken cancellationToken)
    {
        await Task.Delay(100, cancellationToken);
        return Ok();
    }
}";

        await CreateTest(test).RunAsync();
    }

    [Fact]
    public async Task ControllerAction_HttpPost_WithoutCancellationToken_ShouldReportDiagnostic()
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

        var expected = new DiagnosticResult("CC005B", Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("CreateUser");

        await CreateTest(test, expected).RunAsync();
    }

    [Fact]
    public async Task ControllerAction_HttpPut_WithCancellationToken_ShouldNotReportDiagnostic()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

public class UsersController : ControllerBase
{
    [HttpPut]
    public async Task<IActionResult> UpdateUser([FromBody] string user, CancellationToken cancellationToken)
    {
        await Task.Delay(100, cancellationToken);
        return Ok();
    }
}";

        await CreateTest(test).RunAsync();
    }

    [Fact]
    public async Task ControllerAction_HttpDelete_WithoutCancellationToken_ShouldReportDiagnostic()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

public class UsersController : ControllerBase
{
    [HttpDelete]
    public async Task<IActionResult> {|#0:DeleteUser|}(int id)
    {
        await Task.Delay(100);
        return Ok();
    }
}";

        var expected = new DiagnosticResult("CC005B", Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("DeleteUser");

        await CreateTest(test, expected).RunAsync();
    }

    [Fact]
    public async Task ControllerAction_SyncMethod_ShouldNotReportDiagnostic()
    {
        var test = @"
using Microsoft.AspNetCore.Mvc;

public class UsersController : ControllerBase
{
    [HttpGet]
    public IActionResult GetUsers()
    {
        return Ok();
    }
}";

        await CreateTest(test).RunAsync();
    }

    [Fact]
    public async Task NonControllerMethod_ShouldNotReportDiagnostic()
    {
        var test = @"
using System.Threading.Tasks;

public class RegularClass
{
    public async Task GetUsers()
    {
        await Task.Delay(100);
    }
}";

        await CreateTest(test).RunAsync();
    }

    [Fact]
    public async Task ControllerAction_NoHttpAttribute_ShouldNotReportDiagnostic()
    {
        var test = @"
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

public class UsersController : ControllerBase
{
    public async Task<IActionResult> HelperMethod()
    {
        await Task.Delay(100);
        return Ok();
    }
}";

        await CreateTest(test).RunAsync();
    }
}
