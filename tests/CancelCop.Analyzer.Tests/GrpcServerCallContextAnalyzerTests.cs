using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    CancelCop.Analyzer.GrpcServerCallContextAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace CancelCop.Analyzer.Tests;

public class GrpcServerCallContextAnalyzerTests
{
    // A faithful stand-in for Grpc.Core.ServerCallContext (the analyzer gates on the parameter
    // type's name + namespace), so the tests need no gRPC package.
    private const string ContextStub = @"
namespace Grpc.Core
{
    public abstract class ServerCallContext
    {
        public System.Threading.CancellationToken CancellationToken => default;
        public NestedContext Nested => null;
    }

    public class NestedContext
    {
        public System.Threading.CancellationToken CancellationToken => default;
    }
}";

    [Fact]
    public async Task GrpcMethod_IgnoresToken_ShouldReportDiagnostic()
    {
        var test = @"
using System.Threading.Tasks;
using Grpc.Core;

public class GreeterService
{
    private Task SaveAsync() => Task.CompletedTask;

    public async Task<string> SayHello(string request, ServerCallContext {|#0:context|})
    {
        await SaveAsync();
        return ""hi"";
    }
}" + ContextStub;

        var expected = VerifyCS.Diagnostic("CC020").WithLocation(0).WithArguments("context");
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task GrpcMethod_ObservesToken_ShouldNotReportDiagnostic()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;

public class GreeterService
{
    private Task SaveAsync(CancellationToken token) => Task.CompletedTask;

    public async Task<string> SayHello(string request, ServerCallContext context)
    {
        await SaveAsync(context.CancellationToken);
        return ""hi"";
    }
}" + ContextStub;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task GrpcMethod_NullConditionallyObservesToken_ShouldNotReportDiagnostic()
    {
        var test = @"
using System.Threading.Tasks;
using Grpc.Core;

public class GreeterService
{
    public async Task SayHello(ServerCallContext context)
    {
        _ = context?.CancellationToken;
        await Task.Yield();
    }
}" + ContextStub;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task GrpcMethod_NullConditionallyChecksToken_ShouldNotReportDiagnostic()
    {
        var test = @"
using System.Threading.Tasks;
using Grpc.Core;

public class GreeterService
{
    public async Task SayHello(ServerCallContext context)
    {
        context?.CancellationToken.ThrowIfCancellationRequested();
        await Task.Yield();
    }
}" + ContextStub;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task GrpcMethod_NullConditionallyReadsNestedToken_ShouldReportDiagnostic()
    {
        var test = @"
using System.Threading.Tasks;
using Grpc.Core;

public class GreeterService
{
    public async Task SayHello(ServerCallContext {|#0:context|})
    {
        _ = context?.Nested.CancellationToken;
        await Task.Yield();
    }
}" + ContextStub;

        var expected = VerifyCS.Diagnostic("CC020").WithLocation(0).WithArguments("context");
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task GrpcMethod_NullConditionallyChecksNestedToken_ShouldReportDiagnostic()
    {
        var test = @"
using System.Threading.Tasks;
using Grpc.Core;

public class GreeterService
{
    public async Task SayHello(ServerCallContext {|#0:context|})
    {
        context?.Nested.CancellationToken.ThrowIfCancellationRequested();
        await Task.Yield();
    }
}" + ContextStub;

        var expected = VerifyCS.Diagnostic("CC020").WithLocation(0).WithArguments("context");
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task GrpcMethod_ObservesTokenViaThrowIfCancellationRequested_ShouldNotReportDiagnostic()
    {
        // Observing the token by calling a method on it (ThrowIfCancellationRequested) is still an
        // access of context.CancellationToken, so CC020 must stay quiet.
        var test = @"
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;

public class GreeterService
{
    public async Task<string> SayHello(string request, ServerCallContext context)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        await Task.Yield();
        return ""hi"";
    }
}" + ContextStub;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task GrpcMethod_ObservesTokenViaAlias_ShouldNotReportDiagnostic()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;

public class GreeterService
{
    private Task SaveAsync(CancellationToken token) => Task.CompletedTask;

    public async Task<string> SayHello(string request, ServerCallContext context)
    {
        var token = context.CancellationToken;
        await SaveAsync(token);
        return ""hi"";
    }
}" + ContextStub;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task GrpcMethod_NameofTokenOnly_ShouldReportDiagnostic()
    {
        var test = @"
using System.Threading.Tasks;
using Grpc.Core;

public class GreeterService
{
    public async Task<string> SayHello(string request, ServerCallContext {|#0:context|})
    {
        _ = nameof(context.CancellationToken);
        await Task.Yield();
        return ""hi"";
    }
}" + ContextStub;

        var expected = VerifyCS.Diagnostic("CC020").WithLocation(0).WithArguments("context");
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task GrpcMethod_PassesContextOn_ShouldNotReportDiagnostic()
    {
        var test = @"
using System.Threading.Tasks;
using Grpc.Core;

public class GreeterService
{
    private Task Helper(ServerCallContext c) => Task.CompletedTask;

    public async Task<string> SayHello(string request, ServerCallContext context)
    {
        await Helper(context);
        return ""hi"";
    }
}" + ContextStub;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task GrpcMethod_PassesContextAsReducedExtensionReceiver_ShouldNotReportDiagnostic()
    {
        var test = @"
using System.Threading.Tasks;
using Grpc.Core;

public static class ServerCallContextExtensions
{
    public static Task ProcessAsync(this ServerCallContext context) => Task.CompletedTask;
}

public class GreeterService
{
    public async Task<string> SayHello(string request, ServerCallContext context)
    {
        await context.ProcessAsync();
        return ""hi"";
    }
}" + ContextStub;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task GrpcMethod_PassesContextAsNullConditionalExtensionReceiver_ShouldNotReportDiagnostic()
    {
        var test = @"
using System.Threading.Tasks;
using Grpc.Core;

public static class ServerCallContextExtensions
{
    public static Task ProcessAsync(this ServerCallContext context) => Task.CompletedTask;
}

public class GreeterService
{
    public async Task SayHello(ServerCallContext context)
    {
        await (context?.ProcessAsync() ?? Task.CompletedTask);
    }
}" + ContextStub;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task GrpcMethod_NullConditionalOrdinaryInstanceCall_ShouldReportDiagnostic()
    {
        var test = @"
using System.Threading.Tasks;
using Grpc.Core;

public class GreeterService
{
    public async Task SayHello(ServerCallContext {|#0:context|})
    {
        _ = context?.ToString();
        await Task.Yield();
    }
}" + ContextStub;

        var expected = VerifyCS.Diagnostic("CC020").WithLocation(0).WithArguments("context");
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task GrpcMethod_OrdinaryInstanceCall_ShouldReportDiagnostic()
    {
        var test = @"
using System.Threading.Tasks;
using Grpc.Core;

public class GreeterService
{
    public async Task<string> SayHello(string request, ServerCallContext {|#0:context|})
    {
        _ = context.ToString();
        await Task.Yield();
        return ""hi"";
    }
}" + ContextStub;

        var expected = VerifyCS.Diagnostic("CC020").WithLocation(0).WithArguments("context");
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task GrpcMethod_NoAsyncWork_ShouldNotReportDiagnostic()
    {
        var test = @"
using Grpc.Core;

public class GreeterService
{
    public string SayHello(string request, ServerCallContext context)
    {
        return ""hi"";
    }
}" + ContextStub;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task NonGrpcMethod_ShouldNotReportDiagnostic()
    {
        var test = @"
using System.Threading.Tasks;

public class GreeterService
{
    private Task SaveAsync() => Task.CompletedTask;

    public async Task<string> SayHello(string request)
    {
        await SaveAsync();
        return ""hi"";
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
