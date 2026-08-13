using System.Collections.Immutable;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    CancelCop.Analyzer.TokenPropagationAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace CancelCop.Analyzer.Tests;

/// <summary>
/// Framework cancellation sources (<c>HttpContext.RequestAborted</c>,
/// <c>ServerCallContext.CancellationToken</c>) must participate in the shared in-scope token walk
/// so CC002/CC003/CC004 (and siblings) fire in middleware and gRPC methods that have no token
/// parameter.
/// </summary>
public class FrameworkTokenSourceTests
{
    internal const string HttpContextStub = @"
namespace Microsoft.AspNetCore.Http
{
    public abstract class HttpContext
    {
        public System.Threading.CancellationToken RequestAborted => default;
    }
}";

    internal const string GrpcContextStub = @"
namespace Grpc.Core
{
    public abstract class ServerCallContext
    {
        public System.Threading.CancellationToken CancellationToken => default;
    }
}";

    [Fact]
    public async Task MiddlewareInvokeAsync_DelayWithoutToken_ShouldReportCC002()
    {
        var test = @"
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

public class Middleware
{
    public async Task InvokeAsync(HttpContext context)
    {
        await Task.{|#0:Delay|}(100);
    }
}" + HttpContextStub;

        var expected = VerifyCS.Diagnostic("CC002")
            .WithLocation(0)
            .WithArguments("Delay", "context.RequestAborted");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task MiddlewareInvoke_DelayWithoutToken_ShouldReportCC002()
    {
        var test = @"
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

public class Middleware
{
    public async Task Invoke(HttpContext context)
    {
        await Task.{|#0:Delay|}(100);
    }
}" + HttpContextStub;

        var expected = VerifyCS.Diagnostic("CC002")
            .WithLocation(0)
            .WithArguments("Delay", "context.RequestAborted");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task Middleware_GetStringAsyncWithoutToken_ShouldReportCC004()
    {
        var test = @"
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

public class Middleware
{
    private readonly HttpClient _http = new HttpClient();

    public async Task InvokeAsync(HttpContext context)
    {
        await _http.{|#0:GetStringAsync|}(""https://api.example.com"");
    }
}" + HttpContextStub;

        var expected = new DiagnosticResult("CC004", Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("GetStringAsync", "context.RequestAborted");

        var t = new CSharpAnalyzerTest<HttpClientAnalyzer, DefaultVerifier>
        {
            TestCode = test,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90
                .AddPackages(ImmutableArray.Create(new PackageIdentity("Microsoft.Extensions.Http", "9.0.0"))),
        };
        t.ExpectedDiagnostics.Add(expected);
        await t.RunAsync();
    }

    [Fact]
    public async Task Middleware_ToListAsyncWithoutToken_ShouldReportCC003()
    {
        var test = @"
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

public class Middleware
{
    public async Task InvokeAsync(HttpContext context)
    {
        DbSet<User> users = null;
        await users.{|#0:ToListAsync|}();
    }
}

public class User { public int Id { get; set; } }" + HttpContextStub;

        var expected = new DiagnosticResult("CC003", Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("ToListAsync", "context.RequestAborted");

        var t = new CSharpAnalyzerTest<EFCoreAnalyzer, DefaultVerifier>
        {
            TestCode = test,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90
                .AddPackages(ImmutableArray.Create(new PackageIdentity("Microsoft.EntityFrameworkCore", "9.0.0"))),
        };
        t.ExpectedDiagnostics.Add(expected);
        await t.RunAsync();
    }

    [Fact]
    public async Task ControllerAction_WithTokenParameterAndHttpContext_PrefersParameter()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

public class TestClass
{
    public async Task RunAsync(HttpContext context, CancellationToken cancellationToken)
    {
        await Task.{|#0:Delay|}(100);
    }
}" + HttpContextStub;

        var expected = VerifyCS.Diagnostic("CC002")
            .WithLocation(0)
            .WithArguments("Delay", "cancellationToken");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task LookalikeHttpContext_ShouldNotReportDiagnostic()
    {
        var test = @"
using System.Threading.Tasks;

namespace Other.Http
{
    public class HttpContext
    {
        public System.Threading.CancellationToken RequestAborted => default;
    }
}

public class TestClass
{
    public async Task InvokeAsync(Other.Http.HttpContext context)
    {
        await Task.Delay(100);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task StaticLocalFunctionInsideMiddleware_ShouldNotCaptureRequestAborted()
    {
        var test = @"
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

public class Middleware
{
    public async Task InvokeAsync(HttpContext context)
    {
        static async Task Inner(HttpClient client)
        {
            await client.GetStringAsync(""https://api.example.com"");
        }

        await Inner(new HttpClient());
    }
}" + HttpContextStub;

        var t = new CSharpAnalyzerTest<HttpClientAnalyzer, DefaultVerifier>
        {
            TestCode = test,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90
                .AddPackages(ImmutableArray.Create(new PackageIdentity("Microsoft.Extensions.Http", "9.0.0"))),
        };
        await t.RunAsync();
    }

    [Fact]
    public async Task MiddlewarePassesContextOn_StillReportsMissingTokenOnHttpCall()
    {
        var test = @"
using System;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

public class Middleware
{
    private readonly HttpClient _http = new HttpClient();
    private readonly Func<HttpContext, Task> _next = _ => Task.CompletedTask;

    public async Task InvokeAsync(HttpContext context)
    {
        await _http.{|#0:GetStringAsync|}(""https://api.example.com"");
        await _next(context);
    }
}" + HttpContextStub;

        var expected = new DiagnosticResult("CC004", Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("GetStringAsync", "context.RequestAborted");

        var t = new CSharpAnalyzerTest<HttpClientAnalyzer, DefaultVerifier>
        {
            TestCode = test,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90
                .AddPackages(ImmutableArray.Create(new PackageIdentity("Microsoft.Extensions.Http", "9.0.0"))),
        };
        t.ExpectedDiagnostics.Add(expected);
        await t.RunAsync();
    }

    [Fact]
    public async Task GrpcMethod_DelayWithoutToken_ShouldReportCC002()
    {
        var test = @"
using System.Threading.Tasks;
using Grpc.Core;

public class GreeterService
{
    public async Task<string> SayHello(string request, ServerCallContext context)
    {
        await Task.{|#0:Delay|}(100);
        return ""hi"";
    }
}" + GrpcContextStub;

        var expected = VerifyCS.Diagnostic("CC002")
            .WithLocation(0)
            .WithArguments("Delay", "context.CancellationToken");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task LookalikeServerCallContext_ShouldNotReportDiagnostic()
    {
        var test = @"
using System.Threading.Tasks;

namespace Other.Grpc
{
    public class ServerCallContext
    {
        public System.Threading.CancellationToken CancellationToken => default;
    }
}

public class GreeterService
{
    public async Task<string> SayHello(string request, Other.Grpc.ServerCallContext context)
    {
        await Task.Delay(100);
        return ""hi"";
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task HttpContextWithoutRequestAbortedProperty_ShouldStayQuiet()
    {
        var test = @"
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace Microsoft.AspNetCore.Http
{
    public abstract class HttpContext
    {
    }
}

public class Middleware
{
    public async Task InvokeAsync(HttpContext context)
    {
        await Task.Delay(100);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task CapturingLocalFunction_UsesEnclosingRequestAborted()
    {
        var test = @"
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

public class Middleware
{
    public async Task InvokeAsync(HttpContext context)
    {
        async Task Inner()
        {
            await Task.{|#0:Delay|}(100);
        }

        await Inner();
    }
}" + HttpContextStub;

        var expected = VerifyCS.Diagnostic("CC002")
            .WithLocation(0)
            .WithArguments("Delay", "context.RequestAborted");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task InnerHttpContext_BeatsOuterTokenParameter()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

public class TestClass
{
    public async Task OuterAsync(CancellationToken cancellationToken)
    {
        async Task Inner(HttpContext context)
        {
            await Task.{|#0:Delay|}(100);
        }

        await Inner(null);
    }
}" + HttpContextStub;

        var expected = VerifyCS.Diagnostic("CC002")
            .WithLocation(0)
            .WithArguments("Delay", "context.RequestAborted");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task GrpcContextBeforeHttpContext_PrefersRequestAborted()
    {
        var test = @"
using System.Threading.Tasks;
using Grpc.Core;
using Microsoft.AspNetCore.Http;

public class TestClass
{
    public async Task RunAsync(ServerCallContext grpc, HttpContext context)
    {
        await Task.{|#0:Delay|}(100);
    }
}" + HttpContextStub + GrpcContextStub;

        var expected = VerifyCS.Diagnostic("CC002")
            .WithLocation(0)
            .WithArguments("Delay", "context.RequestAborted");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task RequestAbortedPropertyOfWrongType_ShouldStayQuiet()
    {
        var test = @"
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace Microsoft.AspNetCore.Http
{
    public abstract class HttpContext
    {
        public string RequestAborted => """";
    }
}

public class Middleware
{
    public async Task InvokeAsync(HttpContext context)
    {
        await Task.Delay(100);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task StaticRequestAbortedProperty_ShouldStayQuiet()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace Microsoft.AspNetCore.Http
{
    public abstract class HttpContext
    {
        public static CancellationToken RequestAborted => default;
    }
}

public class Middleware
{
    public async Task InvokeAsync(HttpContext context)
    {
        await Task.Delay(100);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TwoServerCallContextParameters_PrefersTheFirst()
    {
        var test = @"
using System.Threading.Tasks;
using Grpc.Core;

public class GreeterService
{
    public async Task<string> SayHello(ServerCallContext first, ServerCallContext second)
    {
        await Task.{|#0:Delay|}(100);
        return ""hi"";
    }
}" + GrpcContextStub;

        var expected = VerifyCS.Diagnostic("CC002")
            .WithLocation(0)
            .WithArguments("Delay", "first.CancellationToken");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task PrimaryConstructorHttpContext_UsesRequestAborted()
    {
        var test = @"
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

public class Worker(HttpContext context)
{
    public async Task RunAsync()
    {
        await Task.{|#0:Delay|}(100);
    }
}" + HttpContextStub;

        var expected = VerifyCS.Diagnostic("CC002")
            .WithLocation(0)
            .WithArguments("Delay", "context.RequestAborted");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }
}
