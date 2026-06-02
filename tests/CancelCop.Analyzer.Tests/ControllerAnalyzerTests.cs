using System.Collections.Immutable;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace CancelCop.Analyzer.Tests;

public class ControllerAnalyzerTests
{
    private static CSharpAnalyzerTest<ControllerAnalyzer, DefaultVerifier> CreateTest(string testCode, params DiagnosticResult[] expected)
    {
        var test = new CSharpAnalyzerTest<ControllerAnalyzer, DefaultVerifier>
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

    [Fact]
    public async Task CustomControllerBase_ShouldNotReportDiagnostic()
    {
        // Custom ControllerBase class in user's namespace should not trigger the analyzer
        var test = @"
using System.Threading.Tasks;

namespace MyApp
{
    // Custom ControllerBase - not from Microsoft.AspNetCore.Mvc
    public abstract class ControllerBase
    {
        protected object Ok() => new object();
    }

    // Attribute that looks like HttpGet but isn't from ASP.NET Core
    [System.AttributeUsage(System.AttributeTargets.Method)]
    public class HttpGetAttribute : System.Attribute { }
}

namespace MyApp.Controllers
{
    public class UsersController : MyApp.ControllerBase
    {
        [MyApp.HttpGet]
        public async Task<object> GetUsers()
        {
            await Task.Delay(100);
            return Ok();
        }
    }
}";

        await CreateTest(test).RunAsync();
    }

    [Fact]
    public async Task ControllerAction_ReturnsValueTask_WithoutCancellationToken_ShouldReportDiagnostic()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

public class UsersController : ControllerBase
{
    [HttpGet]
    public async ValueTask<IActionResult> {|#0:GetUsers|}()
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
    public async Task ControllerAction_ReturnsValueTask_WithCancellationToken_ShouldNotReportDiagnostic()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

public class UsersController : ControllerBase
{
    [HttpGet]
    public async ValueTask<IActionResult> GetUsers(CancellationToken cancellationToken)
    {
        await Task.Delay(100, cancellationToken);
        return Ok();
    }
}";

        await CreateTest(test).RunAsync();
    }

    [Fact]
    public async Task ControllerAction_UserDefinedHttpAttribute_ShouldNotReportDiagnostic()
    {
        // A user-defined HttpGetAttribute (not Microsoft.AspNetCore.Mvc's) is not an MVC routing
        // attribute, so MVC does not treat the method as an action — CC005B must not fire.
        var test = @"
using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

public sealed class HttpGetAttribute : Attribute { }

public class UsersController : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetUsers()
    {
        await Task.Delay(100);
        return Ok();
    }
}";

        await CreateTest(test).RunAsync();
    }

    [Fact]
    public async Task ControllerAction_SubclassedMvcHttpAttribute_ShouldReportDiagnostic()
    {
        // An attribute that derives from the real MVC HttpGetAttribute IS an MVC routing
        // attribute, so CC005B must still fire.
        var test = @"
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

public sealed class HttpGetCachedAttribute : HttpGetAttribute { }

public class UsersController : ControllerBase
{
    [HttpGetCached]
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
    public async Task ControllerAction_NonAction_ShouldNotReportDiagnostic()
    {
        // [NonAction] methods are not routed, so they need no CancellationToken.
        var test = @"
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

public class UsersController : ControllerBase
{
    [NonAction]
    [HttpGet]
    public async Task<IActionResult> GetUsers()
    {
        await Task.Delay(100);
        return Ok();
    }
}";

        await CreateTest(test).RunAsync();
    }

    [Fact]
    public async Task ControllerAction_InheritsNonActionFromBaseOverride_ShouldNotReportDiagnostic()
    {
        // NonActionAttribute is inheritable; an override that inherits [NonAction] from a base
        // virtual action is still not routed, so CC005B must not fire on the override.
        var test = @"
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

public class BaseController : ControllerBase
{
    [NonAction]
    [HttpGet]
    public virtual async Task<IActionResult> GetUsers()
    {
        await Task.Delay(100);
        return Ok();
    }
}

public class UsersController : BaseController
{
    [HttpGet]
    public override async Task<IActionResult> GetUsers()
    {
        await Task.Delay(100);
        return Ok();
    }
}";

        await CreateTest(test).RunAsync();
    }

    [Fact]
    public async Task ControllerAction_UserDefinedNonActionAttribute_StillReportsDiagnostic()
    {
        // A user-defined NonActionAttribute (not Microsoft.AspNetCore.Mvc's) does not stop MVC
        // routing, so CC005B must still fire.
        var test = @"
using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

public sealed class NonActionAttribute : Attribute { }

public class UsersController : ControllerBase
{
    [NonAction]
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
    public async Task ControllerAction_PrivateMethod_ShouldNotReportDiagnostic()
    {
        // Private methods are never routed as actions.
        var test = @"
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

public class UsersController : ControllerBase
{
    [HttpGet]
    private async Task<IActionResult> GetUsers()
    {
        await Task.Delay(100);
        return Ok();
    }
}";

        await CreateTest(test).RunAsync();
    }

    [Fact]
    public async Task ControllerAction_StaticMethod_ShouldNotReportDiagnostic()
    {
        // Static methods are never routed as actions.
        var test = @"
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

public class UsersController : ControllerBase
{
    [HttpGet]
    public static async Task<IActionResult> GetUsers()
    {
        await Task.Delay(100);
        return new OkResult();
    }
}";

        await CreateTest(test).RunAsync();
    }
}
