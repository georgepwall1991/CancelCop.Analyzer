# CancelCop Analyzer

A surgical Roslyn analyzer focused on **CancellationToken** best practices: propagation, parameter positioning, loop cancellation checks, and more. Includes automatic code fixes.

## Why CancelCop?

CancelCop helps you build responsive, cancellable .NET applications by ensuring CancellationToken is properly used throughout your codebase. It catches issues at compile time and provides automatic fixes.

## Features

### 9 Comprehensive Analyzers

| Rule | Description | Severity |
|------|-------------|----------|
| **CC001** | Public async methods must have CancellationToken parameter | Warning |
| **CC002** | CancellationToken must be propagated to async calls | Warning |
| **CC003** | EF Core queries must pass CancellationToken | Warning |
| **CC004** | HttpClient methods must pass CancellationToken | Warning |
| **CC005A** | MediatR handlers must accept CancellationToken | Warning |
| **CC005B** | Controller actions must accept CancellationToken | Warning |
| **CC005C** | Minimal API endpoints must accept CancellationToken | Warning |
| **CC006** | CancellationToken should be the last parameter | Info |
| **CC009** | Loops should check for cancellation | Warning |

### Automatic Code Fixes

- ✅ Adds CancellationToken parameters to method signatures
- ✅ Propagates tokens to inner async calls
- ✅ Adds cancellation checks to loops
- ✅ Handles lambda expressions (Minimal APIs)
- ✅ Smart using directive management
- ✅ Preserves code formatting

## Quick Start

### Installation

```bash
dotnet add package CancelCop.Analyzer
```

### Usage

Once installed, the analyzer runs automatically during build. It will:

1. **Detect** CancellationToken issues in your code
2. **Show warnings** in your IDE with clear descriptions
3. **Offer code fixes** - just click the lightbulb 💡

### Example

```csharp
// ❌ Before (CC001 warning)
public async Task ProcessDataAsync()
{
    await Task.Delay(100);
}

// ✅ After (automatic fix applied)
public async Task ProcessDataAsync(CancellationToken cancellationToken = default)
{
    await Task.Delay(100, cancellationToken);
}
```

## Supported Patterns

### Entity Framework Core
```csharp
await _context.Users.ToListAsync(cancellationToken);
await _context.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
await _context.SaveChangesAsync(cancellationToken);
```

### HttpClient
```csharp
await httpClient.GetAsync(url, cancellationToken);
await httpClient.PostAsync(url, content, cancellationToken);
await httpClient.SendAsync(request, cancellationToken);
```

### ASP.NET Core Controllers
```csharp
[HttpGet]
public async Task<IActionResult> GetUsers(CancellationToken cancellationToken)
{
    var users = await _service.GetUsersAsync(cancellationToken);
    return Ok(users);
}
```

### Minimal APIs
```csharp
app.MapGet("/users", async (CancellationToken ct) =>
    await GetUsersAsync(ct));
```

### MediatR Handlers
```csharp
public class MyHandler : IRequestHandler<MyRequest, MyResponse>
{
    public async Task<MyResponse> Handle(
        MyRequest request,
        CancellationToken cancellationToken)
    {
        // ...
    }
}
```

### Loop Cancellation (New in v1.3.0)
```csharp
public async Task ProcessItemsAsync(List<Item> items, CancellationToken ct)
{
    foreach (var item in items)
    {
        ct.ThrowIfCancellationRequested();  // Analyzer ensures this is present
        await ProcessAsync(item, ct);
    }
}
```

## Benefits

- 🎯 **Responsive Applications**: Properly cancel long-running operations
- 🚀 **Better Performance**: Avoid wasting resources on cancelled operations
- 🛡️ **Production Ready**: Reduce timeout issues and improve reliability
- ⚡ **Developer Friendly**: Automatic fixes save time and reduce errors
- 📊 **Comprehensive**: Covers all major .NET async patterns

## Configuration

All rules are enabled by default with appropriate severity levels. Configure in `.editorconfig`:

```ini
[*.cs]
# Adjust severity (none, suggestion, warning, error)
dotnet_diagnostic.CC001.severity = warning
dotnet_diagnostic.CC006.severity = suggestion
dotnet_diagnostic.CC009.severity = warning
```

## Supported Frameworks

- **.NET 6.0+** (including .NET 8, .NET 9, .NET 10)
- **ASP.NET Core** (Controllers and Minimal APIs)
- **Entity Framework Core** (all async methods)
- **HttpClient** (all async methods)
- **MediatR** (IRequestHandler implementations)
- **ValueTask** and **ValueTask<T>** return types

## Test Coverage

- **111 tests** ensuring reliability
- All analyzers and code fixes thoroughly tested
- Covers edge cases and complex scenarios

## Learn More

- [GitHub Repository](https://github.com/georgepwall1991/CancelCop.Analyzer)
- [Report Issues](https://github.com/georgepwall1991/CancelCop.Analyzer/issues)
- [Sample Project](https://github.com/georgepwall1991/CancelCop.Analyzer/tree/main/samples/CancelCop.Sample)

---

Built with ❤️ using Roslyn and following TDD best practices.
