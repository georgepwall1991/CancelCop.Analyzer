# CancelCop Analyzer

A surgical Roslyn analyzer focused on **CancellationToken** propagation and honoring across public APIs, handlers, EF Core, HTTP calls, and Minimal APIs, with comprehensive automatic code fixes.

## Why CancelCop?

CancelCop helps you build responsive, cancellable .NET applications by ensuring CancellationToken is properly used throughout your codebase. It detects missing tokens and provides automatic fixes with a single click.

## Features

### 8 Comprehensive Analyzers

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

### Automatic Code Fixes

- ✅ Adds CancellationToken parameters to method signatures
- ✅ Propagates tokens to inner async calls
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
// Detects missing tokens in:
await _context.Users.ToListAsync(cancellationToken);
await _context.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
await _context.SaveChangesAsync(cancellationToken);
```

### HttpClient
```csharp
// Detects missing tokens in:
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

## Benefits

- 🎯 **Responsive Applications**: Properly cancel long-running operations
- 🚀 **Better Performance**: Avoid wasting resources on cancelled operations
- 🛡️ **Production Ready**: Reduce timeout issues and improve reliability
- ⚡ **Developer Friendly**: Automatic fixes save time and reduce errors
- 📊 **Comprehensive**: Covers all major .NET async patterns

## Configuration

All rules are enabled by default with appropriate severity levels. You can configure them in `.editorconfig`:

```ini
[*.cs]
# Adjust severity (none, suggestion, warning, error)
dotnet_diagnostic.CC001.severity = warning
dotnet_diagnostic.CC006.severity = suggestion
```

## Test Coverage

- **82 tests** ensuring reliability
- All analyzers and code fixes thoroughly tested
- Covers edge cases and complex scenarios

## Learn More

- [GitHub Repository](https://github.com/georgepwall1991/CancelCop.Analyzer)
- [Report Issues](https://github.com/georgepwall1991/CancelCop.Analyzer/issues)

---

Built with ❤️ using Roslyn and following TDD best practices.
