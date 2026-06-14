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
    public async Task IdiomaticAsyncIteratorProducer_ProducesNoDiagnostics()
    {
        // A correctly-written async-stream producer: [EnumeratorCancellation] token, a loop with an
        // explicit cancellation check, and awaited work flowing the token. It must satisfy CC011 (the
        // attribute), CC016 (the token is consumed by the iterator), CC009 (the loop check), and CC001
        // (it has a token) — i.e. zero diagnostics across every analyzer.
        var code = @"
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

internal sealed class StreamProducer
{
    public async IAsyncEnumerable<int> ProduceAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        for (int i = 0; i < 10; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(10, cancellationToken);
            yield return i;
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
    public async Task IdiomaticLinkedTokenSourceTimeout_ProducesNoDiagnostics()
    {
        // The canonical 'combine the caller's token with a timeout' idiom: a using-declared linked CTS,
        // CancelAfter, and the linked token passed on. CC014 (disposal) is satisfied by `using var`,
        // CC002 by passing linked.Token, and CC022 must not confuse CancelAfter with Cancel().
        var code = @"
using System;
using System.Threading;
using System.Threading.Tasks;

internal sealed class TimeoutService
{
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(TimeSpan.FromSeconds(5));
        return await ComputeAsync(linked.Token);
    }

    private Task<int> ComputeAsync(CancellationToken cancellationToken) => Task.FromResult(0);
}";

        var test = new AllAnalyzersTest
        {
            TestCode = code,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };

        await test.RunAsync();
    }

    [Fact]
    public async Task IdiomaticReceiveLoop_ProducesNoDiagnostics()
    {
        // A message receive loop: condition checks the token, ReceiveAsync and handling both flow it.
        // No analyzer fires.
        var code = @"
using System.Threading;
using System.Threading.Tasks;

internal interface IChannel
{
    Task<string?> ReceiveAsync(CancellationToken cancellationToken);
}

internal sealed class Listener
{
    public async Task RunAsync(IChannel channel, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var message = await channel.ReceiveAsync(cancellationToken);
            if (message is null)
                break;

            await HandleAsync(message, cancellationToken);
        }
    }

    private Task HandleAsync(string message, CancellationToken cancellationToken) => Task.CompletedTask;
}";

        var test = new AllAnalyzersTest
        {
            TestCode = code,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };

        await test.RunAsync();
    }

    [Fact]
    public async Task IdiomaticTaskRunCpuOffload_ProducesNoDiagnostics()
    {
        // Offloading CPU-bound work with Task.Run, passing the token to Task.Run and checking it inside
        // the compute loop. No analyzer fires (the lambda returns a value, so CC024 does not apply).
        var code = @"
using System.Threading;
using System.Threading.Tasks;

internal sealed class ComputeService
{
    public Task<long> SumAsync(int n, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            long total = 0;
            for (int i = 0; i < n; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                total += i;
            }
            return total;
        }, cancellationToken);
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
    public async Task IdiomaticRecursiveAsyncTraversal_ProducesNoDiagnostics()
    {
        // A recursive async tree walk: cancellation check on entry, token flowed into the recursive
        // call and the per-node await. No analyzer fires.
        var code = @"
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

internal sealed class Node
{
    public IEnumerable<Node> Children { get; } = new List<Node>();
}

internal sealed class TreeWalker
{
    public async Task VisitAsync(Node node, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await ProcessAsync(node, cancellationToken);
        foreach (var child in node.Children)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await VisitAsync(child, cancellationToken);
        }
    }

    private Task ProcessAsync(Node node, CancellationToken cancellationToken) => Task.CompletedTask;
}";

        var test = new AllAnalyzersTest
        {
            TestCode = code,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };

        await test.RunAsync();
    }

    [Fact]
    public async Task IdiomaticValueTaskCacheFastPath_ProducesNoDiagnostics()
    {
        // A ValueTask-returning method with a synchronous cache fast-path and a token-flowing slow path.
        // The token is observed (slow path), so CC016 must not fire, and nothing else should either.
        var code = @"
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

internal sealed class CachingService
{
    private readonly Dictionary<int, string> _cache = new();

    public async ValueTask<string> GetAsync(int key, CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(key, out var cached))
            return cached;

        var value = await LoadAsync(key, cancellationToken);
        _cache[key] = value;
        return value;
    }

    private Task<string> LoadAsync(int key, CancellationToken cancellationToken) => Task.FromResult(string.Empty);
}";

        var test = new AllAnalyzersTest
        {
            TestCode = code,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };

        await test.RunAsync();
    }

    [Fact]
    public async Task IdiomaticHttpPollingLoop_ProducesNoDiagnostics()
    {
        // A poll-until-cancelled loop: condition checks the token, the HTTP call and the delay both flow
        // it. CC009 (condition check), CC004/CC002 (token passed) and the rest stay quiet.
        var code = @"
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

internal sealed class PollingService
{
    private readonly HttpClient _client = new HttpClient();

    public async Task RunAsync(string url, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var payload = await _client.GetStringAsync(url, cancellationToken);
            _ = payload;
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
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
    public async Task IdiomaticAsyncLazyInitialization_ProducesNoDiagnostics()
    {
        // Async double-checked lazy init guarded by a SemaphoreSlim: WaitAsync(token), re-check, init,
        // Release in finally. No analyzer fires.
        var code = @"
using System.Threading;
using System.Threading.Tasks;

internal sealed class LazyService
{
    private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
    private string? _value;

    public async Task<string> GetAsync(CancellationToken cancellationToken)
    {
        if (_value is not null)
            return _value;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            _value ??= await LoadAsync(cancellationToken);
            return _value;
        }
        finally
        {
            _gate.Release();
        }
    }

    private Task<string> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(string.Empty);
}";

        var test = new AllAnalyzersTest
        {
            TestCode = code,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };

        await test.RunAsync();
    }

    [Fact]
    public async Task IdiomaticWhenAnyTimeoutRace_ProducesNoDiagnostics()
    {
        // Racing real work against Task.Delay(timeout, token) with Task.WhenAny is a common soft-timeout
        // pattern. Both tasks flow the token; WhenAny has no token overload, so CC002 must not ask.
        var code = @"
using System;
using System.Threading;
using System.Threading.Tasks;

internal sealed class TimeoutRaceService
{
    public async Task<bool> RunAsync(Func<CancellationToken, Task> work, CancellationToken cancellationToken)
    {
        var workTask = work(cancellationToken);
        var timeout = Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
        var winner = await Task.WhenAny(workTask, timeout);
        return winner == workTask;
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
    public async Task IdiomaticNestedAwaitUsing_ProducesNoDiagnostics()
    {
        // Two await using declarations over IAsyncDisposable resources whose factories flow the token,
        // then token-flowing work. CC025 (already await using) and the rest must stay quiet.
        var code = @"
using System;
using System.Threading;
using System.Threading.Tasks;

internal interface IConnection : IAsyncDisposable { }
internal interface ITransaction : IAsyncDisposable { }

internal sealed class UnitOfWork
{
    private readonly Func<CancellationToken, ValueTask<IConnection>> _connect;
    private readonly Func<IConnection, CancellationToken, ValueTask<ITransaction>> _begin;

    public UnitOfWork(
        Func<CancellationToken, ValueTask<IConnection>> connect,
        Func<IConnection, CancellationToken, ValueTask<ITransaction>> begin)
    {
        _connect = connect;
        _begin = begin;
    }

    public async Task RunAsync(Func<ITransaction, CancellationToken, Task> work, CancellationToken cancellationToken)
    {
        await using var connection = await _connect(cancellationToken);
        await using var transaction = await _begin(connection, cancellationToken);
        await work(transaction, cancellationToken);
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
    public async Task IdiomaticTokenRegisterCleanup_ProducesNoDiagnostics()
    {
        // Registering a cancellation callback and awaiting a TaskCompletionSource is a common bridge to
        // callback-based APIs; the registration is disposed via await using. No analyzer may fire.
        var code = @"
using System.Threading;
using System.Threading.Tasks;

internal sealed class BridgeService
{
    public async Task<int> WaitForSignalAsync(CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<int>();
        await using (cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken)))
        {
            return await tcs.Task;
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
    public async Task IdiomaticProgressReportingLoop_ProducesNoDiagnostics()
    {
        // A progress-reporting loop: cancellation check each iteration, token-flowing await, and
        // IProgress<T>.Report. Nothing may fire.
        var code = @"
using System;
using System.Threading;
using System.Threading.Tasks;

internal sealed class ImportService
{
    public async Task RunAsync(int count, IProgress<int> progress, CancellationToken cancellationToken)
    {
        for (int i = 0; i < count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(10, cancellationToken);
            progress.Report(i);
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
    public async Task IdiomaticSemaphoreGatedSection_ProducesNoDiagnostics()
    {
        // The canonical async mutual-exclusion pattern: await WaitAsync(token), work in try, Release in
        // finally. CC026 (no blocking Wait), CC002, and the rest must stay quiet.
        var code = @"
using System.Threading;
using System.Threading.Tasks;

internal sealed class GatedService
{
    private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await ComputeAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private Task<int> ComputeAsync(CancellationToken cancellationToken) => Task.FromResult(0);
}";

        var test = new AllAnalyzersTest
        {
            TestCode = code,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };

        await test.RunAsync();
    }

    [Fact]
    public async Task IdiomaticWhenAllFanOut_ProducesNoDiagnostics()
    {
        // Fan-out with Task.WhenAll over a LINQ projection that threads the token into each task.
        // Task.WhenAll has no token overload, so CC002 must not ask for one, and each ProcessAsync call
        // already receives the token.
        var code = @"
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

internal sealed class FanOutService
{
    public async Task<int[]> RunAsync(IEnumerable<int> items, CancellationToken cancellationToken)
    {
        var tasks = items.Select(i => ProcessAsync(i, cancellationToken));
        return await Task.WhenAll(tasks);
    }

    private Task<int> ProcessAsync(int item, CancellationToken cancellationToken) => Task.FromResult(item);
}";

        var test = new AllAnalyzersTest
        {
            TestCode = code,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };

        await test.RunAsync();
    }

    [Fact]
    public async Task IdiomaticRetryWithBackoff_ProducesNoDiagnostics()
    {
        // A canonical async retry loop: a cancellation check at the top of each attempt, awaited work
        // and the backoff delay both flowing the token. No analyzer may fire.
        var code = @"
using System;
using System.Threading;
using System.Threading.Tasks;

internal sealed class RetryService
{
    public async Task<int> RunAsync(Func<CancellationToken, Task<int>> action, CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return await action(cancellationToken);
            }
            catch (TimeoutException)
            {
                await Task.Delay(TimeSpan.FromSeconds(attempt), cancellationToken);
            }
        }

        return -1;
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
