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
