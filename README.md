# CancelCop Analyzer

[![NuGet](https://img.shields.io/nuget/v/CancelCop.Analyzer.svg)](https://www.nuget.org/packages/CancelCop.Analyzer/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

A surgical Roslyn analyzer focused on **CancellationToken** best practices: propagation, parameter positioning, loop cancellation checks, and more. Includes automatic code fixes.

## Why CancelCop?

CancellationToken is essential for building responsive .NET applications, but it's easy to forget:
- Adding tokens to public async methods
- Propagating tokens to inner async calls
- Checking for cancellation in loops
- Following parameter ordering conventions

CancelCop catches these issues at compile time and offers one-click fixes.

## Installation

```bash
dotnet add package CancelCop.Analyzer
```

Or via Package Manager:
```powershell
Install-Package CancelCop.Analyzer
```

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

## Supported Frameworks

- **.NET 6.0+** (including .NET 8, .NET 9, .NET 10)
- **ASP.NET Core** (Controllers and Minimal APIs)
- **Entity Framework Core** (all async methods)
- **HttpClient** (all async methods)
- **MediatR** (IRequestHandler implementations)
- **ValueTask** and **ValueTask<T>** return types

## Project Quality

- **111 unit tests** with comprehensive coverage
- **Test-Driven Development** approach
- Built on official **Microsoft Roslyn APIs**
- Follows **.NET Analyzer best practices**

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
dotnet pack src/CancelCop.Analyzer/CancelCop.Analyzer.csproj -c Release
```

## Project Structure

```
CancelCop.Analyzer/
├── src/
│   ├── CancelCop.Analyzer/           # Analyzers and code fix providers
│   └── CancelCop.Analyzer.Package/   # NuGet packaging
├── tests/
│   └── CancelCop.Analyzer.Tests/     # XUnit tests (111 tests)
├── samples/
│   └── CancelCop.Sample/             # Example project with all rules
├── .github/workflows/                # CI/CD (build, test, publish)
└── docs/                             # Additional documentation
```

## Sample Project

The `samples/CancelCop.Sample` project demonstrates all analyzer rules with:
- Separate files for each rule (CC001, CC002, etc.)
- Both violation examples (triggering warnings) and correct patterns
- Detailed comments explaining why each rule matters

Build the sample to see the analyzers in action:
```bash
dotnet build samples/CancelCop.Sample
```

## Contributing

Contributions are welcome! Please see [CONTRIBUTING.md](CONTRIBUTING.md) for guidelines.

Key points:
- Follow TDD approach (tests first)
- Ensure all tests pass
- Update documentation for new features
- One feature per pull request

## Roadmap

See [NEXT_STEPS.md](NEXT_STEPS.md) for planned features:
- CC007: Detect `CancellationToken.None` usage
- CC008: Detect unused CancellationToken parameters
- CC010: Detect async void methods
- CC006 code fix for parameter reordering

## License

[MIT License](LICENSE)

## Author

**George Wall** - [GitHub](https://github.com/georgepwall1991)

---

⭐ If CancelCop helps you write better async code, consider giving it a star!
