using System.Reflection;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace CancelCop.Analyzer.Tests;

/// <summary>
/// False-positive regression guard: idiomatic, correct async code must trigger NONE of the rules.
/// Runs every analyzer in the package together, so a rule that over-fires on good code fails here.
/// </summary>
public class AllAnalyzersCleanCodeTests
{
    private sealed class AllAnalyzersTest : CSharpAnalyzerTest<MissingCancellationTokenAnalyzer, DefaultVerifier>
    {
        protected override IEnumerable<DiagnosticAnalyzer> GetDiagnosticAnalyzers() =>
            typeof(MissingCancellationTokenAnalyzer).Assembly.GetTypes()
                .Where(t => !t.IsAbstract && typeof(DiagnosticAnalyzer).IsAssignableFrom(t))
                .Select(t => (DiagnosticAnalyzer)Activator.CreateInstance(t)!);
    }

    [Fact]
    public async Task IdiomaticAsyncCode_ProducesNoDiagnostics()
    {
        var code = @"
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

internal sealed class CleanService
{
    public async Task RunAsync(IAsyncEnumerable<int> source, SemaphoreSlim gate, CancellationToken cancellationToken)
    {
        await Task.Delay(100, cancellationToken);
        await gate.WaitAsync(cancellationToken);

        for (int i = 0; i < 10; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
        }

        await foreach (var item in source.WithCancellation(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
        }

        await using var resource = new AsyncResource();
        await resource.UseAsync(cancellationToken);

        try
        {
            await Task.Delay(1, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _ = ex;
        }
    }

    public async IAsyncEnumerable<int> StreamAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        yield return 1;
        await Task.CompletedTask;
    }

    private sealed class AsyncResource : IAsyncDisposable
    {
        public Task UseAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() => default;
    }
}";

        var test = new AllAnalyzersTest
        {
            TestCode = code,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };

        await test.RunAsync();
    }

    [Fact]
    public async Task IdiomaticControllerCode_ProducesNoDiagnostics()
    {
        // A proper MVC controller action that accepts a token must satisfy both the general CC001
        // and the controller-specific CC005B (faithful ControllerBase/[HttpGet] stubs).
        var code = @"
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.AspNetCore.Mvc
{
    public abstract class ControllerBase { }
    public sealed class HttpGetAttribute : System.Attribute { }
}

internal sealed class UsersController : Microsoft.AspNetCore.Mvc.ControllerBase
{
    [Microsoft.AspNetCore.Mvc.HttpGet]
    public async Task<int> Get(CancellationToken cancellationToken)
    {
        await Task.Delay(1, cancellationToken);
        return 1;
    }
}";

        var test = new AllAnalyzersTest
        {
            TestCode = code,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };

        await test.RunAsync();
    }

    [Fact]
    public async Task IdiomaticMediatRAndSignalRCode_ProducesNoDiagnostics()
    {
        // A MediatR handler and a SignalR hub method that both accept a token must satisfy CC005A /
        // CC018 (and CC001) with zero diagnostics (faithful interface/base stubs).
        var code = @"
using System.Threading;
using System.Threading.Tasks;

namespace MediatR
{
    public interface IRequestHandler<TRequest, TResponse>
    {
        Task<TResponse> Handle(TRequest request, CancellationToken cancellationToken);
    }
}

namespace Microsoft.AspNetCore.SignalR
{
    public abstract class Hub { }
}

internal sealed class GetValueHandler : MediatR.IRequestHandler<string, int>
{
    public async Task<int> Handle(string request, CancellationToken cancellationToken)
    {
        await Task.Delay(1, cancellationToken);
        return 1;
    }
}

internal sealed class ChatHub : Microsoft.AspNetCore.SignalR.Hub
{
    public async Task Send(string message, CancellationToken cancellationToken)
    {
        await Task.Delay(1, cancellationToken);
    }
}";

        var test = new AllAnalyzersTest
        {
            TestCode = code,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };

        await test.RunAsync();
    }

    [Fact]
    public async Task IdiomaticMinimalApiCode_ProducesNoDiagnostics()
    {
        // A Minimal API endpoint whose handler lambda accepts a token must satisfy CC005C with zero
        // diagnostics (faithful IEndpointRouteBuilder + MapGet extension stubs).
        var code = @"
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;

namespace Microsoft.AspNetCore.Routing
{
    public interface IEndpointRouteBuilder { }
}

namespace Microsoft.AspNetCore.Builder
{
    public static class EndpointRouteBuilderExtensions
    {
        public static void MapGet(this Microsoft.AspNetCore.Routing.IEndpointRouteBuilder endpoints, string pattern, Delegate handler) { }
    }
}

internal sealed class Routes
{
    public void Map(Microsoft.AspNetCore.Routing.IEndpointRouteBuilder app)
    {
        app.MapGet(""/"", async (CancellationToken cancellationToken) => await Task.Delay(1, cancellationToken));
    }
}";

        var test = new AllAnalyzersTest
        {
            TestCode = code,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };

        await test.RunAsync();
    }

    [Fact]
    public async Task NestedScopesCapturingToken_ProduceNoDiagnostics()
    {
        // The shared scope walk must recognise the outer token captured by a local function and a
        // lambda, so the token-propagation rules stay quiet.
        var code = @"
using System;
using System.Threading;
using System.Threading.Tasks;

internal sealed class NestedService
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        async Task LocalAsync()
        {
            await Task.Delay(1, cancellationToken);
        }

        await LocalAsync();

        Func<Task> work = async () => await Task.Delay(1, cancellationToken);
        await work();

        for (int i = 0; i < 5; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
        }
    }
}";

