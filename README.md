# CancelCop Analyzer

A surgical Roslyn analyzer focused on **CancellationToken** propagation and honoring across public APIs, handlers, EF Core, and HTTP calls, with automatic code fixes.

## Features

### Analyzers
- **CC001**: Public async methods must have CancellationToken parameter
- **CC002**: CancellationToken must be propagated to async calls (Task.Delay, Task.Run, custom async methods)
- **CC003**: EF Core queries must pass CancellationToken (ToListAsync, FirstOrDefaultAsync, SaveChangesAsync, etc.)
- **CC004**: HttpClient methods must pass CancellationToken (GetAsync, PostAsync, etc.)
- **CC005A**: MediatR handlers must accept CancellationToken parameter
- **CC005B**: Controller action methods must accept CancellationToken parameter
- **CC005C**: Minimal API endpoint handlers must accept CancellationToken parameter
- **CC006**: CancellationToken should be the last parameter (convention, Info severity)

### Code Fix Providers
- Automatically adds `CancellationToken` parameters with default values
- Propagates tokens to inner async calls
- Smart using directive handling
- Works with lambdas and method declarations

### Quality
- **TDD Approach**: Comprehensive test coverage (82 tests passing)
- **Built on Roslyn**: Uses official Microsoft Roslyn APIs
- **Well Tested**: Full integration with XUnit and Roslyn testing framework

## Installation

```bash
dotnet add package CancelCop.Analyzer
```

## Usage

Once installed, the analyzer will automatically detect violations in your code:

```csharp
// ❌ Warning CC001: Missing CancellationToken
public async Task ProcessDataAsync()
{
    await Task.Delay(100);
}

// ✅ Correct: Has CancellationToken
public async Task ProcessDataAsync(CancellationToken cancellationToken = default)
{
    await Task.Delay(100, cancellationToken);
}
```

## Analyzer Rules

### CC001: Public async methods must have CancellationToken parameter

**Severity**: Warning

**Description**: Public and protected async methods should accept a `CancellationToken` parameter to allow cancellation of async operations.

**Code Fix**: Adds `CancellationToken cancellationToken = default` as the last parameter.

## Examples

```csharp
// CC001: Missing CancellationToken parameter
public async Task ProcessDataAsync() // ❌ Warning
{
    await Task.Delay(100);
}

// ✅ Fixed
public async Task ProcessDataAsync(CancellationToken cancellationToken = default)
{
    await Task.Delay(100, cancellationToken);
}

// CC002: Not propagating CancellationToken
public async Task ProcessAsync(CancellationToken cancellationToken)
{
    await Task.Delay(100); // ❌ Warning: Should pass cancellationToken
    await DoWorkAsync(); // ❌ Warning: Should pass cancellationToken
}

// CC003: EF Core without CancellationToken
public async Task<User> GetUserAsync(int id, CancellationToken cancellationToken)
{
    return await _context.Users.FirstOrDefaultAsync(u => u.Id == id); // ❌ Warning
}

// CC004: HttpClient without CancellationToken
public async Task<string> FetchDataAsync(CancellationToken cancellationToken)
{
    return await _httpClient.GetStringAsync("https://api.example.com"); // ❌ Warning
}

// CC005B: Controller action without CancellationToken
[HttpGet]
public async Task<IActionResult> GetUsers() // ❌ Warning
{
    var users = await _service.GetUsersAsync();
    return Ok(users);
}

// CC005C: Minimal API without CancellationToken
app.MapGet("/users", async () => await GetUsersAsync()); // ❌ Warning

// CC006: CancellationToken not last parameter
public async Task ProcessAsync(CancellationToken cancellationToken, string name) // ℹ️ Info
{
    // Convention suggests CancellationToken should be last
}
```

## Building from Source

```bash
# Restore dependencies
dotnet restore

# Build solution
dotnet build

# Run tests
dotnet test

# Pack NuGet package
dotnet pack src/CancelCop.Analyzer/CancelCop.Analyzer.csproj -c Release
```

## Project Structure

```
CancelCop/
├── src/
│   ├── CancelCop.Analyzer/          # Main analyzer and code fix provider
│   └── CancelCop.Analyzer.Package/  # NuGet packaging project
├── tests/
│   └── CancelCop.Analyzer.Tests/    # XUnit tests using Roslyn testing framework
├── samples/
│   └── CancelCop.Sample/            # Example project demonstrating the analyzer
├── .github/workflows/               # CI/CD workflows
├── CancelCop.sln                    # Solution file with folder structure
├── Directory.Build.props            # Shared MSBuild properties
├── .editorconfig                    # Code style configuration
└── NEXT_STEPS.md                    # Development roadmap
```

## Contributing

Contributions are welcome! Please ensure:
- All tests pass
- Follow TDD approach for new rules
- Add tests for both analyzer and code fix provider

## License

MIT
