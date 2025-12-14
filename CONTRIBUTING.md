# Contributing to CancelCop.Analyzer

Thank you for your interest in contributing to CancelCop! We welcome contributions from the community.

## Getting Started

1. Fork the repository
2. Clone your fork locally
3. Create a new branch for your feature or bugfix
4. Make your changes following our guidelines
5. Ensure all tests pass
6. Submit a pull request

## Development Setup

### Prerequisites

- **.NET 10.0 SDK** (or later)
- A C# IDE (Visual Studio, JetBrains Rider, or VS Code with C# extension)
- Git

### Building the Project

```bash
# Clone your fork
git clone https://github.com/YOUR_USERNAME/CancelCop.Analyzer.git
cd CancelCop.Analyzer

# Restore dependencies
dotnet restore

# Build solution
dotnet build

# Run tests
dotnet test

# Run tests with verbose output
dotnet test --logger "console;verbosity=detailed"
```

### Project Structure

```
CancelCop.Analyzer/
├── src/
│   ├── CancelCop.Analyzer/           # Main analyzers and code fix providers
│   │   ├── *Analyzer.cs              # Diagnostic analyzers (CC001, CC002, etc.)
│   │   ├── *CodeFixProvider.cs       # Code fix providers
│   │   └── CancellationTokenHelpers.cs  # Shared helper methods
│   └── CancelCop.Analyzer.Package/   # NuGet packaging project
├── tests/
│   └── CancelCop.Analyzer.Tests/     # XUnit tests
│       ├── *AnalyzerTests.cs         # Tests for analyzers
│       └── *CodeFixTests.cs          # Tests for code fixes
├── samples/
│   └── CancelCop.Sample/             # Example project demonstrating rules
└── docs/                             # Additional documentation
```

## Development Guidelines

### Test-Driven Development (TDD)

This project strictly follows TDD principles. For any new feature or bug fix:

1. **Write tests first**: Define the expected behavior through tests
2. **Run tests**: Verify that the new tests fail (red)
3. **Implement**: Write the minimum code to make tests pass (green)
4. **Refactor**: Clean up the code while keeping tests green
5. **Repeat**: Continue until the feature is complete

### Adding a New Analyzer Rule

Follow this checklist when adding a new rule:

#### 1. Plan the Rule

- [ ] Define the rule ID (e.g., CC010)
- [ ] Write a clear description of what it detects
- [ ] Document why this matters (the problem it solves)
- [ ] Choose appropriate severity (Warning, Info, Error)
- [ ] Determine if a code fix is feasible

#### 2. Write Tests First

Create test file: `tests/CancelCop.Analyzer.Tests/[RuleName]AnalyzerTests.cs`

```csharp
public class MyNewAnalyzerTests
{
    [Fact]
    public async Task ViolatingCode_ShouldReportDiagnostic()
    {
        var test = @"
// Code that violates the rule
";
        var expected = VerifyCS.Diagnostic("CC0XX")
            .WithLocation(0)
            .WithArguments("expected", "arguments");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task CorrectCode_ShouldNotReportDiagnostic()
    {
        var test = @"
// Code that follows the rule
";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
```

#### 3. Implement the Analyzer

Create analyzer file: `src/CancelCop.Analyzer/[RuleName]Analyzer.cs`

- Add comprehensive XML documentation
- Use `CancellationTokenHelpers` for common operations
- Register appropriate syntax node actions
- Include the rule ID in a public constant

#### 4. Implement Code Fix Provider (if applicable)

Create code fix file: `src/CancelCop.Analyzer/[RuleName]CodeFixProvider.cs`

- Create corresponding test file: `*CodeFixTests.cs`
- Ensure the fix preserves code formatting
- Handle edge cases gracefully

#### 5. Update Documentation

- [ ] Add rule to `AnalyzerReleases.Unshipped.md`
- [ ] Update `NEXT_STEPS.md` with completion status
- [ ] Add sample to `samples/CancelCop.Sample/`
- [ ] Update `README.md` with new rule

#### 6. Final Verification