        var test = new AllAnalyzersTest
        {
            TestCode = code,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };

        await test.RunAsync();
    }

    [Fact]
    public async Task NonAsyncUsingPatterns_ProduceNoDiagnostics()
    {
        // A non-async method that reads a using resource synchronously into a completed task, and
        // one that awaits it (async), must both be clean — CC027 only flags the deferred-receiver
        // case.
        var code = @"
using System;
using System.Threading.Tasks;

internal sealed class Resource : IDisposable
{
    public void Dispose() { }
    public int Value => 0;
    public Task<int> LoadAsync() => Task.FromResult(0);
}

internal sealed class ResourceService
{
    private Task<int> ReadValueAsync()
    {
        using var resource = new Resource();
        return Task.FromResult(resource.Value);
    }

    private async Task<int> LoadAsync()
    {
        using var resource = new Resource();
        return await resource.LoadAsync();
    }
}";

        var test = new AllAnalyzersTest
        {
            TestCode = code,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };

        await test.RunAsync();
    }

    [Fact]
    public async Task IdiomaticResourceLifecycle_ProducesNoDiagnostics()
    {
        // A linked CTS disposed via `using`, an `await using` async-disposable, and `await CancelAsync`
        // must all be clean across the lifecycle rules (CC014/CC022/CC025).
        var code = @"
using System;
using System.Threading;
using System.Threading.Tasks;

internal sealed class LifecycleService
{
    private sealed class AsyncResource : IAsyncDisposable
    {
        public Task UseAsync(CancellationToken token) => Task.CompletedTask;
        public ValueTask DisposeAsync() => default;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        await using var resource = new AsyncResource();
        await resource.UseAsync(cts.Token);

        await cts.CancelAsync();
    }
}";

        var test = new AllAnalyzersTest
        {
            TestCode = code,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };

        await test.RunAsync();
    }

    [Fact]
    public async Task ExoticSyntaxWithTokens_ProducesNoDiagnostics()
    {
        // Expression-bodied members, a switch expression returning a Task, and a non-async
        // Task-returning method must not trip any analyzer when the token is threaded correctly.
        var code = @"
using System.Threading;
using System.Threading.Tasks;

internal sealed class ExoticService
{
    public Task RunAsync(int x, CancellationToken cancellationToken) => x switch
    {
        0 => Task.CompletedTask,
        _ => Task.Delay(x, cancellationToken),
    };

    public async Task<int> ComputeAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(1, cancellationToken);
        return await Task.FromResult(42);
    }

    public Task DelegateAsync(CancellationToken cancellationToken)
        => InnerAsync(cancellationToken);

    private Task InnerAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}";

        var test = new AllAnalyzersTest
        {
            TestCode = code,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };

        await test.RunAsync();
    }

    [Fact]
    public async Task IdiomaticFrameworkCode_ProducesNoDiagnostics()
    {
        // Faithful stubs for the framework base types the property-token rules gate on, plus
        // representative implementations that correctly observe their cancellation source.
        var code = @"
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Extensions.Hosting
{
    public abstract class BackgroundService
    {
        protected abstract Task ExecuteAsync(CancellationToken stoppingToken);
    }
}

namespace Grpc.Core
{
    public abstract class ServerCallContext
    {
        public CancellationToken CancellationToken => default;
    }
}

internal sealed class Worker : Microsoft.Extensions.Hosting.BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(1000, stoppingToken);
        }
    }
}

