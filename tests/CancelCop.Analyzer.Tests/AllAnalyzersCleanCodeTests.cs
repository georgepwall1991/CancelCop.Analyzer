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
}
