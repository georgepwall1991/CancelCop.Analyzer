<p align="center">
  <img src="https://raw.githubusercontent.com/georgepwall1991/CancelCop.Analyzer/main/assets/cancelcop-icon.png" width="96" height="96" alt="CancelCop.Analyzer icon — Roslyn analyzer for CancellationToken and async/await in C#/.NET">
</p>

# CancelCop.Analyzer

**Compile-time CancellationToken and async/await Roslyn analyzer for C#/.NET** — catches missing cancellation propagation, ignored ASP.NET Core `RequestAborted`, EF Core and HttpClient token gaps, sync-over-async deadlocks, blocking I/O, `async void`, and resource-lifetime bugs so they fail in the editor and CI, not only at runtime.

[![NuGet](https://img.shields.io/nuget/v/CancelCop.Analyzer.svg)](https://www.nuget.org/packages/CancelCop.Analyzer/)
[![NuGet downloads](https://img.shields.io/nuget/dt/CancelCop.Analyzer.svg)](https://www.nuget.org/packages/CancelCop.Analyzer/)
[![CI](https://github.com/georgepwall1991/CancelCop.Analyzer/actions/workflows/ci.yml/badge.svg)](https://github.com/georgepwall1991/CancelCop.Analyzer/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://github.com/georgepwall1991/CancelCop.Analyzer/blob/main/LICENSE)

Stop shipping async that cannot cancel.

## The problem

`CancellationToken` and correct async/await usage are essential for responsive .NET apps, but cancellation bugs hide across API boundaries. A public method without a token, an `HttpClient` or EF Core call that ignores the caller's token, a controller that never sees `RequestAborted`, a timeout CTS that drops the parent token, or a `.Result` / `Thread.Sleep` inside async code often compiles cleanly and only fails under load, shutdown, or client disconnect.

Runtime review and occasional CA rules miss what a dedicated cancellation-and-async analyzer can prove from your call sites.

## What it catches

CancelCop reports high-signal async and cancellation failures early (34 diagnostics, many with code fixes):

- missing `CancellationToken` on public async methods and framework handlers (controllers, Minimal APIs, MediatR, SignalR, `BackgroundService`)
- tokens accepted but not propagated to `HttpClient`, EF Core, `Task.Delay`, and other cancellable APIs
- loops and async streams that ignore cancellation (`ThrowIfCancellationRequested`, `.WithCancellation`, `[EnumeratorCancellation]`, iterators declared with no token at all)
- timeout `CancellationTokenSource` that silently drops a parent token (`CreateLinkedTokenSource` + `CancelAfter`)
- sync-over-async and blocking I/O (`.Result` / `.Wait()`, `Thread.Sleep`, `SemaphoreSlim.Wait()`, blocking `File` / stream APIs, `Process.WaitForExit()`, blocking sync primitives)
- `async void`, unawaited fire-and-forget calls, swallowed `OperationCanceledException`, and resource-lifetime bugs (undisposed CTS locals and fields, premature `using` disposal)

When the analyzer cannot prove a problem statically, it **stays quiet**. High-signal feedback, not noisy guesses.

## Install

```xml
<PackageReference Include="CancelCop.Analyzer" Version="1.35.0">
  <PrivateAssets>all</PrivateAssets>
  <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
</PackageReference>
```

Or:

```bash
dotnet add package CancelCop.Analyzer
```

```powershell
Install-Package CancelCop.Analyzer -Version 1.35.0
```

**No runtime dependency** is added to your app. CancelCop runs as a Roslyn analyzer during build and in supported IDEs. Use `PrivateAssets="all"` so the analyzer stays a development dependency for libraries.

## See it work

Product-flow diagrams from the real sample build (CC001–CC029 diagnostic text):

### 1. Build / IDE diagnostics (CancellationToken and async)

![CancelCop Roslyn analyzer warnings for CancellationToken and async — CC001 missing token, CC002 propagation, CC003 EF Core, CC015 sync-over-async](https://raw.githubusercontent.com/georgepwall1991/CancelCop.Analyzer/main/assets/flow-ide-diagnostics.svg)

### 2. Before / after code fix (HttpClient token propagation)

![Before and after: HttpClient GetStringAsync missing CancellationToken fixed with CC002/CC004 code fix](https://raw.githubusercontent.com/georgepwall1991/CancelCop.Analyzer/main/assets/flow-before-after-fix.svg)

### 3. Product loop — analyzer, code fixes, and CI

![CancelCop product loop: Roslyn analyzer build diagnostics, one-click code fixes, and CI gate for CancellationToken and async/await](https://raw.githubusercontent.com/georgepwall1991/CancelCop.Analyzer/main/assets/flow-analyzer-ci-loop.svg)

## 30-second path

1. Reference the package with `PrivateAssets="all"`.
2. Build in the IDE or with `dotnet build` so analyzers run.
3. Fix any `CC00x` warnings (most have one-click code fixes).
4. Optionally promote critical rules to errors in `.editorconfig` when the codebase is clean:

```ini
[*.cs]
dotnet_diagnostic.CC002.severity = error
dotnet_diagnostic.CC015.severity = error
```

5. Keep the sample project handy for rule demos:

```bash
dotnet build samples/CancelCop.Sample
```

## Feature snapshot

| Area | What CancelCop does |
|------|---------------------|
| Token presence | Flags public/protected async methods and framework handlers missing `CancellationToken`. |
| Propagation | Requires tokens to flow into HttpClient, EF Core, and other cancellable overloads when a token is in scope. |
| ASP.NET Core | Controllers, Minimal APIs, SignalR hubs, middleware via `HttpContext.RequestAborted`. |
| Hosted services | `BackgroundService.ExecuteAsync` must observe the stopping token. |
| gRPC / MediatR | Observes `ServerCallContext.CancellationToken` and handler signatures. |
| Async streams | `await foreach` + `.WithCancellation`; iterators need `[EnumeratorCancellation]`. |
| Timeout CTS | Links parent tokens with `CreateLinkedTokenSource` + `CancelAfter` (CC029). |
| Sync-over-async | `.Result` / `.Wait()` / `GetAwaiter().GetResult()`, `Thread.Sleep`, `SemaphoreSlim.Wait()`, blocking file I/O. |
| Async hygiene | `async void`, void-returning async lambdas, swallowed cancellation, `await using`, CTS disposal. |
| Code fixes | Most rules offer compilable one-click fixes; Fix All is supported where safe. |

## Compatibility

- Analyzer assemblies target **.NET Standard 2.0** and compile against **Roslyn 4.8** (Visual Studio 2022 17.8+ / .NET SDK 8+ hosts)
- Consumer projects can target any framework supported by a compatible compiler host
- **ASP.NET Core**, **EF Core**, **HttpClient**, **gRPC**, **SignalR**, **MediatR**, **BackgroundService**
- **`IAsyncEnumerable<T>`**, **ValueTask** / **`ValueTask<T>`**

## Analyzer Rules

| Rule | Description | Severity | Code Fix |
|------|-------------|----------|----------|
| **CC001** | Public async methods must have CancellationToken parameter | Warning | ✅ |
| **CC002** | CancellationToken must be propagated to async calls | Warning | ✅ |
| **CC003** | EF Core queries must pass CancellationToken | Warning | ✅ |
| **CC004** | HttpClient methods must pass CancellationToken | Warning | ✅ |
| **CC005A** | MediatR handlers must accept CancellationToken | Warning | ✅ |
| **CC005B** | Controller actions must accept CancellationToken | Warning | ✅ |
| **CC005C** | Minimal API endpoints must accept CancellationToken | Warning | ✅ |
| **CC006** | CancellationToken should be the last parameter | Info | ❌ |
| **CC009** | Loops should check for cancellation | Warning | ✅ |
| **CC010** | `await foreach` should flow a CancellationToken via `.WithCancellation` | Warning | ✅ |
| **CC011** | Async-iterator CancellationToken should be `[EnumeratorCancellation]` | Warning | ✅ |
| **CC012** | Avoid passing `CancellationToken.None`/`default` when a token is in scope | Info | ✅ |
| **CC013** | Avoid `Thread.Sleep` in async code; use `await Task.Delay` | Warning | ✅ |
| **CC014** | `CancellationTokenSource` should be disposed | Warning | ✅ |
| **CC015** | Avoid blocking on async code (`.Result`/`.Wait()`/`.GetAwaiter().GetResult()`) | Warning | ✅ |
| **CC016** | `CancellationToken` parameter is accepted but never used | Info | ❌ |
| **CC017** | `BackgroundService.ExecuteAsync` should observe its stopping token | Warning | ❌ |
| **CC018** | SignalR hub methods should accept a `CancellationToken` | Warning | ✅ |
| **CC019** | Broad `catch` swallows `OperationCanceledException` | Info | ✅ |
| **CC020** | gRPC method should observe `ServerCallContext.CancellationToken` | Warning | ❌ |
| **CC021** | Method should observe `HttpContext.RequestAborted` | Info | ❌ |
| **CC022** | Prefer `await CancelAsync()` over `Cancel()` in async code | Info | ✅ |
| **CC023** | Avoid `async void` (non-event-handler) | Warning | ✅ |
| **CC024** | Avoid `async` lambdas converted to `Action` | Warning | ❌ |
| **CC025** | Prefer `await using` for `IAsyncDisposable` | Info | ✅ |
| **CC026** | Avoid `SemaphoreSlim.Wait()` in async code; use `await WaitAsync()` | Warning | ✅ |
| **CC027** | Returned task uses a disposed `using` resource | Warning | ❌ |
| **CC028** | Avoid blocking `System.IO` calls (`File`, `StreamReader`, `StreamWriter`, `Stream`) in async code; use the async counterpart | Warning | ✅ |
| **CC029** | Timeout `CancellationTokenSource` should link the in-scope token (`CreateLinkedTokenSource` + `CancelAfter`) | Warning | ✅ |
| **CC030** | Avoid blocking `Process.WaitForExit()` in async code; use `await WaitForExitAsync(token)` | Warning | ✅ |
| **CC031** | Avoid blocking synchronization primitives (`ManualResetEventSlim.Wait`, `WaitHandle.WaitOne`, `Monitor.Wait`, `Thread.Join`) in async code | Warning | ❌ |
| **CC032** | Async call discarded in non-async code, where the compiler's CS4014 does not fire | Warning | ❌ |
| **CC033** | `CancellationTokenSource` field created by the type and never disposed | Warning | ❌ |
| **CC034** | Public `IAsyncEnumerable<T>` iterator declared with no `CancellationToken` parameter | Warning | ✅ |

## Quick Examples

### CC001: Missing CancellationToken Parameter

```csharp
// ❌ Warning CC001
public async Task ProcessDataAsync()
{
    await Task.Delay(100);
}

// ✅ Fixed
public async Task ProcessDataAsync(CancellationToken cancellationToken = default)
{
    await Task.Delay(100, cancellationToken);
}
```

### CC002: Token Not Propagated

```csharp
// ❌ Warning CC002 - token available but not passed
public async Task ProcessAsync(CancellationToken cancellationToken)
{
    await Task.Delay(100);           // Should pass cancellationToken
    await DoWorkAsync();              // Should pass cancellationToken
}

// ✅ Fixed
public async Task ProcessAsync(CancellationToken cancellationToken)
{
    await Task.Delay(100, cancellationToken);
    await DoWorkAsync(cancellationToken);
}
```

### CC003: EF Core Without Token

```csharp
// ❌ Warning CC003
public async Task<User?> GetUserAsync(int id, CancellationToken cancellationToken)
{
    return await _context.Users.FirstOrDefaultAsync(u => u.Id == id);
}

// ✅ Fixed
public async Task<User?> GetUserAsync(int id, CancellationToken cancellationToken)
{
    return await _context.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
}
```

### CC004: HttpClient Without Token

```csharp
// ❌ Warning CC004
public async Task<string> FetchDataAsync(CancellationToken cancellationToken)
{
    return await _httpClient.GetStringAsync("https://api.example.com");
}

// ✅ Fixed
public async Task<string> FetchDataAsync(CancellationToken cancellationToken)
{
    return await _httpClient.GetStringAsync("https://api.example.com", cancellationToken);
}
```

### CC005B: Controller Action Without Token

```csharp
// ❌ Warning CC005B
[HttpGet]
public async Task<IActionResult> GetUsers()
{
    var users = await _service.GetUsersAsync();
    return Ok(users);
}

// ✅ Fixed - ASP.NET Core injects the token automatically
[HttpGet]
public async Task<IActionResult> GetUsers(CancellationToken cancellationToken)
{
    var users = await _service.GetUsersAsync(cancellationToken);
    return Ok(users);
}
```

### CC005C: Minimal API Without Token

```csharp
// ❌ Warning CC005C
app.MapGet("/users", async () => await GetUsersAsync());

// ✅ Fixed
app.MapGet("/users", async (CancellationToken ct) => await GetUsersAsync(ct));

// ❌ Warning CC005C — method-group handlers are analysed too (v1.4.4);
// the fix adds `CancellationToken cancellationToken = default` to GetUsersAsync itself
app.MapGet("/users", GetUsersAsync);
```

### CC006: Token Not Last Parameter

```csharp
// ℹ️ Info CC006 - convention suggests token should be last
public async Task ProcessAsync(CancellationToken cancellationToken, string name)
{
}

// ✅ Better - follows .NET conventions
public async Task ProcessAsync(string name, CancellationToken cancellationToken)
{
}
```

### CC009: Loop Without Cancellation Check

```csharp
// ❌ Warning CC009 - loop doesn't check for cancellation
public async Task ProcessItemsAsync(List<Item> items, CancellationToken cancellationToken)
{
    foreach (var item in items)  // Could process 1M items without checking!
    {
        await ProcessAsync(item);
    }
}

// ✅ Fixed
public async Task ProcessItemsAsync(List<Item> items, CancellationToken cancellationToken)
{
    foreach (var item in items)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await ProcessAsync(item);
    }
}
```

### CC010: `await foreach` Without a Token

```csharp
// ❌ Warning CC010 - the async stream never receives the token
await foreach (var item in source)
{
}

// ✅ Fixed - .WithCancellation flows the token to the producer
await foreach (var item in source.WithCancellation(cancellationToken))
{
}
```

### CC011: Async Iterator Token Without `[EnumeratorCancellation]`

```csharp
// ❌ Warning CC011 - WithCancellation can't deliver a token to this parameter
public async IAsyncEnumerable<int> ReadAsync(CancellationToken token)
{
    yield return await NextAsync(token);
}

// ✅ Fixed
public async IAsyncEnumerable<int> ReadAsync([EnumeratorCancellation] CancellationToken token)
{
    yield return await NextAsync(token);
}
```

### CC012: Explicit `CancellationToken.None` When a Token Is in Scope

```csharp
// ℹ️ Info CC012 - discards cancellation even though a token is available
public async Task RunAsync(CancellationToken cancellationToken)
    => await DoAsync(CancellationToken.None);

// ✅ Fixed
public async Task RunAsync(CancellationToken cancellationToken)
    => await DoAsync(cancellationToken);
```

### CC013: `Thread.Sleep` in Async Code

```csharp
// ❌ Warning CC013 - blocks the thread and ignores cancellation
public async Task RunAsync(CancellationToken ct)
{
    Thread.Sleep(1000);
}

// ✅ Fixed
public async Task RunAsync(CancellationToken ct)
{
    await Task.Delay(1000, ct);
}
```

### CC014: Undisposed `CancellationTokenSource`

```csharp
// ❌ Warning CC014 - the source's timer/handle leak
var cts = new CancellationTokenSource();
await DoAsync(cts.Token);

// ✅ Fixed
using var cts = new CancellationTokenSource();
await DoAsync(cts.Token);
```

### CC015: Blocking on Async Code

```csharp
// ❌ Warning CC015 - can deadlock and discards cancellation
public async Task<int> RunAsync()
    => GetValueAsync().Result;

// ✅ Fixed
public async Task<int> RunAsync()
    => await GetValueAsync();
```

### CC016: Unused `CancellationToken` Parameter

```csharp
// ℹ️ Info CC016 - accepts a token but never observes it
public async Task SaveAsync(string text, CancellationToken cancellationToken)
{
    await File.WriteAllTextAsync("f.txt", text);   // token ignored
}

// ✅ Fixed
public async Task SaveAsync(string text, CancellationToken cancellationToken)
{
    await File.WriteAllTextAsync("f.txt", text, cancellationToken);
}
```

### CC017: `BackgroundService` Ignoring Its Stopping Token

```csharp
// ❌ Warning CC017 - never stops on shutdown
protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    while (true) { await DoWorkAsync(); }
}

// ✅ Fixed
protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    while (!stoppingToken.IsCancellationRequested) { await DoWorkAsync(stoppingToken); }
}
```

### CC018: SignalR Hub Method Without a Token

```csharp
// ❌ Warning CC018 - keeps running after the client disconnects
public async Task Broadcast(string message)
    => await Clients.All.SendAsync("recv", message);

// ✅ Fixed
public async Task Broadcast(string message, CancellationToken cancellationToken)
    => await Clients.All.SendAsync("recv", message, cancellationToken);
```

### CC019: Broad `catch` Swallowing Cancellation

```csharp
// ℹ️ Info CC019 - also swallows OperationCanceledException
try { await DoAsync(token); }
catch (Exception ex) { Log(ex); }

// ✅ Fixed - let cancellation propagate
try { await DoAsync(token); }
catch (Exception ex) when (ex is not OperationCanceledException) { Log(ex); }
```

### CC020: gRPC Method Ignoring `ServerCallContext.CancellationToken`

```csharp
// ❌ Warning CC020 - keeps running after the client cancels
public override async Task<Reply> Handle(Request request, ServerCallContext context)
    => new Reply { Value = await _db.LoadAsync() };

// ✅ Fixed
public override async Task<Reply> Handle(Request request, ServerCallContext context)
    => new Reply { Value = await _db.LoadAsync(context.CancellationToken) };
```

### CC021: Method Ignoring `HttpContext.RequestAborted`

```csharp
// ℹ️ Info CC021 - work continues after the client disconnects
public async Task InvokeAsync(HttpContext context)
    => await _service.DoWorkAsync();

// ✅ Fixed
public async Task InvokeAsync(HttpContext context)
    => await _service.DoWorkAsync(context.RequestAborted);
```

### CC022: Prefer `CancelAsync()` Over `Cancel()`

```csharp
// ℹ️ Info CC022 - runs callbacks synchronously on this thread
public async Task StopAsync(CancellationTokenSource cts)
    => cts.Cancel();

// ✅ Fixed
public async Task StopAsync(CancellationTokenSource cts)
    => await cts.CancelAsync();
```

### CC023: `async void`

```csharp
// ❌ Warning CC023 - cannot be awaited; exceptions crash the process
public async void ProcessAsync() => await DoWorkAsync();

// ✅ Fixed
public async Task ProcessAsync() => await DoWorkAsync();
```

### CC024: `async` Lambda Converted to `Action`

```csharp
// ❌ Warning CC024 - the async body runs fire-and-forget (async void)
Parallel.ForEach(items, async item => await ProcessAsync(item));

// ✅ Fixed - use an API that awaits, e.g.
await Parallel.ForEachAsync(items, async (item, ct) => await ProcessAsync(item, ct));
```

### CC025: `await using` for `IAsyncDisposable`

```csharp
// ℹ️ Info CC025 - Dispose() blocks on the async cleanup
using var resource = new AsyncResource();

// ✅ Fixed
await using var resource = new AsyncResource();
```

### CC026: `SemaphoreSlim.Wait()` in Async Code

```csharp
// ❌ Warning CC026 - blocks the thread; a classic deadlock source
public async Task RunAsync(SemaphoreSlim gate, CancellationToken ct)
{
    gate.Wait();
}

// ✅ Fixed
public async Task RunAsync(SemaphoreSlim gate, CancellationToken ct)
{
    await gate.WaitAsync(ct);
}
```

### CC027: Returned Task Uses a Disposed `using` Resource

```csharp
// ❌ Warning CC027 - the stream is disposed before the returned task completes
public Task<byte[]> ReadAsync(string path)
{
    using var stream = File.OpenRead(path);
    return ReadAllBytesAsync(stream);
}

// ✅ Fixed - make the method async so the resource lives until completion
public async Task<byte[]> ReadAsync(string path)
{
    using var stream = File.OpenRead(path);
    return await ReadAllBytesAsync(stream);
}
```

### CC028: Blocking I/O in Async Code

```csharp
// ❌ Warning CC028 - blocks the thread for the whole disk read
public async Task<string> LoadAsync(string path)
{
    var text = File.ReadAllText(path);   // also flags StreamReader.ReadToEnd()/ReadLine() and StreamWriter.Write/WriteLine/Flush
    await Task.Yield();
    return text;
}

// ✅ Fixed - the async counterpart yields the thread and accepts a CancellationToken
public async Task<string> LoadAsync(string path, CancellationToken cancellationToken)
{
    var text = await File.ReadAllTextAsync(path, cancellationToken);
    return text;
}

// ❌ Warning CC028 - the Stream primitives block too, on any Stream subclass
public async Task ArchiveAsync(Stream source, Stream destination)
{
    source.CopyTo(destination);          // also flags Stream Read/Write/Flush
    await Task.Yield();
}

// ✅ Fixed
public async Task ArchiveAsync(Stream source, Stream destination, CancellationToken cancellationToken)
{
    await source.CopyToAsync(destination, cancellationToken);
}
```

> `MemoryStream` is excluded — it is backed by an in-memory buffer, so the "blocking" call never
> leaves the CPU and the async form only wraps the same synchronous work.

### CC029: Timeout CTS Should Link the In-Scope Token

```csharp
// ❌ Warning CC029 - timeout ignores the caller's cancellation (e.g. RequestAborted)
public async Task RunAsync(CancellationToken cancellationToken)
{
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    await DoAsync(cts.Token);
}

// ✅ Fixed - parent cancel and timeout both apply
public async Task RunAsync(CancellationToken cancellationToken)
{
    using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    cts.CancelAfter(TimeSpan.FromSeconds(30));
    await DoAsync(cts.Token);
}
```

### CC030: Blocking `Process.WaitForExit()` in Async Code

```csharp
// ❌ Warning CC030 - blocks a thread for an unbounded wait on an external process
public async Task RunToolAsync(Process process)
{
    process.WaitForExit();
    await Task.Yield();
}

// ✅ Fixed - yields the thread and honours cancellation
public async Task RunToolAsync(Process process, CancellationToken cancellationToken)
{
    await process.WaitForExitAsync(cancellationToken);
}
```

> The `WaitForExit(int)` timeout overload is not flagged: it returns `bool` and `WaitForExitAsync`
> takes only a token, so there is no rewrite that preserves the call's meaning.

### CC031: Blocking Synchronization Primitives in Async Code

```csharp
// ❌ Warning CC031 - parks a pooled thread until another thread signals
public async Task WaitForReadyAsync(ManualResetEventSlim ready)
{
    ready.Wait();
    await Task.Yield();
}

// ✅ Fixed - an awaitable signal yields the thread and honours cancellation
public async Task WaitForReadyAsync(SemaphoreSlim ready, CancellationToken cancellationToken)
{
    await ready.WaitAsync(cancellationToken);
}
```

> Analyzer-only by design. These primitives have no `…Async` counterpart in .NET, so resolving the
> finding is a design change — a `SemaphoreSlim`, a `TaskCompletionSource`, or awaiting the task
> instead of joining the thread — rather than a mechanical rewrite. `SemaphoreSlim.Wait` belongs to
> CC026, which can offer a real fix.

### CC032: Async Call Not Awaited in Non-Async Code

```csharp
// ❌ Warning CC032 - a constructor cannot be async, so CS4014 never fires here
public Service()
{
    InitializeAsync();
}

// ✅ Fixed - the caller awaits, so cancellation and failures flow
public async Task StartAsync(CancellationToken cancellationToken)
{
    await InitializeAsync(cancellationToken);
}
```

> Fills a real compiler gap: **CS4014 only fires inside an async method**. In a constructor, a
> synchronous method, or a non-async lambda the compiler says nothing. A task that is assigned,
> returned, passed as an argument, or explicitly discarded with `_ =` is not dropped and is not
> flagged — `_ =` is the documented way to opt in deliberately. Analyzer-only: the right resolution
> depends on intent.

### CC033: `CancellationTokenSource` Field Never Disposed

```csharp
// ❌ Warning CC033 - created by this type, never disposed
public class Worker
{
    private readonly CancellationTokenSource _cts = new CancellationTokenSource();
}

// ✅ Fixed - the owner disposes what it created
public sealed class Worker : IDisposable
{
    private readonly CancellationTokenSource _cts = new CancellationTokenSource();

    public void Dispose() => _cts.Dispose();
}
```

> Complements CC014, which covers *local* sources and can offer a `using` fix. A field's lifetime is
> the object's, so the resolution is a design change and CC033 is analyzer-only. It fires only when
> the declaring type **creates** the source — an injected one is owned by whoever created it, and
> disposing it would be a bug. Fields that escape (returned or passed as an argument) and `static`
> fields stay quiet.

### CC034: Async Iterator Missing a `CancellationToken`

```csharp
// ❌ Warning CC034 - nothing can stop this enumeration
public async IAsyncEnumerable<int> ReadAsync()
{
    yield return 1;
}

// ✅ Fixed - the token flows in, and [EnumeratorCancellation] makes the consumer's
//    .WithCancellation(token) actually reach it
public async IAsyncEnumerable<int> ReadAsync(
    [EnumeratorCancellation] CancellationToken cancellationToken = default)
{
    yield return 1;
}
```

> Closes a gap between three rules: CC001 only covers `Task`/`ValueTask` returns, CC011 needs a token
> parameter to already exist, and CC010 flags the consumer. A stream declared with no token slips
> past all three. Only **iterators** (methods that yield) are flagged — a method that merely returns
> an `IAsyncEnumerable<T>` is a pass-through — and only public/protected ones, matching CC001.
> Overrides, interface implementations, and `extern` are excluded, since adding a parameter would
> break the contract rather than fix it.

## Configuration

All rules are enabled by default. Configure severity in `.editorconfig`:

```ini
[*.cs]
# Disable a rule
dotnet_diagnostic.CC001.severity = none

# Make a rule an error (fails build)
dotnet_diagnostic.CC002.severity = error

# Make CC006 more prominent
dotnet_diagnostic.CC006.severity = warning
```

## Compatibility and Supported Frameworks

- Analyzer assemblies target **.NET Standard 2.0** and compile against **Roslyn 4.8**, compatible
  with Visual Studio 2022 17.8+ and .NET SDK 8+ compiler hosts
- Consumer projects can target any framework supported by a compatible compiler host
- **ASP.NET Core** (Controllers, Minimal APIs, SignalR hubs, middleware via `HttpContext.RequestAborted`)
- **Hosted services** (`BackgroundService.ExecuteAsync`)
- **gRPC** (`ServerCallContext.CancellationToken`)
- **Entity Framework Core** (curated cancellable query and save methods)
- **HttpClient** (curated cancellable request and content methods)
- **MediatR** (IRequestHandler implementations)
- **Async streams** (`IAsyncEnumerable<T>`, `[EnumeratorCancellation]`)
- **ValueTask** and **ValueTask<T>** return types

## Project Quality

- **700+ regression tests** with comprehensive coverage, plus a cross-analyzer false-positive guard that
  runs every analyzer over idiomatic code (core, framework, nested-scope, exotic-syntax) and asserts
  zero diagnostics
- **Test-Driven Development** approach
- Built on official **Microsoft Roslyn APIs**
- Follows **.NET Analyzer best practices** (every rule documented, release-tracked, and covered by
  `RuleCatalogTests` drift guards)

## Building from Source

```bash
# Clone the repository
git clone https://github.com/georgepwall1991/CancelCop.Analyzer.git
cd CancelCop.Analyzer

# Restore and build
dotnet restore
dotnet build

# Run tests
dotnet test

# Pack NuGet package
dotnet pack src/CancelCop.Analyzer.Package/CancelCop.Analyzer.Package.csproj -c Release
```

## Project Structure

```
CancelCop.Analyzer/
├── src/
│   ├── CancelCop.Analyzer/           # Diagnostic analyzers
│   ├── CancelCop.Analyzer.CodeFixes/ # Code-fix providers
│   └── CancelCop.Analyzer.Package/   # NuGet packaging
├── tests/
│   └── CancelCop.Analyzer.Tests/     # xUnit regression suite
├── samples/
│   └── CancelCop.Sample/             # Example project with all rules
├── .github/workflows/                # CI/CD (build, test, publish)
└── docs/                             # Additional documentation
```

## Sample Project

The `samples/CancelCop.Sample` project demonstrates the analyzer rules with:

- focused examples grouped by diagnostic family;
- Both violation examples (triggering warnings) and correct patterns
- Detailed comments explaining why each rule matters

Build the sample to see the analyzers in action:
```bash
dotnet build samples/CancelCop.Sample
```

## Contributing

Contributions are welcome! Please see the
[contribution guidelines](https://github.com/georgepwall1991/CancelCop.Analyzer/blob/main/CONTRIBUTING.md).

Key points:
- Follow TDD approach (tests first)
- Ensure all tests pass
- Update documentation for new features
- One feature per pull request

## Roadmap

CancelCop now ships **29 rules** spanning token presence, propagation, positioning, loop checks,
async streams, blocking sync-over-async (including blocking File/StreamReader I/O), resource
lifecycle, async hygiene, and framework cancellation sources. The features originally planned here have shipped (under their final IDs):
`CancellationToken.None` misuse → **CC012**, unused token parameters → **CC016**, async void →
**CC023**. New rules are added opportunistically as common cancellation pitfalls surface; bug fixes
and false-positive hardening continue each release.

## License

[MIT License](https://github.com/georgepwall1991/CancelCop.Analyzer/blob/main/LICENSE)

## Author

**George Wall** - [GitHub](https://github.com/georgepwall1991)

---

⭐ If CancelCop helps you write better async code, consider giving it a star!
