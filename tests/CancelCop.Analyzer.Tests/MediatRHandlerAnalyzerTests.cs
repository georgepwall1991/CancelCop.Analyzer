using System.Collections.Immutable;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace CancelCop.Analyzer.Tests;

public class MediatRHandlerAnalyzerTests
{
    private static CSharpAnalyzerTest<MediatRHandlerAnalyzer, DefaultVerifier> CreateTest(string testCode, params DiagnosticResult[] expected)
    {
        var test = new CSharpAnalyzerTest<MediatRHandlerAnalyzer, DefaultVerifier>
        {
            TestCode = testCode,
            ReferenceAssemblies = Microsoft.CodeAnalysis.Testing.ReferenceAssemblies.Net.Net90
                .AddPackages(ImmutableArray.Create(new PackageIdentity("MediatR", "13.0.0"))),
            CompilerDiagnostics = CompilerDiagnostics.None,
        };

        test.ExpectedDiagnostics.AddRange(expected);
        return test;
    }

    [Fact]
    public async Task RequestHandler_WithoutCancellationToken_ShouldReportDiagnostic()
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

        var expected = new DiagnosticResult("CC005A", Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("Handle");

        await CreateTest(test, expected).RunAsync();
    }

    [Fact]
    public async Task RequestHandler_WithCancellationToken_ShouldNotReportDiagnostic()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;
using MediatR;

public class GetUserQuery : IRequest<string> { }

public class GetUserQueryHandler : IRequestHandler<GetUserQuery, string>
{
    public async Task<string> Handle(GetUserQuery request, CancellationToken cancellationToken)
    {
        await Task.Delay(100, cancellationToken);
        return ""User"";
    }
}";

        await CreateTest(test).RunAsync();
    }

    [Fact]
    public async Task RequestHandler_SyncMethod_ShouldNotReportDiagnostic()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;
using MediatR;

public class GetUserQuery : IRequest<string> { }

public class GetUserQueryHandler : IRequestHandler<GetUserQuery, string>
{
    public Task<string> Handle(GetUserQuery request, CancellationToken cancellationToken)
    {
        return Task.FromResult(""User"");
    }
}";

        await CreateTest(test).RunAsync();
    }

    [Fact]
    public async Task RequestHandler_VoidReturn_WithoutCancellationToken_ShouldReportDiagnostic()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;
using MediatR;

public class SendEmailCommand : IRequest { }

public class SendEmailCommandHandler : IRequestHandler<SendEmailCommand>
{
    public async Task {|#0:Handle|}(SendEmailCommand request)
    {
        await Task.Delay(100);
    }
}";

        var expected = new DiagnosticResult("CC005A", Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("Handle");

        await CreateTest(test, expected).RunAsync();
    }

    [Fact]
    public async Task NonHandlerMethod_WithoutCancellationToken_ShouldNotReportDiagnostic()
    {
        var test = @"
using System.Threading.Tasks;

public class RegularClass
{
    public async Task Handle(string request)
    {
        await Task.Delay(100);
    }
}";

        await CreateTest(test).RunAsync();
    }
}
