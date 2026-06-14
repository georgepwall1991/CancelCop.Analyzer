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
    public async Task IdiomaticEtlIngestionPipeline_ProducesNoDiagnostics()
    {
        // Capstone: a realistic ETL pipeline combining await foreach over a token-flowing source
        // (CC010), a per-item cancellation check (CC009), token-flowing transform/write/flush (CC002),
        // an entry guard, and token-flowing transform/write/flush (CC002). Every analyzer must stay silent.
        var code = @"
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

internal interface ISink
{
    Task WriteAsync(int value, CancellationToken cancellationToken);
    Task FlushAsync(CancellationToken cancellationToken);
}

internal sealed class IngestionService
{
    public async Task IngestAsync(IAsyncEnumerable<int> source, ISink sink, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await foreach (var raw in source.WithCancellation(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var transformed = await TransformAsync(raw, cancellationToken);
            await sink.WriteAsync(transformed, cancellationToken);
        }

        await sink.FlushAsync(cancellationToken);
    }

    private Task<int> TransformAsync(int raw, CancellationToken cancellationToken) => Task.FromResult(raw * 2);
}";

        var test = new AllAnalyzersTest
        {
            TestCode = code,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };

        await test.RunAsync();
    }

    [Fact]
    public async Task IdiomaticRegisterWithState_ProducesNoDiagnostics()
    {
        // The closure-free Register(callback, state) overload, registration disposed via await using,
        // awaiting a TaskCompletionSource. No analyzer fires.
        var code = @"
using System.Threading;
using System.Threading.Tasks;

internal sealed class SignalAwaiter
{
    public async Task<int> WaitAsync(CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<int>();
        await using (cancellationToken.Register(static s => ((TaskCompletionSource<int>)s!).TrySetCanceled(), tcs))
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
    public async Task IdiomaticConfiguredAwaitUsing_ProducesNoDiagnostics()
    {
        // Library-style await using over a ConfiguredAsyncDisposable (ConfigureAwait(false) on the
        // resource), with token-flowing work configured too. CC025 is satisfied (already await using).
        // No analyzer fires.
        var code = @"
using System;
using System.Threading;
using System.Threading.Tasks;

internal sealed class ConfiguredScopeService
{
    public async Task RunAsync(IAsyncDisposable resource, CancellationToken cancellationToken)
    {
        await using (resource.ConfigureAwait(false))
        {
            await DoWorkAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private Task DoWorkAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}";

        var test = new AllAnalyzersTest
        {
            TestCode = code,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };

        await test.RunAsync();
    }

    [Fact]
    public async Task IdiomaticAccumulateTasksThenWhenAll_ProducesNoDiagnostics()
    {
        // Build a task list in a loop (per-iteration check, token-flowing starts), then await WhenAll.
        // No analyzer fires.
        var code = @"
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

internal sealed class BatchStarter
{
    public async Task RunAsync(IEnumerable<int> items, CancellationToken cancellationToken)
    {
        var tasks = new List<Task>();
        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            tasks.Add(ProcessAsync(item, cancellationToken));
        }
        await Task.WhenAll(tasks);
    }

    private Task ProcessAsync(int item, CancellationToken cancellationToken) => Task.CompletedTask;
}";

        var test = new AllAnalyzersTest
        {
            TestCode = code,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };

        await test.RunAsync();
    }

    [Fact]
    public async Task IdiomaticTryWriteProducerLoop_ProducesNoDiagnostics()
    {
        // A producer using the non-blocking TryWrite into an unbounded channel, with a per-iteration
        // cancellation check and cooperative yield, then Complete. No analyzer fires.
        var code = @"
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

internal sealed class TryWriteProducer
{
    public async Task ProduceAsync(Channel<int> channel, int count, CancellationToken cancellationToken)
    {
        for (int i = 0; i < count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            channel.Writer.TryWrite(i);
            await Task.Yield();
        }
        channel.Writer.Complete();
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
    public async Task IdiomaticSoftCancelReturningDefault_ProducesNoDiagnostics()
    {
        // Catching OperationCanceledException specifically (not a broad catch) to return a sentinel on
        // soft-cancel is a deliberate choice CC019 does not flag (it targets broad swallowing catches).
        var code = @"
using System;
using System.Threading;
using System.Threading.Tasks;

internal sealed class SoftCancelService
{
    public async Task<int> TryComputeAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await ComputeAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return -1;
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
    public async Task IdiomaticCooperativeYieldLoop_ProducesNoDiagnostics()
    {
        // A loop that yields cooperatively with Task.Yield() (no token overload, so CC002 is quiet),
        // a per-iteration cancellation check, and token-flowing work. No analyzer fires.
        var code = @"
using System.Threading;
using System.Threading.Tasks;

internal sealed class CooperativeWorker
{
    public async Task RunAsync(int count, CancellationToken cancellationToken)
    {
        for (int i = 0; i < count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            await StepAsync(i, cancellationToken);
        }
    }

    private Task StepAsync(int i, CancellationToken cancellationToken) => Task.CompletedTask;
}";

        var test = new AllAnalyzersTest
        {
            TestCode = code,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };

        await test.RunAsync();
    }

    [Fact]
    public async Task IdiomaticArrayPoolRentReturn_ProducesNoDiagnostics()
    {
        // Renting a buffer from ArrayPool, reading into it with a token-flowing ReadAsync, returning it
        // in finally. No analyzer fires.
        var code = @"
using System;
using System.Buffers;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

internal sealed class PooledReader
{
    public async Task<int> ReadAsync(Stream stream, CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(1024);
        try
        {
            return await stream.ReadAsync(buffer.AsMemory(0, 1024), cancellationToken);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
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
    public async Task IdiomaticTransactionCommitRollback_ProducesNoDiagnostics()
    {
        // await using a transaction, commit on success, rollback then rethrow on failure. The catch-all
        // rethrows (CC019 suppressed), all calls flow the token. No analyzer fires.
        var code = @"
using System;
using System.Threading;
using System.Threading.Tasks;

internal interface ITransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken);
    Task RollbackAsync(CancellationToken cancellationToken);
}

internal sealed class UnitOfWork
{
    private readonly Func<CancellationToken, ValueTask<ITransaction>> _begin;

    public UnitOfWork(Func<CancellationToken, ValueTask<ITransaction>> begin) => _begin = begin;

    public async Task ExecuteAsync(Func<CancellationToken, Task> work, CancellationToken cancellationToken)
    {
        await using var tx = await _begin(cancellationToken);
        try
        {
            await work(cancellationToken);
            await tx.CommitAsync(cancellationToken);
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            throw;
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
    public async Task IdiomaticSubscriptionStreamConsume_ProducesNoDiagnostics()
    {
        // await using a subscription (IAsyncDisposable), then consuming its message stream with
        // WithCancellation(token) and a per-item check. No analyzer fires.
        var code = @"
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

internal interface ISubscription : IAsyncDisposable
{
    IAsyncEnumerable<string> Messages { get; }
}

internal sealed class Broker
{
    private readonly Func<string, CancellationToken, ValueTask<ISubscription>> _subscribe;

    public Broker(Func<string, CancellationToken, ValueTask<ISubscription>> subscribe) => _subscribe = subscribe;

    public async Task ListenAsync(string topic, CancellationToken cancellationToken)
    {
        await using var subscription = await _subscribe(topic, cancellationToken);
        await foreach (var message in subscription.Messages.WithCancellation(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
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
    public async Task IdiomaticEntryGuardThenWork_ProducesNoDiagnostics()
    {
        // A fast-fail entry guard (ThrowIfCancellationRequested at the top) followed by token-flowing
        // work. No analyzer fires.
        var code = @"
using System.Threading;
using System.Threading.Tasks;

internal sealed class GuardedComputer
{
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var prepared = await PrepareAsync(cancellationToken);
        return await ComputeAsync(prepared, cancellationToken);
    }

    private Task<int> PrepareAsync(CancellationToken cancellationToken) => Task.FromResult(0);
    private Task<int> ComputeAsync(int input, CancellationToken cancellationToken) => Task.FromResult(input);
}";

        var test = new AllAnalyzersTest
        {
            TestCode = code,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };

        await test.RunAsync();
    }

    [Fact]
    public async Task IdiomaticSwitchExpressionReturningTasks_ProducesNoDiagnostics()
    {
        // A non-async method that returns a Task selected by a switch expression, each arm flowing the
        // token. No await (so CC016 N/A), token passed on each arm. No analyzer fires.
        var code = @"
using System.Threading;
using System.Threading.Tasks;

internal sealed class Dispatcher
{
    public Task HandleAsync(int command, CancellationToken cancellationToken) => command switch
    {
        0 => ReadAsync(cancellationToken),
        1 => WriteAsync(cancellationToken),
        _ => Task.CompletedTask,
    };

    private Task ReadAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    private Task WriteAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}";

        var test = new AllAnalyzersTest
        {
            TestCode = code,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };

        await test.RunAsync();
    }

    [Fact]
    public async Task IdiomaticManualAsyncEnumerator_ProducesNoDiagnostics()
    {
        // Manual enumeration: GetAsyncEnumerator(token) (token flowed), await using disposal, a
        // MoveNextAsync loop with a per-item check. No analyzer fires.
        var code = @"
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

internal sealed class ManualConsumer
{
    public async Task RunAsync(IAsyncEnumerable<int> source, CancellationToken cancellationToken)
    {
        await using var enumerator = source.GetAsyncEnumerator(cancellationToken);
        while (await enumerator.MoveNextAsync())
        {
            cancellationToken.ThrowIfCancellationRequested();
            await HandleAsync(enumerator.Current, cancellationToken);
        }
    }

    private Task HandleAsync(int item, CancellationToken cancellationToken) => Task.CompletedTask;
}";

        var test = new AllAnalyzersTest
        {
            TestCode = code,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };

        await test.RunAsync();
    }

    [Fact]
    public async Task IdiomaticWhenAllThenLinqAggregate_ProducesNoDiagnostics()
    {
        // Fan-out with WhenAll (token flowed per task), then LINQ aggregation over the materialized
        // results. WhenAll has no token overload (CC002 quiet). No analyzer fires.
        var code = @"
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

internal sealed class StatsService
{
    public async Task<int> TotalAsync(IEnumerable<int> ids, CancellationToken cancellationToken)
    {
        var results = await Task.WhenAll(ids.Select(id => SizeAsync(id, cancellationToken)));
        return results.Where(r => r > 0).Sum();
    }

    private Task<int> SizeAsync(int id, CancellationToken cancellationToken) => Task.FromResult(id);
}";

        var test = new AllAnalyzersTest
        {
            TestCode = code,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };

        await test.RunAsync();
    }

    [Fact]
    public async Task IdiomaticAwaitForeachWithEarlyBreak_ProducesNoDiagnostics()
    {
        // await foreach with WithCancellation(token), a per-item check, and an early break after a cap.
        // No analyzer fires.
        var code = @"
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

internal sealed class TopNCollector
{
    public async Task<List<int>> TakeAsync(IAsyncEnumerable<int> source, int max, CancellationToken cancellationToken)
    {
        var result = new List<int>();
        await foreach (var item in source.WithCancellation(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            result.Add(item);
            if (result.Count >= max)
                break;
        }
        return result;
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
    public async Task IdiomaticGenericAsyncRepository_ProducesNoDiagnostics()
    {
        // A generic async method forwarding the token to a generic store. No analyzer fires (the token
        // is used; CC002 sees the token already passed).
        var code = @"
using System.Threading;
using System.Threading.Tasks;

internal interface IStore
{
    Task<T> LoadAsync<T>(int id, CancellationToken cancellationToken);
}

internal sealed class Repository
{
    private readonly IStore _store;

    public Repository(IStore store) => _store = store;

    public async Task<T> GetAsync<T>(int id, CancellationToken cancellationToken)
    {
        var entity = await _store.LoadAsync<T>(id, cancellationToken);
        return entity;
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
    public async Task IdiomaticConfiguredValueTaskAwait_ProducesNoDiagnostics()
    {
        // Awaiting a ValueTask<T> with ConfigureAwait(false), token flowed into the source. No analyzer
        // fires.
        var code = @"
using System.Threading;
using System.Threading.Tasks;

internal interface ISource
{
    ValueTask<int> ReadAsync(CancellationToken cancellationToken);
}

internal sealed class Reader
{
    private readonly ISource _source;

    public Reader(ISource source) => _source = source;

    public async ValueTask<int> GetAsync(CancellationToken cancellationToken)
        => await _source.ReadAsync(cancellationToken).ConfigureAwait(false);
}";

        var test = new AllAnalyzersTest
        {
            TestCode = code,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };

        await test.RunAsync();
    }

    [Fact]
    public async Task IdiomaticAsyncTemplateMethod_ProducesNoDiagnostics()
    {
        // An async template-method base: a public RunAsync threads the token through virtual/abstract
        // steps. No analyzer fires (overrides/abstracts are externally-controlled signatures).
        var code = @"
using System.Threading;
using System.Threading.Tasks;

internal abstract class ProcessorBase
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await BeforeAsync(cancellationToken);
        await StepAsync(cancellationToken);
    }

    protected virtual Task BeforeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    protected abstract Task StepAsync(CancellationToken cancellationToken);
}

internal sealed class ConcreteProcessor : ProcessorBase
{
    protected override async Task StepAsync(CancellationToken cancellationToken)
        => await Task.Delay(10, cancellationToken);
}";

        var test = new AllAnalyzersTest
        {
            TestCode = code,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };

        await test.RunAsync();
    }

    [Fact]
    public async Task IdiomaticAsyncIteratorWithFinallyCleanup_ProducesNoDiagnostics()
    {
        // An async iterator that opens a resource, yields token-flowing results, and disposes in
        // finally. [EnumeratorCancellation] (CC011), loop check (CC009), token-flowing work. No analyzer
        // fires.
        var code = @"
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

internal interface ICursor : IAsyncDisposable
{
    Task<int> NextAsync(CancellationToken cancellationToken);
}

internal sealed class CursorReader
{
    private readonly Func<CancellationToken, ValueTask<ICursor>> _open;

    public CursorReader(Func<CancellationToken, ValueTask<ICursor>> open) => _open = open;

    public async IAsyncEnumerable<int> ReadAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var cursor = await _open(cancellationToken);
        try
        {
            for (int i = 0; i < 3; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return await cursor.NextAsync(cancellationToken);
            }
        }
        finally
        {
            await cursor.DisposeAsync();
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
    public async Task IdiomaticParallelForEachAsyncWithOptions_ProducesNoDiagnostics()
    {
        // Parallel.ForEachAsync with a ParallelOptions carrying the token (and MaxDegreeOfParallelism);
        // the async body converts to a Func<...ValueTask> (not Action), so CC024 stays quiet. No
        // analyzer fires.
        var code = @"
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

internal sealed class ParallelImporter
{
    public async Task RunAsync(IEnumerable<int> items, CancellationToken cancellationToken)
    {
        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = 4,
            CancellationToken = cancellationToken,
        };

        await Parallel.ForEachAsync(items, options, async (item, token) =>
        {
            await ProcessAsync(item, token);
        });
    }

    private ValueTask ProcessAsync(int item, CancellationToken cancellationToken) => ValueTask.CompletedTask;
}";

        var test = new AllAnalyzersTest
        {
            TestCode = code,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };

        await test.RunAsync();
    }

    [Fact]
    public async Task IdiomaticConditionalAwait_ProducesNoDiagnostics()
    {
        // A partially-async method: one branch returns synchronously, the other awaits token-flowing
        // work. The token is observed where async work happens. No analyzer fires.
        var code = @"
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

internal sealed class CacheAside
{
    private readonly Dictionary<int, string> _cache = new();

    public async Task<string> GetAsync(int key, CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(key, out var hit))
            return hit;

        var loaded = await LoadAsync(key, cancellationToken);
        _cache[key] = loaded;
        return loaded;
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
    public async Task IdiomaticDoWhilePollLoop_ProducesNoDiagnostics()
    {
        // A do/while poll-until-result loop: cancellation check in the body, token-flowing poll and
        // delay, and the condition also observes the token. No analyzer fires.
        var code = @"
using System;
using System.Threading;
using System.Threading.Tasks;

internal sealed class Poller
{
    public async Task<string> WaitForResultAsync(CancellationToken cancellationToken)
    {
        string? result;
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            result = await PollAsync(cancellationToken);
            if (result is null)
                await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
        }
        while (result is null && !cancellationToken.IsCancellationRequested);

        return result ?? string.Empty;
    }

    private Task<string?> PollAsync(CancellationToken cancellationToken) => Task.FromResult<string?>(""ok"");
}";

        var test = new AllAnalyzersTest
        {
            TestCode = code,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };

        await test.RunAsync();
    }

    [Fact]
    public async Task IdiomaticAsyncGeneratorPaging_ProducesNoDiagnostics()
    {
        // An async generator that pages a source and yields items: [EnumeratorCancellation] token
        // (CC011), checks in both loops (CC009), token-flowing fetch (CC002). No analyzer fires.
        var code = @"
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

internal sealed class PagingGenerator
{
    public async IAsyncEnumerable<int> StreamAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        int page = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var items = await FetchAsync(page++, cancellationToken);
            if (items.Count == 0)
                yield break;

            foreach (var item in items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return item;
            }
        }
    }

    private Task<IReadOnlyList<int>> FetchAsync(int page, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<int>>(new List<int>());
}";

        var test = new AllAnalyzersTest
        {
            TestCode = code,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };

        await test.RunAsync();
    }

    [Fact]
    public async Task IdiomaticCatchWithTokenFilter_ProducesNoDiagnostics()
    {
        // A broad catch guarded by a when-filter that lets cancellation propagate (rethrow on cancel)
        // is the correct shape; CC019 (which targets unfiltered broad catches) must not fire.
        var code = @"
using System;
using System.Threading;
using System.Threading.Tasks;

internal sealed class GuardedService
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await DoWorkAsync(cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            Log(ex);
        }
    }

    private Task DoWorkAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    private void Log(Exception ex) { }
}";

        var test = new AllAnalyzersTest
        {
            TestCode = code,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };

        await test.RunAsync();
    }

    [Fact]
    public async Task IdiomaticBackgroundTaskLifecycle_ProducesNoDiagnostics()
    {
        // A start/stop background-task lifecycle: a field CTS (not flagged by CC014), CancelAsync on
        // stop (CC022 satisfied), the stored task joined via WaitAsync(token), and a token-checked loop.
        // No analyzer fires.
        var code = @"
using System.Threading;
using System.Threading.Tasks;

internal sealed class BackgroundRunner
{
    private readonly CancellationTokenSource _cts = new CancellationTokenSource();
    private Task? _running;

    public void Start() => _running = RunLoopAsync(_cts.Token);

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _cts.CancelAsync();
        if (_running is not null)
            await _running.WaitAsync(cancellationToken);
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(1000, cancellationToken);
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
    public async Task IdiomaticPerIterationAwaitUsing_ProducesNoDiagnostics()
    {
        // Opening and disposing an IAsyncDisposable per loop iteration via await using, with a
        // cancellation check and token-flowing work. No analyzer fires.
        var code = @"
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

internal interface ISession : IAsyncDisposable
{
    Task ProcessAsync(CancellationToken cancellationToken);
}

internal sealed class PerItemRunner
{
    private readonly Func<int, CancellationToken, ValueTask<ISession>> _open;

    public PerItemRunner(Func<int, CancellationToken, ValueTask<ISession>> open) => _open = open;

    public async Task RunAsync(IEnumerable<int> ids, CancellationToken cancellationToken)
    {
        foreach (var id in ids)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var session = await _open(id, cancellationToken);
            await session.ProcessAsync(cancellationToken);
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
    public async Task IdiomaticWaitToReadDrainLoop_ProducesNoDiagnostics()
    {
        // The WaitToReadAsync/TryRead drain pattern: outer await WaitToReadAsync(token), inner TryRead
        // loop with a cancellation check and token-flowing handling. No analyzer fires.
        var code = @"
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

internal sealed class Drainer
{
    public async Task RunAsync(ChannelReader<int> reader, CancellationToken cancellationToken)
    {
        while (await reader.WaitToReadAsync(cancellationToken))
        {
            while (reader.TryRead(out var item))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await HandleAsync(item, cancellationToken);
            }
        }
    }

    private Task HandleAsync(int item, CancellationToken cancellationToken) => Task.CompletedTask;
}";

        var test = new AllAnalyzersTest
        {
            TestCode = code,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };

        await test.RunAsync();
    }

    [Fact]
    public async Task IdiomaticWaitUntilCancelled_ProducesNoDiagnostics()
    {
        // Awaiting an infinite Task.Delay with the token to block until cancellation, with OCE handled
        // gracefully. The token is flowed; no analyzer fires.
        var code = @"
using System.Threading;
using System.Threading.Tasks;

internal sealed class IdleHost
{
    public async Task WaitForShutdownAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
        catch (TaskCanceledException)
        {
            // expected on shutdown
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
    public async Task IdiomaticAwaitForeachOverTokenizedProducer_ProducesNoDiagnostics()
    {
        // await foreach over a producer call that already received the token; CC010 must not also ask
        // for .WithCancellation. A per-item check satisfies CC009. No analyzer fires.
        var code = @"
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

internal sealed class FeedReader
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await foreach (var item in GetItemsAsync(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await HandleAsync(item, cancellationToken);
        }
    }

    private async IAsyncEnumerable<int> GetItemsAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        for (int i = 0; i < 3; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return i;
        }
    }

    private Task HandleAsync(int item, CancellationToken cancellationToken) => Task.CompletedTask;
}";

        var test = new AllAnalyzersTest
        {
            TestCode = code,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };

        await test.RunAsync();
    }

    [Fact]
    public async Task IdiomaticSharedReadinessTask_ProducesNoDiagnostics()
    {
        // Awaiting a shared readiness Task field before doing token-flowing work. Awaiting a Task field
        // needs no token; the subsequent query flows it. No analyzer fires.
        var code = @"
using System.Threading;
using System.Threading.Tasks;

internal sealed class ReadyService
{
    private readonly Task _ready = Task.CompletedTask;

    public async Task<int> GetAsync(CancellationToken cancellationToken)
    {
        await _ready;
        return await QueryAsync(cancellationToken);
    }

    private Task<int> QueryAsync(CancellationToken cancellationToken) => Task.FromResult(0);
}";

        var test = new AllAnalyzersTest
        {
            TestCode = code,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };

        await test.RunAsync();
    }

    [Fact]
    public async Task IdiomaticHeterogeneousWhenAll_ProducesNoDiagnostics()
    {
        // Awaiting two differently-typed tasks together by starting both (token-flowing) and awaiting
        // each after a combined WhenAll. No analyzer fires.
        var code = @"
using System.Threading;
using System.Threading.Tasks;

internal sealed class CompositeLoader
{
    public async Task<(int, string)> LoadAsync(CancellationToken cancellationToken)
    {
        var countTask = CountAsync(cancellationToken);
        var nameTask = NameAsync(cancellationToken);
        await Task.WhenAll(countTask, nameTask);
        return (await countTask, await nameTask);
    }

    private Task<int> CountAsync(CancellationToken cancellationToken) => Task.FromResult(0);
    private Task<string> NameAsync(CancellationToken cancellationToken) => Task.FromResult(string.Empty);
}";

        var test = new AllAnalyzersTest
        {
            TestCode = code,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };

        await test.RunAsync();
    }

    [Fact]
    public async Task IdiomaticSwitchStatementWithAwaitedBranches_ProducesNoDiagnostics()
    {
        // A switch statement whose branches each await a token-flowing operation. No analyzer fires.
        var code = @"
using System.Threading;
using System.Threading.Tasks;

internal enum Command { Read, Write, Delete }

internal sealed class CommandHandler
{
    public async Task<int> HandleAsync(Command command, CancellationToken cancellationToken)
    {
        switch (command)
        {
            case Command.Read:
                return await ReadAsync(cancellationToken);
            case Command.Write:
                await WriteAsync(cancellationToken);
                return 0;
            default:
                await DeleteAsync(cancellationToken);
                return -1;
        }
    }

    private Task<int> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(0);
    private Task WriteAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    private Task DeleteAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}";

        var test = new AllAnalyzersTest
        {
            TestCode = code,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };

        await test.RunAsync();
    }

    [Fact]
    public async Task IdiomaticSpecificCatchWithAsyncCleanup_ProducesNoDiagnostics()
    {
        // try with token-flowing work, a specific (non-broad) catch, and async cleanup in finally.
        // CC019 targets broad catches only, so it must not fire. No analyzer fires.
        var code = @"
using System.IO;
using System.Threading;
using System.Threading.Tasks;

internal sealed class ResilientService
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await DoWorkAsync(cancellationToken);
        }
        catch (IOException)
        {
            await CompensateAsync(cancellationToken);
        }
        finally
        {
            await FlushLogsAsync(cancellationToken);
        }
    }

    private Task DoWorkAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    private Task CompensateAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    private Task FlushLogsAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}";

        var test = new AllAnalyzersTest
        {
            TestCode = code,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };

        await test.RunAsync();
    }

    [Fact]
    public async Task IdiomaticAsyncValidationAggregation_ProducesNoDiagnostics()
    {
        // Running multiple async validators, each flowing the token, and aggregating their results with
        // a per-validator cancellation check. No analyzer fires.
        var code = @"
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

internal interface IValidator
{
    Task<string?> ValidateAsync(CancellationToken cancellationToken);
}

internal sealed class ValidationRunner
{
    public async Task<IReadOnlyList<string>> RunAsync(IEnumerable<IValidator> validators, CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        foreach (var validator in validators)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var error = await validator.ValidateAsync(cancellationToken);
            if (error is not null)
                errors.Add(error);
        }
        return errors;
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
    public async Task IdiomaticSignalDrivenWorker_ProducesNoDiagnostics()
    {
        // A worker that waits on a SemaphoreSlim(0) signal each iteration, checks cancellation, and does
        // token-flowing work. No analyzer fires.
        var code = @"
using System.Threading;
using System.Threading.Tasks;

internal sealed class SignalWorker
{
    private readonly SemaphoreSlim _signal = new SemaphoreSlim(0);

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await _signal.WaitAsync(cancellationToken);
            await DrainAsync(cancellationToken);
        }
    }

    private Task DrainAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}";

        var test = new AllAnalyzersTest
        {
            TestCode = code,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };

        await test.RunAsync();
    }

    [Fact]
    public async Task IdiomaticLazyTaskWithWaitAsync_ProducesNoDiagnostics()
    {
        // A shared Lazy<Task<T>> awaited cancelably per caller via Task.WaitAsync(token). The token is
        // observed (CC016 satisfied) and the method has a token param (CC001 satisfied). No analyzer fires.
        var code = @"
using System;
using System.Threading;
using System.Threading.Tasks;

internal sealed class SharedInitializer
{
    private readonly Lazy<Task<string>> _shared = new(() => LoadAsync());

    public Task<string> GetAsync(CancellationToken cancellationToken)
        => _shared.Value.WaitAsync(cancellationToken);

    private static Task<string> LoadAsync() => Task.FromResult(string.Empty);
}";

        var test = new AllAnalyzersTest
        {
            TestCode = code,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };

        await test.RunAsync();
    }

    [Fact]
    public async Task IdiomaticBoundedConcurrencyFanOut_ProducesNoDiagnostics()
    {
        // Throttled fan-out: a SemaphoreSlim limits concurrency, each task awaits WaitAsync(token),
        // works, and Releases in finally; WhenAll-joined. No analyzer fires.
        var code = @"
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

internal sealed class ThrottledRunner
{
    public async Task RunAsync(IEnumerable<int> items, CancellationToken cancellationToken)
    {
        using var gate = new SemaphoreSlim(4);
        var tasks = items.Select(async item =>
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                await ProcessAsync(item, cancellationToken);
            }
            finally
            {
                gate.Release();
            }
        });

        await Task.WhenAll(tasks);
    }

    private Task ProcessAsync(int item, CancellationToken cancellationToken) => Task.CompletedTask;
}";

        var test = new AllAnalyzersTest
        {
            TestCode = code,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };

        await test.RunAsync();
    }

    [Fact]
    public async Task IdiomaticPeriodicTimerLoop_ProducesNoDiagnostics()
    {
        // A PeriodicTimer tick loop: WaitForNextTickAsync(token) as the condition, an explicit
        // cancellation check and token-flowing work in the body. No analyzer fires.
        var code = @"
using System;
using System.Threading;
using System.Threading.Tasks;

internal sealed class HeartbeatService
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await BeatAsync(cancellationToken);
        }
    }

    private Task BeatAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}";

        var test = new AllAnalyzersTest
        {
            TestCode = code,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };

        await test.RunAsync();
    }

    [Fact]
    public async Task IdiomaticHttpStreamingDownload_ProducesNoDiagnostics()
    {
        // Streaming an HTTP response: SendAsync with HttpCompletionOption.ResponseHeadersRead and the
        // token, then ReadAsStreamAsync(token). CC004/CC002 (token passed) stay quiet.
        var code = @"
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

internal sealed class DownloadService
{
    private readonly HttpClient _client = new HttpClient();

    public async Task<Stream> OpenAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStreamAsync(cancellationToken);
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
    public async Task IdiomaticBufferedStreamCopyLoop_ProducesNoDiagnostics()
    {
        // A manual buffered copy loop: cancellation check per chunk, ReadAsync/WriteAsync both flow the
        // token. No analyzer fires.
        var code = @"
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

internal sealed class CopyService
{
    public async Task CopyAsync(Stream input, Stream output, CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        int read;
        while ((read = await input.ReadAsync(buffer.AsMemory(), cancellationToken)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        await output.FlushAsync(cancellationToken);
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
    public async Task IdiomaticSyncBodiedTaskMethodWithToken_ProducesNoDiagnostics()
    {
        // A Task-returning method with no await that accepts a token and forwards it to a downstream
        // Task-returning call. With no await in the body, CC016 (unused-token) does not apply, and the
        // token is forwarded anyway. No analyzer fires.
        var code = @"
using System.Threading;
using System.Threading.Tasks;

internal sealed class ForwardingService
{
    private readonly IDownstream _downstream;

    public ForwardingService(IDownstream downstream) => _downstream = downstream;

    public Task<int> GetAsync(int id, CancellationToken cancellationToken)
        => _downstream.QueryAsync(id, cancellationToken);
}

internal interface IDownstream
{
    Task<int> QueryAsync(int id, CancellationToken cancellationToken);
}";

        var test = new AllAnalyzersTest
        {
            TestCode = code,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };

        await test.RunAsync();
    }

    [Fact]
    public async Task IdiomaticConfiguredAsyncStreamConsumer_ProducesNoDiagnostics()
    {
        // Library-style async-stream consumption: WithCancellation(token) then ConfigureAwait(false),
        // with a per-item cancellation check. CC010/CC009 and the rest stay quiet.
        var code = @"
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

internal sealed class ConfiguredConsumer
{
    public async Task ConsumeAsync(IAsyncEnumerable<int> source, CancellationToken cancellationToken)
    {
        await foreach (var item in source.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await HandleAsync(item, cancellationToken).ConfigureAwait(false);
        }
    }

    private Task HandleAsync(int item, CancellationToken cancellationToken) => Task.CompletedTask;
}";

        var test = new AllAnalyzersTest
        {
            TestCode = code,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };

        await test.RunAsync();
    }

    [Fact]
    public async Task IdiomaticSyncDisposableUsingInAsync_ProducesNoDiagnostics()
    {
        // A `using` over a purely-synchronous IDisposable inside async code is correct (it is NOT
        // IAsyncDisposable), so CC025 must not suggest await using. No analyzer fires.
        var code = @"
using System;
using System.Threading;
using System.Threading.Tasks;

internal sealed class Lease : IDisposable
{
    public void Dispose() { }
}

internal sealed class LeaseService
{
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        using var lease = new Lease();
        return await ComputeAsync(cancellationToken);
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
    public async Task IdiomaticAsyncLambdasAsFuncTask_ProducesNoDiagnostics()
    {
        // Async lambdas materialized as Func<Task> and awaited via WhenAll(Select(f => f())) is correct
        // usage — CC024 (async-lambda-to-Action) must not fire because these convert to Func<Task>.
        var code = @"
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

internal sealed class StepRunner
{
    public async Task RunAllAsync(CancellationToken cancellationToken)
    {
        var steps = new List<Func<Task>>
        {
            async () => await StepAsync(1, cancellationToken),
            async () => await StepAsync(2, cancellationToken),
        };

        await Task.WhenAll(steps.Select(step => step()));
    }

    private Task StepAsync(int id, CancellationToken cancellationToken) => Task.CompletedTask;
}";

        var test = new AllAnalyzersTest
        {
            TestCode = code,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };

        await test.RunAsync();
    }

    [Fact]
    public async Task IdiomaticSequentialWorkflow_ProducesNoDiagnostics()
    {
        // A multi-step async workflow threading the token through each step (validate, reserve, charge,
        // confirm). No analyzer fires.
        var code = @"
using System.Threading;
using System.Threading.Tasks;

internal sealed class OrderWorkflow
{
    public async Task<bool> PlaceAsync(int orderId, CancellationToken cancellationToken)
    {
        await ValidateAsync(orderId, cancellationToken);
        var reservation = await ReserveAsync(orderId, cancellationToken);
        await ChargeAsync(reservation, cancellationToken);
        return await ConfirmAsync(orderId, cancellationToken);
    }

    private Task ValidateAsync(int id, CancellationToken cancellationToken) => Task.CompletedTask;
    private Task<int> ReserveAsync(int id, CancellationToken cancellationToken) => Task.FromResult(id);
    private Task ChargeAsync(int reservation, CancellationToken cancellationToken) => Task.CompletedTask;
    private Task<bool> ConfirmAsync(int id, CancellationToken cancellationToken) => Task.FromResult(true);
}";

        var test = new AllAnalyzersTest
        {
            TestCode = code,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };

        await test.RunAsync();
    }

    [Fact]
    public async Task IdiomaticGracefulCancellationHandling_ProducesNoDiagnostics()
    {
        // Catching OperationCanceledException specifically to run cleanup and then rethrow is the
        // correct graceful-shutdown shape — CC019 (which targets broad swallowing catches) must not fire.
        var code = @"
using System;
using System.Threading;
using System.Threading.Tasks;

internal sealed class GracefulService
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await DoWorkAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            await CleanupAsync();
            throw;
        }
    }

    private Task DoWorkAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    private Task CleanupAsync() => Task.CompletedTask;
}";

        var test = new AllAnalyzersTest
        {
            TestCode = code,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };

        await test.RunAsync();
    }

    [Fact]
    public async Task IdiomaticAsyncEnumerableTransformPipeline_ProducesNoDiagnostics()
    {
        // An async-iterator that transforms another async stream: [EnumeratorCancellation] on its token
        // (CC011), .WithCancellation(token) on the source (CC010), and a per-item check (CC009). All
        // quiet.
        var code = @"
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

internal sealed class TransformPipeline
{
    public async IAsyncEnumerable<int> DoubleAsync(
        IAsyncEnumerable<int> source,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var value in source.WithCancellation(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return value * 2;
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
    public async Task IdiomaticChannelProducerConsumerPair_ProducesNoDiagnostics()
    {
        // A producer and a consumer running concurrently over a bounded channel, both flowing the token
        // and Task.WhenAll-joined. No analyzer fires.
        var code = @"
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

internal sealed class Pipeline
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var channel = Channel.CreateBounded<int>(10);
        var producer = ProduceAsync(channel.Writer, cancellationToken);
        var consumer = ConsumeAsync(channel.Reader, cancellationToken);
        await Task.WhenAll(producer, consumer);
    }

    private async Task ProduceAsync(ChannelWriter<int> writer, CancellationToken cancellationToken)
    {
        for (int i = 0; i < 100; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await writer.WriteAsync(i, cancellationToken);
        }
        writer.Complete();
    }

    private async Task ConsumeAsync(ChannelReader<int> reader, CancellationToken cancellationToken)
    {
        await foreach (var item in reader.ReadAllAsync(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await HandleAsync(item, cancellationToken);
        }
    }

    private Task HandleAsync(int item, CancellationToken cancellationToken) => Task.CompletedTask;
}";

        var test = new AllAnalyzersTest
        {
            TestCode = code,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };

        await test.RunAsync();
    }

    [Fact]
    public async Task IdiomaticManualAsyncDisposalInFinally_ProducesNoDiagnostics()
    {
        // Manual try/finally with await resource.DisposeAsync() is a valid alternative to await using;
        // CC025 (which targets `using` forms) and the rest stay quiet.
        var code = @"
using System;
using System.Threading;
using System.Threading.Tasks;

internal interface IConnection : IAsyncDisposable
{
    Task<int> ReadAsync(CancellationToken cancellationToken);
}

internal sealed class ManualDisposeService
{
    private readonly Func<CancellationToken, ValueTask<IConnection>> _connect;

    public ManualDisposeService(Func<CancellationToken, ValueTask<IConnection>> connect) => _connect = connect;

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var connection = await _connect(cancellationToken);
        try
        {
            return await connection.ReadAsync(cancellationToken);
        }
        finally
        {
            await connection.DisposeAsync();
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
    public async Task IdiomaticServerStreamingWriterLoop_ProducesNoDiagnostics()
    {
        // A server-streaming writer loop: cancellation check per iteration, token-flowing produce and
        // write. No analyzer fires.
        var code = @"
using System.Threading;
using System.Threading.Tasks;

internal interface IStreamWriter<T>
{
    Task WriteAsync(T item, CancellationToken cancellationToken);
}

internal sealed class Streamer
{
    public async Task StreamAsync(IStreamWriter<int> writer, int count, CancellationToken cancellationToken)
    {
        for (int i = 0; i < count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var value = await ProduceAsync(i, cancellationToken);
            await writer.WriteAsync(value, cancellationToken);
        }
    }

    private Task<int> ProduceAsync(int i, CancellationToken cancellationToken) => Task.FromResult(i);
}";

        var test = new AllAnalyzersTest
        {
            TestCode = code,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };

        await test.RunAsync();
    }

    [Fact]
    public async Task IdiomaticPaginationBatchLoop_ProducesNoDiagnostics()
    {
        // Page through a data source until a short page is returned, checking cancellation each page and
        // flowing the token into the fetch and per-item handling. No analyzer fires.
        var code = @"
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

internal sealed class BatchProcessor
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        const int pageSize = 100;
        int page = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var items = await FetchPageAsync(page, pageSize, cancellationToken);
            foreach (var item in items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await HandleAsync(item, cancellationToken);
            }

            if (items.Count < pageSize)
                break;

            page++;
        }
    }

    private Task<IReadOnlyList<int>> FetchPageAsync(int page, int size, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<int>>(new List<int>());

    private Task HandleAsync(int item, CancellationToken cancellationToken) => Task.CompletedTask;
}";

        var test = new AllAnalyzersTest
        {
            TestCode = code,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };

        await test.RunAsync();
    }

    [Fact]
    public async Task IdiomaticEventToTaskBridge_ProducesNoDiagnostics()
    {
        // Bridging an event to a Task: subscribe, register cancellation on the TCS, await, unsubscribe
        // in finally. No analyzer fires.
        var code = @"
using System;
using System.Threading;
using System.Threading.Tasks;

internal sealed class Device
{
    public event EventHandler? Ready;
    public void Raise() => Ready?.Invoke(this, EventArgs.Empty);
}

internal sealed class DeviceWaiter
{
    public async Task WaitForReadyAsync(Device device, CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource();
        void Handler(object? sender, EventArgs e) => tcs.TrySetResult();
        device.Ready += Handler;
        try
        {
            await using (cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken)))
            {
                await tcs.Task;
            }
        }
        finally
        {
            device.Ready -= Handler;
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
    public async Task IdiomaticTwoTokenLinkedSource_ProducesNoDiagnostics()
    {
        // Linking two external tokens with a using-declared CreateLinkedTokenSource and flowing the
        // combined token. CC014 (using var disposal) and the rest stay quiet.
        var code = @"
using System.Threading;
using System.Threading.Tasks;

internal sealed class CombinedService
{
    private readonly CancellationTokenSource _shutdown = new CancellationTokenSource();

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdown.Token);
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
    public async Task IdiomaticAsyncStreamAggregation_ProducesNoDiagnostics()
    {
        // Consuming an injected IAsyncEnumerable with .WithCancellation(token) and aggregating. CC010
        // (token flowed) and the rest stay quiet.
        var code = @"
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

internal sealed class AggregationService
{
    public async Task<int> SumAsync(IAsyncEnumerable<int> source, CancellationToken cancellationToken)
    {
        int total = 0;
        await foreach (var value in source.WithCancellation(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            total += value;
        }
        return total;
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
