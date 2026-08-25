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

CancelCop reports high-signal async and cancellation failures early (49 diagnostics, many with code fixes):

- missing `CancellationToken` on public async methods and framework handlers (controllers, Minimal APIs, MediatR, SignalR, `BackgroundService`)
- tokens accepted but not propagated to `HttpClient`, EF Core, `Task.Delay`, and other cancellable APIs
- loops and async streams that ignore cancellation (`ThrowIfCancellationRequested`, `.WithCancellation`, `[EnumeratorCancellation]`)
- timeout `CancellationTokenSource` that silently drops a parent token (`CreateLinkedTokenSource` + `CancelAfter`)
- sync-over-async and blocking I/O (`.Result` / `.Wait()`, `Thread.Sleep`, `SemaphoreSlim.Wait()`, blocking `File` / stream / socket APIs, `Process.WaitForExit()`, blocking sync primitives)
- `async void`, unawaited fire-and-forget calls, swallowed `OperationCanceledException`, and resource-lifetime bugs (undisposed CTS locals and fields, premature `using` disposal)

When the analyzer cannot prove a problem statically, it **stays quiet**. High-signal feedback, not noisy guesses.

## Install

```xml
<PackageReference Include="CancelCop.Analyzer" Version="1.52.29">
  <PrivateAssets>all</PrivateAssets>
  <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
</PackageReference>
```

Or:

```bash
dotnet add package CancelCop.Analyzer
```

```powershell
Install-Package CancelCop.Analyzer -Version 1.52.29
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
| **CC031** | Avoid blocking synchronization primitives (`ManualResetEventSlim.Wait`, `WaitHandle.WaitOne`, `Monitor.Wait`, `Thread.Join`, `ReaderWriterLockSlim.Enter*Lock`/`TryEnter*Lock`, `ReaderWriterLock.Acquire*Lock`/`UpgradeToWriterLock`, `Barrier.SignalAndWait`) in async code | Warning | ❌ |
| **CC032** | Async call discarded in non-async code, where the compiler's CS4014 does not fire | Warning | ❌ |
| **CC033** | `CancellationTokenSource` field created by the type and never disposed | Warning | ❌ |
| **CC034** | `ParallelOptions` created without `CancellationToken` while a token is in scope | Warning | ✅ |
| **CC035** | Empty `catch (OperationCanceledException)` silently discards the cancellation | Info | ❌ |
| **CC036** | Blocking `Socket` call (`Receive`, `Send`, `Accept`, `Connect`, …) in async code | Warning | ❌ |
| **CC037** | Blocking `TcpClient.Connect` in async code | Warning | ✅ |
| **CC038** | Blocking `TcpListener.AcceptTcpClient` / `AcceptSocket` in async code | Warning | ✅ |
| **CC039** | Blocking `UdpClient.Receive` in async code | Warning | ✅ |
| **CC040** | Blocking `HttpListener.GetContext` in async code | Warning | ✅ |
| **CC041** | Blocking `NamedPipeServerStream.WaitForConnection` in async code | Warning | ✅ |
| **CC042** | Blocking `NamedPipeClientStream.Connect` in async code | Warning | ✅ |
| **CC043** | Blocking `Dns.GetHostAddresses` in async code | Warning | ✅ |
| **CC044** | Blocking `Dns.GetHostEntry` in async code | Warning | ✅ |
| **CC045** | Blocking `DbConnection.Open` in async code | Warning | ✅ |
| **CC046** | Blocking `DbCommand.ExecuteReader` in async code | Warning | ✅ |
| **CC047** | Blocking `DbCommand.ExecuteNonQuery` in async code | Warning | ✅ |
| **CC048** | Blocking `DbCommand.ExecuteScalar` in async code | Warning | ✅ |
| **CC049** | Blocking `SmtpClient.Send` in async code | Warning | ✅ |

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

Convention ASP.NET middleware `Invoke`/`InvokeAsync(HttpContext)` is **not** flagged: the pipeline does not inject a `CancellationToken` parameter. Use `context.RequestAborted` (CC002/CC004/CC021).

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

When the enclosing method has an `HttpContext` (or gRPC `ServerCallContext`) instead of a token parameter, the same rule flows `context.RequestAborted` / `context.CancellationToken`:

```csharp
// ❌ Warning CC002 — RequestAborted is in scope but not passed
public async Task InvokeAsync(HttpContext context)
{
    await Task.Delay(100);
}