```bash
# Run all tests
dotnet test

# Build in Release mode
dotnet build -c Release

# Verify sample project shows expected warnings
dotnet build samples/CancelCop.Sample
```

### Code Style

#### General

- Follow the existing code style in the project
- Use meaningful names for variables, methods, and classes
- Keep methods focused and small (single responsibility)
- Prefer early returns over deep nesting

#### XML Documentation

All public APIs must have XML documentation:

```csharp
/// <summary>
/// Brief description of the class/method.
/// </summary>
/// <remarks>
/// <para><b>Rule ID:</b> CC0XX</para>
/// <para><b>Why this matters:</b> Explanation...</para>
/// </remarks>
/// <example>
/// <code>
/// // Example usage
/// </code>
/// </example>
```

#### Analyzer Best Practices

- Use `CancellationTokenHelpers` for common CancellationToken operations
- Register for specific syntax nodes, not entire compilation
- Enable concurrent execution: `context.EnableConcurrentExecution()`
- Exclude generated code: `context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None)`
- Store diagnostic properties for code fix providers

### Testing

#### Test Requirements

- All tests must pass before submitting a PR
- Add tests for both positive (violations) and negative (no violations) cases
- Test edge cases and boundary conditions
- Use the Roslyn testing framework patterns

#### Test Naming Convention

```
[MethodUnderTest]_[Scenario]_[ExpectedBehavior]
```

Examples:
- `ForEachLoop_WithoutCancellationCheck_ShouldReportDiagnostic`
- `ForLoop_WithThrowIfCancellationRequested_ShouldNotReportDiagnostic`

### Commit Messages

Use conventional commit format:

```
type(scope): brief description

[optional body]

[optional footer]
```

Types:
- `feat`: New feature
- `fix`: Bug fix
- `test`: Adding/updating tests
- `docs`: Documentation changes
- `refactor`: Code changes that don't fix bugs or add features
- `chore`: Maintenance tasks

Examples:
- `feat(CC009): add loop cancellation analyzer`
- `fix(CC002): handle local functions correctly`
- `test(CC001): add ValueTask return type tests`
- `docs: update README with CC009 examples`

## Pull Request Process

### Before Submitting

1. [ ] All tests pass locally
2. [ ] Code follows project style guidelines
3. [ ] XML documentation added for public APIs
4. [ ] `AnalyzerReleases.Unshipped.md` updated (if adding/changing rules)
5. [ ] README updated (if adding new features)
6. [ ] No merge conflicts with main branch

### PR Guidelines

- **One feature per PR**: Keep pull requests focused
- **Clear title**: Use conventional commit format
- **Description**: Explain what changes you made and why
- **Reference issues**: Use `Fixes #123` or `Closes #123`
- **Screenshots**: Include if there are visual changes

### Review Process

1. Create PR against `main` branch
2. Wait for automated CI checks to pass
3. Address reviewer feedback
4. Once approved, maintainer will merge

## Reporting Bugs

When reporting bugs, please include:

- **Clear description** of the issue
- **Steps to reproduce** the problem
- **Expected behavior** vs actual behavior
- **Code sample** that demonstrates the issue
- **Environment**: OS, .NET version, IDE

Use this template:

```markdown
## Bug Description
[Clear description of the issue]

## Steps to Reproduce
1. [First step]
2. [Second step]
3. [...]

## Expected Behavior
[What should happen]

## Actual Behavior
[What actually happens]

## Code Sample
```csharp
// Code that demonstrates the issue
```

## Environment
- OS: [e.g., macOS 14.0, Windows 11]
- .NET SDK: [e.g., 10.0.101]
- IDE: [e.g., Rider 2024.3, VS 2022]
```

## Suggesting Features

Feature suggestions are welcome! Please:

1. Check existing issues to avoid duplicates
2. Provide a clear use case
3. Explain how it aligns with CancelCop's goals
4. Consider providing a PR if you can implement it

## Questions?

- Open an issue for discussion
- Check existing issues and pull requests
- Review the README.md and documentation

## License

By contributing to CancelCop, you agree that your contributions will be licensed under the MIT License.