internal abstract class GreeterBase
{
    public abstract Task<string> HandleAsync(string request, Grpc.Core.ServerCallContext context);
}

internal sealed class GreeterService : GreeterBase
{
    public override async Task<string> HandleAsync(string request, Grpc.Core.ServerCallContext context)
    {
        await Task.Delay(1, context.CancellationToken);
        return request;
    }
}";

        var test = new AllAnalyzersTest
        {
            TestCode = code,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };

        await test.RunAsync();
    }

    [Fact]
    public async Task ModernCSharpShapes_ProduceNoDiagnostics()
    {
        // Primary-constructor class and record struct, a file-scoped namespace, and an expression-bodied
        // async method that propagates a primary-constructor token must all stay clean. The shared scope
        // walk covers primary constructors, so the propagation rules see the captured token.
        var code = @"
using System.Threading;
using System.Threading.Tasks;

namespace Modern;

internal sealed class Worker(CancellationToken lifetime)
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(1, cancellationToken);
        await TickAsync();
    }

    private async Task TickAsync() => await Task.Delay(1, lifetime);
}

internal record struct Job(int Id)
{
    public async Task<int> ProcessAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(1, cancellationToken);
        return Id;
    }
}";

        var test = new AllAnalyzersTest
        {
            TestCode = code,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };

        await test.RunAsync();
    }

    [Fact]
    public async Task IdiomaticAsyncFileIo_ProducesNoDiagnostics()
    {
        // The async File counterparts flowing the in-scope token are the correct shape CC028 steers
        // toward, so they (and every other rule) must stay silent.
        var code = @"
using System.IO;
using System.Threading;
using System.Threading.Tasks;

internal sealed class FileService
{
    public async Task<string> LoadAsync(string path, CancellationToken cancellationToken)
    {
        var text = await File.ReadAllTextAsync(path, cancellationToken);
        await File.WriteAllTextAsync(path, text, cancellationToken);
        return text;
    }
}";

        var test = new AllAnalyzersTest
        {
            TestCode = code,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };

        await test.RunAsync();
    }

    [Fact]
    public async Task IdiomaticLibraryStyleAsync_ProducesNoDiagnostics()
    {
        // Library-style async: ConfigureAwait(false) on every await, a ValueTask-returning method, an
        // await using over an IAsyncDisposable whose factory flows the token, and a TaskCompletionSource
        // — all threading the token. None of the rules may fire.
        var code = @"
using System;
using System.Threading;
using System.Threading.Tasks;

internal interface IConnection : IAsyncDisposable
{
    ValueTask<int> ReadAsync(CancellationToken cancellationToken);
}

internal sealed class LibraryService
{
    private readonly Func<CancellationToken, ValueTask<IConnection>> _factory;

    public LibraryService(Func<CancellationToken, ValueTask<IConnection>> factory) => _factory = factory;

    public async ValueTask<int> QueryAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _factory(cancellationToken).ConfigureAwait(false);
        return await connection.ReadAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task<int> WrapAsync(CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<int>();
        cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
        return tcs.Task;
    }
}";

        var test = new AllAnalyzersTest
        {
            TestCode = code,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };

        await test.RunAsync();
    }

    [Fact]
    public async Task IdiomaticChannelsAndParallelForEachAsync_ProduceNoDiagnostics()
    {
        // System.Threading.Channels and Parallel.ForEachAsync are core modern async patterns. A
        // channel producer/consumer threading the token, and a ForEachAsync whose body awaits with the
        // token, must not trip any rule — notably CC010 must stay quiet because ReadAllAsync(token)
        // already flows a token into the async stream.
        var code = @"
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

internal sealed class PipelineService
{
    public async Task ProduceAsync(ChannelWriter<int> writer, CancellationToken cancellationToken)
    {
        for (int i = 0; i < 10; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await writer.WriteAsync(i, cancellationToken);
        }
    }

    public async Task ConsumeAsync(ChannelReader<int> reader, CancellationToken cancellationToken)
    {
        await foreach (var item in reader.ReadAllAsync(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(item, cancellationToken);
        }
    }

    public async Task FanOutAsync(IEnumerable<int> items, CancellationToken cancellationToken)
    {
        await Parallel.ForEachAsync(items, cancellationToken, async (item, token) =>
        {
            await Task.Delay(item, token);
        });
    }
}";

        var test = new AllAnalyzersTest
        {
            TestCode = code,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };

        await test.RunAsync();
    }

    [Fact]
    public async Task IdiomaticRawStreamAndHttpClient_ProduceNoDiagnostics()
    {
        // Raw Stream async I/O (ReadAsync/WriteAsync/CopyToAsync) and HttpClient.SendAsync, all
        // threading the in-scope token, are the canonical correct shapes — no rule may fire.
        var code = @"
using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

internal sealed class TransferService
{
    public async Task CopyAsync(Stream input, Stream output, byte[] buffer, CancellationToken cancellationToken)
    {
        int read = await input.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
        await output.WriteAsync(buffer, 0, read, cancellationToken);
        await input.CopyToAsync(output, cancellationToken);
        await output.FlushAsync(cancellationToken);
    }

    public async Task<string> FetchAsync(HttpClient client, HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var response = await client.SendAsync(request, cancellationToken);
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }
}";

        var test = new AllAnalyzersTest
        {
            TestCode = code,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };

        await test.RunAsync();
    }

    [Fact]
    public async Task IdiomaticAsyncStreamWriter_ProducesNoDiagnostics()
    {
        // The async StreamWriter counterparts are the shape CC028 steers toward. WriteAsync(string)
        // has no CancellationToken overload (so passing none is correct) while FlushAsync flows the
        // in-scope token; neither — nor any other rule — may fire.
        var code = @"
using System.IO;
using System.Threading;
using System.Threading.Tasks;

internal sealed class WriterService
{
    public async Task PersistAsync(StreamWriter writer, string text, CancellationToken cancellationToken)
    {
        await writer.WriteAsync(text);
        await writer.FlushAsync(cancellationToken);
    }
}";

        var test = new AllAnalyzersTest
        {
            TestCode = code,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };

        await test.RunAsync();
    }

    [Fact]
    public async Task PatternMatchingAndGenerics_ProduceNoDiagnostics()
    {
        // A switch statement with awaited arms, a generic async method, and a pattern-matched catch
        // filter that lets cancellation propagate must not trip any analyzer when the token is threaded.
        var code = @"
using System;
using System.Threading;
using System.Threading.Tasks;

internal sealed class Dispatcher
{
    public async Task<T> RouteAsync<T>(int kind, T value, CancellationToken cancellationToken)
    {
        switch (kind)
        {
            case 0:
                await Task.Delay(1, cancellationToken);
                break;
            default:
                await Task.Delay(2, cancellationToken);
                break;
        }

        try
        {
            await Task.Delay(1, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _ = ex;
        }

        return value;
    }
}";

        var test = new AllAnalyzersTest
        {
            TestCode = code,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };

        await test.RunAsync();
    }
}