// ✅ Fixed
public async Task InvokeAsync(HttpContext context)
{
    await Task.Delay(100, context.RequestAborted);
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

Middleware with no token parameter is covered too — the in-scope token is `RequestAborted`:

```csharp
// ❌ Warning CC004
public async Task InvokeAsync(HttpContext context)
{
    return await _httpClient.GetStringAsync("https://api.example.com");
}

// ✅ Fixed
public async Task InvokeAsync(HttpContext context)
{
    return await _httpClient.GetStringAsync("https://api.example.com", context.RequestAborted);
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
> CC026, which can offer a real fix. `ReaderWriterLockSlim.Enter*Lock` / `TryEnter*Lock`,
> `ReaderWriterLock.Acquire*Lock`, and `Barrier.SignalAndWait` are included because they
> are not `WaitHandle` members and would otherwise be a silent false negative. A
> zero-timeout `TryEnter` or `Acquire*Lock` is an immediate probe and stays quiet.
> `UpgradeToWriterLock(0)` still reports: a failed upgrade restores the read lock
> with `Timeout.Infinite`.
> `Barrier.SignalAndWait(0)` still reports: the last arriver runs the post-phase action
> before returning.

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

### CC034: `ParallelOptions` Missing a `CancellationToken`

```csharp
// ❌ Warning CC034 - nothing can stop this loop
public void Process(int[] items, CancellationToken cancellationToken)
{
    var options = new ParallelOptions { MaxDegreeOfParallelism = 4 };
    Parallel.ForEach(items, options, Handle);
}

// ✅ Fixed - the loop observes cancellation between partitions
var options = new ParallelOptions
{
    MaxDegreeOfParallelism = 4,
    CancellationToken = cancellationToken,
};
```

> `ParallelOptions.CancellationToken` is the **only** way to cancel a `Parallel` loop. CC002 cannot
> see this: it matches calls with token-accepting overloads, but here the token is a property in an
> object initializer and `Parallel.ForEach` has no token-taking overload at all. Fires only when a
> token is actually in scope, and stays quiet when the token is assigned afterwards
> (`options.CancellationToken = token`).

### CC035: Cancellation Silently Swallowed by an Empty Catch

```csharp
// ❌ Info CC035 - the caller cannot tell the save did not happen
try
{
    await SaveAsync(cancellationToken);
}
catch (OperationCanceledException)
{
}
```

> CC019 covers a **broad** catch that swallows cancellation among everything else; a clause naming
> `OperationCanceledException` explicitly is outside its scope. Scoped to the **empty body**: any
> statement, a `when` filter, a rethrow, or even a comment recording the intent means the author
> considered the case, and the rule stays quiet. So `catch (TaskCanceledException) { /* expected on
> shutdown */ }` — the idiomatic wait-until-cancelled — is clean.

### CC036: Blocking Socket Calls in Async Code

```csharp
// ❌ Warning CC036 - can block indefinitely waiting for a connection
public async Task ServeAsync(Socket listener)
{
    var client = listener.Accept();
}

// ✅ Fixed
var client = await listener.AcceptAsync(cancellationToken);
```

> CC028 already covers every `Stream`, so a `NetworkStream` is handled there. `Socket` itself is not,
> because its async counterparts are **not signature-compatible** — `Receive(byte[])` pairs with
> `ReceiveAsync(Memory<byte>, CancellationToken)` — and that compatibility is exactly what makes
> CC028's rewrites safe. Loosening it would trade fix safety for reach, so this is a separate,
> analyzer-only rule.

### CC037: Blocking `TcpClient.Connect` in Async Code

```csharp
// ❌ Warning CC037 - parks a pool thread until the handshake finishes
public async Task RunAsync(TcpClient client, CancellationToken cancellationToken)
{
    cancellationToken.ThrowIfCancellationRequested();
    client.Connect(host, port);
}

// ✅ Fixed (.NET 6+; older targets use ConnectAsync(host, port) without a token)
await client.ConnectAsync(host, port, cancellationToken);
```

> CC036 covers `Socket.Connect`. Application code almost always uses the `TcpClient` wrapper,
> which none of the previous rules reported. `client.Client.Blocking = false` silences only
> IP/endpoint `Connect` on that same simple local, parameter, or field (the call returns
> `WouldBlock` instead of parking), including in top-level programs. Hostname `Connect` still
> reports — `TcpClient.Connect(string, int)` does synchronous DNS. Property or method receivers
> are not exempt (a getter may return a new instance). An unrelated `Socket.Blocking = false`
> does not exempt, and reassigning the client after `Blocking = false` invalidates the
> exemption. The fixer rewrites a safe `Connect` to `await ConnectAsync`,
> flowing an in-scope token. Named `hostname:` arguments are reported
> without a rewrite (`ConnectAsync` uses `host`). Null-conditional calls,
> positions where `await` cannot compile, and a this/base/this-alias call
> inside `ConnectAsync` are reported without a fix. The token-taking
> `ConnectAsync` overload is modern .NET only — `netstandard2.0` / .NET
> Framework have the tokenless form.

### CC038: Blocking `TcpListener` Accept in Async Code

```csharp
// ❌ Warning CC038 - parks a pool thread until a client connects
public async Task RunAsync(TcpListener listener, CancellationToken cancellationToken)
{
    cancellationToken.ThrowIfCancellationRequested();
    listener.AcceptTcpClient();
}

// ✅ Fixed (.NET 6+; older targets use AcceptTcpClientAsync() without a token)
await listener.AcceptTcpClientAsync(cancellationToken);
```

> CC036 covers `Socket.Accept`. CC037 covers `TcpClient.Connect`. The listener accept
> path is a third type, which none of the previous rules reported. A *positive*
> `if (listener.Pending())` / `while (Pending())` / `while (flag && Pending())`
> guard, the inverted poll (`if (!Pending()) continue;` then accept), and
> `listener.Server.Blocking = false` stay quiet; `if (!listener.Pending()) Accept`
> is the blocking path and still reports. The fixer rewrites a safe accept to
> `await AcceptTcpClientAsync` / `await AcceptSocketAsync`, flowing an in-scope
> token when the rewritten call binds. Null-conditional calls, positions where
> `await` cannot compile, and a this/base/this-alias call inside the matching
> `Accept*Async` are reported without a rewrite. Unusable TAP hiders stay quiet.
> The token-taking `Accept*Async` overloads are modern .NET only —
> `netstandard2.0` / .NET Framework have the tokenless form.

### CC039: Blocking `UdpClient.Receive` in Async Code

```csharp
// ❌ Warning CC039 - parks a pool thread until a datagram arrives
public async Task RunAsync(UdpClient client, CancellationToken cancellationToken)
{
    cancellationToken.ThrowIfCancellationRequested();
    IPEndPoint? remote = null;
    client.Receive(ref remote);
}

// ✅ Fixed (.NET 6+; older targets use ReceiveAsync() without a token)
await client.ReceiveAsync(cancellationToken);
```

> CC036 covers `Socket.Receive`. CC037 covers `TcpClient.Connect`. CC038 covers
> `TcpListener` accept. The UDP wrapper is a fourth type, which none of the
> previous rules reported. `if (client.Available > 0)`,
> `while (Available > 0)`, the inverted poll (`if (Available == 0) continue;`
> then receive), and `client.Client.Blocking = false` stay quiet;
> `if (Available == 0) Receive` is the blocking path and still reports.
> The fixer rewrites a discarded `Receive(ref endpoint)` statement to
> `var received = await ReceiveAsync(...)` and assigns
> `endpoint = received.RemoteEndPoint`. `ReceiveAsync` returns
> `UdpReceiveResult` and does not take the `ref` endpoint, so a
> value-use of the `byte[]` is reported without a rewrite. A braceless
> `if`/`while` body, null-conditional calls, await-illegal positions,
> and a this/base/this-alias call inside `ReceiveAsync` are reported
> without a rewrite. Unusable TAP hiders stay quiet. The token-taking
> `ReceiveAsync` overload is modern .NET only — `netstandard2.0` / .NET
> Framework have the tokenless form.

### CC040: Blocking `HttpListener.GetContext` in Async Code

```csharp
// ❌ Warning CC040 - parks a pool thread until a request arrives
public async Task RunAsync(HttpListener listener, CancellationToken cancellationToken)
{
    cancellationToken.ThrowIfCancellationRequested();
    listener.GetContext();
}

// ✅ Fixed
await listener.GetContextAsync();
```

> CC036–CC039 cover Socket / TcpClient / TcpListener / UdpClient. The HTTP
> listener is a fifth type, which none of the previous rules reported.
> The fixer rewrites a safe `GetContext()` to `await GetContextAsync()`.
> `GetContextAsync` does not take a `CancellationToken`, so the rewrite
> never invents one. Null-conditional calls and positions where `await`
> cannot compile are reported without a rewrite. `HttpListener` is sealed.

### CC041: Blocking `NamedPipeServerStream.WaitForConnection` in Async Code

```csharp
// ❌ Warning CC041 - parks a pool thread until a client connects
public async Task RunAsync(NamedPipeServerStream server, CancellationToken cancellationToken)
{
    cancellationToken.ThrowIfCancellationRequested();
    server.WaitForConnection();
}

// ✅ Fixed
await server.WaitForConnectionAsync(cancellationToken);
```

> CC028 covers File/Stream `Read`/`Write`/`CopyTo`/`Flush`. CC036–CC040 cover
> Socket / TcpClient / TcpListener / UdpClient / HttpListener. The named-pipe
> server is a sixth type, which none of the previous rules reported.
> The fixer rewrites a safe `WaitForConnection()` to
> `await WaitForConnectionAsync`, flowing an in-scope token when the
> rewritten call still binds. Null-conditional calls and positions
> where `await` cannot compile are reported without a rewrite.
> `NamedPipeServerStream` is sealed. The token-taking
> `WaitForConnectionAsync` overload is modern .NET only — .NET Framework has
> the tokenless form.

### CC042: Blocking `NamedPipeClientStream.Connect` in Async Code

```csharp
// ❌ Warning CC042 - parks a pool thread until the server accepts
public async Task RunAsync(NamedPipeClientStream client, CancellationToken cancellationToken)
{
    cancellationToken.ThrowIfCancellationRequested();
    client.Connect();
}

// ✅ Fixed
await client.ConnectAsync(cancellationToken);
```

> CC041 covers `NamedPipeServerStream.WaitForConnection`. The client connect
> is a sibling type, which none of the previous rules reported. The `int` and
> `TimeSpan` timeout overloads still park the thread and also report.
> The fixer rewrites a safe `Connect` to `await ConnectAsync`, keeping the
> timeout argument and flowing an in-scope token when the rewritten call
> still binds. There is no tokenless `ConnectAsync(TimeSpan)`, so that
> overload is reported without a rewrite unless a token is in scope.
> Null-conditional calls and positions where `await` cannot compile are
> reported without a rewrite. `NamedPipeClientStream` is sealed.
> `ConnectAsync` is modern .NET only — the rule stays quiet where that
> member is absent (.NET Framework has no `ConnectAsync` at all).

### CC043: Blocking `Dns.GetHostAddresses` in Async Code

```csharp
// ❌ Warning CC043 - parks a pool thread on a DNS query
public async Task RunAsync(string host, CancellationToken cancellationToken)
{
    cancellationToken.ThrowIfCancellationRequested();
    Dns.GetHostAddresses(host);
}

// ✅ Fixed
await Dns.GetHostAddressesAsync(host, cancellationToken);
```

> CC036–CC042 cover Socket / Tcp / Udp / HttpListener / named-pipe. DNS is a
> separate type, which none of the previous rules reported. CC002 cannot see
> it (no token overload of the invoked method). The `AddressFamily` overload
> and `using static System.Net.Dns` also report. A compile-time constant IP
> (`"127.0.0.1"`, `"::1"`, `const string`) is a parse, not a query, and stays
> quiet; `"localhost"` and non-const locals still report. The fixer rewrites
> a safe `GetHostAddresses` to `await GetHostAddressesAsync`, flowing an
> in-scope token when the rewritten call still binds. The `AddressFamily`
> TAP has an optional token, so a tokenless rewrite still compiles.
> Positions where `await` cannot compile are reported without a rewrite.
> A `using static` identifier rewrite is withheld when a same-named
> helper would capture the bind. `Dns` is a static type. The token-taking
> `GetHostAddressesAsync` overload
> is modern .NET only — .NET Framework has the tokenless form.

### CC044: Blocking `Dns.GetHostEntry` in Async Code

```csharp
// ❌ Warning CC044 - parks a pool thread on a DNS query (incl. reverse lookup)
public async Task RunAsync(string host, CancellationToken cancellationToken)
{
    cancellationToken.ThrowIfCancellationRequested();
    Dns.GetHostEntry(host);
}

// ✅ Fixed
await Dns.GetHostEntryAsync(host, cancellationToken);
```

> CC043 covers `GetHostAddresses` only. GetHostEntry is a sibling, which
> none of the previous rules reported. A numeric IP still reports — unlike
> GetHostAddresses, GetHostEntry does reverse DNS for that address. The
> `AddressFamily` and `IPAddress` overloads and `using static` also report.
> The fixer rewrites a safe `GetHostEntry` to `await GetHostEntryAsync`,
> flowing an in-scope token when the rewritten call still binds to
> `System.Net.Dns`. The `IPAddress` TAP is tokenless, so that rewrite
> never invents a token. The `AddressFamily` TAP has an optional token.
> A `using static` identifier rewrite is withheld when a same-named
> helper would capture the bind. Positions where `await` cannot compile
> are reported without a rewrite. The token-taking string
> `GetHostEntryAsync` overload is modern .NET only; the `IPAddress`
> async form is tokenless.

### CC045: Blocking `DbConnection.Open` in Async Code

```csharp
// ❌ Warning CC045 - parks a pool thread on a database handshake
public async Task RunAsync(DbConnection connection, CancellationToken cancellationToken)
{
    cancellationToken.ThrowIfCancellationRequested();
    connection.Open();
}

// ✅ Fixed
await connection.OpenAsync(cancellationToken);
```

> CC003 covers EF Core queries. ADO.NET `Open` is a separate type, which none
> of the previous rules reported. Concrete providers match through the
> override chain. `DbCommand.ExecuteReader` is CC046. The fixer rewrites a
> safe `Open()` to `await OpenAsync`, flowing an in-scope token. Null-conditional
> calls and positions where `await` cannot compile are reported without a fix.
> `OpenAsync` has accepted a `CancellationToken` since .NET Framework 4.5.

### CC046: Blocking `DbCommand.ExecuteReader` in Async Code

```csharp
// ❌ Warning CC046 - parks a pool thread on a database query
public async Task RunAsync(DbCommand command, CancellationToken cancellationToken)
{
    cancellationToken.ThrowIfCancellationRequested();
    command.ExecuteReader();
}

// ✅ Fixed
await command.ExecuteReaderAsync(cancellationToken);
```

> CC003 covers EF Core queries. CC045 covers `DbConnection.Open`. ADO.NET
> `ExecuteReader` is a separate member, which none of the previous rules
> reported. The method is not virtual — providers hide it with `new` for a
> covariant reader — and those hiders still report when they match the
> framework shape. Custom helpers, generic helpers, and statics stay
> quiet. `IDbCommand`
> stays quiet. `ExecuteNonQuery` is CC047. `ExecuteScalar` is CC048.
> The fixer rewrites a safe `ExecuteReader` to `await ExecuteReaderAsync`,
> preserving a `CommandBehavior` argument and flowing an in-scope token.
> When the original call is a receiver or is followed by `!`, the await
> is parenthesized. Null-conditional calls and positions where `await`
> cannot compile are
> reported without a fix. Provider `new` TAP hiders still match —
> `ExecuteReaderAsync()` / `ExecuteReaderAsync(CancellationToken)` are not
> virtual. A `Task<int>` hider is not a reader API and stays quiet.
> `ExecuteReaderAsync` has accepted a `CancellationToken` since .NET
> Framework 4.5.

### CC047: Blocking `DbCommand.ExecuteNonQuery` in Async Code

```csharp
// ❌ Warning CC047 - parks a pool thread on a command that does not return rows
public async Task RunAsync(DbCommand command, CancellationToken cancellationToken)
{
    cancellationToken.ThrowIfCancellationRequested();
    command.ExecuteNonQuery();
}

// ✅ Fixed
await command.ExecuteNonQueryAsync(cancellationToken);
```

> CC003 covers EF Core queries. CC045 covers `DbConnection.Open`. CC046
> covers `ExecuteReader`. ADO.NET `ExecuteNonQuery` is a separate member,
> which none of the previous rules reported. Overrides and `new` hiders
> that match the framework shape still report. Custom helpers, generic
> helpers, and `IDbCommand` stay quiet. `ExecuteScalar` is CC048.
> The fixer rewrites a safe `ExecuteNonQuery()` to
> `await ExecuteNonQueryAsync`, flowing an in-scope token. Null-conditional
> calls and positions where `await` cannot compile are reported without a
> fix. `ExecuteNonQueryAsync` has accepted a `CancellationToken` since .NET
> Framework 4.5.
> `ExecuteNonQueryAsync` has accepted a `CancellationToken` since .NET
> Framework 4.5.

### CC048: Blocking `DbCommand.ExecuteScalar` in Async Code

```csharp
// ❌ Warning CC048 - parks a pool thread on a single-value query
public async Task RunAsync(DbCommand command, CancellationToken cancellationToken)
{
    cancellationToken.ThrowIfCancellationRequested();
    command.ExecuteScalar();
}

// ✅ Fixed
await command.ExecuteScalarAsync(cancellationToken);
```

> CC003 covers EF Core queries. CC045 covers `DbConnection.Open`. CC046
> covers `ExecuteReader`. CC047 covers `ExecuteNonQuery`. ADO.NET
> `ExecuteScalar` is a separate member, which none of the previous rules
> reported. Overrides and `new` hiders that match the framework shape
> still report, including a more-derived return such as `string`. Custom
> helpers, generic helpers, statics, `void` hiders, `Task`/`ValueTask`
> hiders, and `IDbCommand` stay
> quiet. The fixer rewrites a safe `ExecuteScalar()` to
> `await ExecuteScalarAsync`, flowing an in-scope token. Null-conditional
> calls, positions where `await` cannot compile, and a this/base/this-alias
> call inside `ExecuteScalarAsync` are reported without a fix. Covariant
> `Task<string>` hiders still match.
> `ExecuteScalarAsync` has accepted a `CancellationToken` since .NET
> Framework 4.5.

### CC049: Blocking `SmtpClient.Send` in Async Code

```csharp
// ❌ Warning CC049 - parks a pool thread on an SMTP handshake
public async Task RunAsync(SmtpClient client, MailMessage message, CancellationToken cancellationToken)
{
    cancellationToken.ThrowIfCancellationRequested();
    client.Send(message);
}

// ✅ Fixed
await client.SendMailAsync(message, cancellationToken);
```

> CC004 covers HttpClient. ADO.NET rules cover database waits. `SmtpClient.Send`
> is a separate type, which none of the previous rules reported. The TAP
> counterpart is `SendMailAsync`, **not** the event-based `SendAsync`. Token-taking
> `SendMailAsync` is .NET 5+; .NET Framework has the tokenless form. `Send` is
> not virtual; `new` hiders that match the framework shape still report.
> The fixer rewrites a safe `Send` to `await SendMailAsync`, flowing an
> in-scope token. Null-conditional calls, positions where `await` cannot
> compile, and a this/base/this-alias call inside `SendMailAsync` are
> reported without a fix.

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

CancelCop now ships **49 rules** spanning token presence, propagation, positioning, loop checks,
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
