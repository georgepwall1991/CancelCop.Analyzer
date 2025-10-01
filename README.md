# CancelCop Analyzer

A surgical Roslyn analyzer focused on **CancellationToken** propagation and honoring across public APIs, handlers, EF Core, and HTTP calls, with automatic code fixes.

## Features

- **CC001**: Detects public and protected async methods missing `CancellationToken` parameters
- **Code Fix Provider**: Automatically adds `CancellationToken` parameters with default values
- **Smart Ordering**: Maintains alphabetical using directive ordering
- **TDD Approach**: Comprehensive test coverage using XUnit and Roslyn testing framework

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

## Future Rules (Planned)

- **CC002**: CancellationToken must be propagated to async calls
- **CC003**: EF Core queries must pass CancellationToken
- **CC004**: HttpClient methods must pass CancellationToken
- **CC005**: Handler methods (MediatR, Controllers) must accept and honor CancellationToken
- **CC006**: CancellationToken should be last parameter (convention)

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
