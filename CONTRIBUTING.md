# Contributing to CancelCop.Analyzer

Thank you for your interest in contributing to CancelCop! We welcome contributions from the community.

## Getting Started

1. Fork the repository
2. Clone your fork locally
3. Create a new branch for your feature or bugfix
4. Make your changes
5. Ensure all tests pass
6. Submit a pull request

## Development Setup

### Prerequisites

- .NET 9.0 SDK or later
- A C# IDE (Visual Studio, Rider, or VS Code)

### Building the Project

```bash
# Restore dependencies
dotnet restore

# Build solution
dotnet build

# Run tests
dotnet test
```

## Development Guidelines

### Test-Driven Development (TDD)

This project follows TDD principles:

1. **Write tests first**: Before implementing any new analyzer or code fix, write tests that define the expected behavior
2. **Run tests**: Verify that the new tests fail (red)
3. **Implement**: Write the minimum code to make tests pass (green)
4. **Refactor**: Clean up the code while keeping tests green

### Adding a New Analyzer Rule

When adding a new analyzer rule:

1. Create test cases in `tests/CancelCop.Analyzer.Tests/`
2. Implement the analyzer in `src/CancelCop.Analyzer/Analyzers/`
3. If applicable, create a code fix provider in `src/CancelCop.Analyzer/CodeFixes/`
4. Add tests for the code fix provider
5. Update the README.md with the new rule documentation
6. Update the CHANGELOG.md

### Code Style

- Follow the existing code style in the project
- Use meaningful names for variables, methods, and classes
- Add XML documentation comments to public APIs
- Keep methods focused and small

### Testing

- All tests must pass before submitting a PR
- Add tests for both positive and negative cases
- Test edge cases and boundary conditions
- Use the Roslyn testing framework for analyzer tests
- Aim for high code coverage

### Pull Request Process

1. Update the CHANGELOG.md with details of changes
2. Ensure all tests pass locally
3. Update documentation if needed
4. Create a pull request with a clear title and description
5. Link any relevant issues
6. Wait for code review and address feedback

## Pull Request Guidelines

- **One feature per PR**: Keep pull requests focused on a single feature or bugfix
- **Clear descriptions**: Explain what changes you made and why
- **Reference issues**: Link to related issues using `Fixes #123` or `Closes #123`
- **Clean commits**: Use meaningful commit messages
- **Up to date**: Rebase on the latest main branch before submitting

## Reporting Bugs

When reporting bugs, please include:

- A clear description of the issue
- Steps to reproduce
- Expected behavior
- Actual behavior
- Code samples that demonstrate the issue
- Your environment (OS, .NET version, IDE)

## Suggesting Features

Feature suggestions are welcome! Please:

- Check existing issues to avoid duplicates
- Provide a clear use case
- Explain how it aligns with the project goals
- Consider providing a pull request if you can

## Code of Conduct

Please note that this project is released with a [Code of Conduct](CODE_OF_CONDUCT.md). By participating in this project you agree to abide by its terms.

## Questions?

If you have questions, feel free to:

- Open an issue for discussion
- Check existing issues and pull requests
- Review the README.md for documentation

## License

By contributing to CancelCop, you agree that your contributions will be licensed under the MIT License.
