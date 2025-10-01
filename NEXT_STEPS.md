# CancelCop - Next Steps & Roadmap

## ✅ Completed (v1.0.0)

- [x] **CC001**: Public/protected async methods must have CancellationToken parameter
  - Analyzer implementation
  - Code fix provider with smart using directive handling
  - Comprehensive test coverage (10/10 passing)
- [x] Project structure with src/tests/samples folders
- [x] TDD infrastructure with XUnit and Roslyn testing framework
- [x] NuGet package configuration
- [x] GitHub Actions CI/CD workflows
- [x] Documentation (README, .editorconfig, Directory.Build.props)

---

## 🎯 Immediate Next Steps (v1.1.0)

### CC002: CancellationToken Propagation Detection
**Priority: HIGH** | **Effort: Medium**

Detect when async methods have a CancellationToken parameter but fail to pass it to inner async calls.

#### Examples to Detect:
```csharp
// ❌ Has token but doesn't propagate
public async Task ProcessAsync(CancellationToken cancellationToken)
{
    await Task.Delay(100);  // Should pass cancellationToken
    await DoWorkAsync();    // Should pass cancellationToken
}

// ✅ Correct
public async Task ProcessAsync(CancellationToken cancellationToken)
{
    await Task.Delay(100, cancellationToken);
    await DoWorkAsync(cancellationToken);
}
```

#### Implementation Steps (TDD):
1. Write tests for missing token in `Task.Delay`, `Task.Run`, etc.
2. Write tests for missing token in custom async method calls
3. Implement analyzer using `InvocationExpressionSyntax` analysis
4. Write code fix tests
5. Implement code fix to auto-add token parameter to invocations
6. Build and verify

---

### CC003: EF Core Query Detection
**Priority: HIGH** | **Effort: Medium**

Ensure EF Core query methods receive CancellationToken.

#### Examples to Detect:
```csharp
// ❌ Missing token
public async Task<User> GetUserAsync(int id, CancellationToken cancellationToken)
{
    return await _context.Users.FirstOrDefaultAsync(u => u.Id == id);
}

// ✅ Correct
public async Task<User> GetUserAsync(int id, CancellationToken cancellationToken)
{
    return await _context.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
}
```

#### Key Methods to Monitor:
- `ToListAsync`, `ToArrayAsync`, `ToDictionaryAsync`
- `FirstAsync`, `FirstOrDefaultAsync`
- `SingleAsync`, `SingleOrDefaultAsync`
- `AnyAsync`, `AllAsync`, `CountAsync`
- `ForEachAsync`, `SumAsync`, `AverageAsync`
- `SaveChangesAsync`

#### Implementation Steps (TDD):
1. Write tests for each EF Core async extension method
2. Implement analyzer detecting `DbContext` and `IQueryable` operations
3. Write code fix tests
4. Implement code fix provider
5. Build and verify

---

### CC004: HttpClient Methods
**Priority: HIGH** | **Effort: Low-Medium**

Detect missing CancellationToken in HttpClient method calls.

#### Examples to Detect:
```csharp
// ❌ Missing token
public async Task<string> FetchDataAsync(CancellationToken cancellationToken)
{
    return await _httpClient.GetStringAsync("https://api.example.com");
}

// ✅ Correct
public async Task<string> FetchDataAsync(CancellationToken cancellationToken)
{
    return await _httpClient.GetStringAsync("https://api.example.com", cancellationToken);
}
```

#### Key Methods to Monitor:
- `GetAsync`, `GetStringAsync`, `GetByteArrayAsync`, `GetStreamAsync`
- `PostAsync`, `PutAsync`, `DeleteAsync`, `PatchAsync`
- `SendAsync`

#### Implementation Steps (TDD):
1. Write tests for each HttpClient async method
2. Implement analyzer detecting HttpClient invocations
3. Write code fix tests
4. Implement code fix provider
5. Build and verify

---

### CC005: Handler Pattern Detection
**Priority: MEDIUM** | **Effort: Medium-High**

Detect missing CancellationToken in common handler patterns.

#### Patterns to Support:

**MediatR Handlers:**
```csharp
// ❌ Missing token
public class MyHandler : IRequestHandler<MyRequest, MyResponse>
{
    public async Task<MyResponse> Handle(MyRequest request)
    {
        // ...
    }
}

// ✅ Correct
public class MyHandler : IRequestHandler<MyRequest, MyResponse>
{
    public async Task<MyResponse> Handle(MyRequest request, CancellationToken cancellationToken)
    {
        // ...
    }
}
```

**ASP.NET Core Controllers:**
```csharp
// ❌ Missing token
[HttpGet]
public async Task<IActionResult> GetUsers()
{
    var users = await _service.GetUsersAsync();
    return Ok(users);
}

// ✅ Correct
[HttpGet]
public async Task<IActionResult> GetUsers(CancellationToken cancellationToken)
{
    var users = await _service.GetUsersAsync(cancellationToken);
    return Ok(users);
}
```

**Minimal APIs:**
```csharp
// ❌ Missing token
app.MapGet("/users", async () => await GetUsersAsync());

// ✅ Correct
app.MapGet("/users", async (CancellationToken ct) => await GetUsersAsync(ct));
```

#### Implementation Steps (TDD):
1. Write tests for MediatR `IRequestHandler` implementations
2. Write tests for Controller action methods with HTTP attributes
3. Write tests for Minimal API endpoints
4. Implement pattern-specific analyzers
5. Write code fix tests
6. Implement code fix providers
7. Build and verify

---

### CC006: Parameter Position Convention
**Priority: LOW** | **Effort: Low**

Suggest CancellationToken should be the last parameter (convention).

#### Example:
```csharp
// ⚠️ Not conventional
public async Task ProcessAsync(CancellationToken cancellationToken, string name)
{
    // ...
}

// ✅ Conventional
public async Task ProcessAsync(string name, CancellationToken cancellationToken)
{
    // ...
}
```

#### Implementation Steps (TDD):
1. Write tests detecting token not in last position
2. Implement analyzer checking parameter order
3. Write code fix tests (parameter reordering)
4. Implement code fix provider
5. Build and verify

**Note:** This should be configurable via severity (Info/Warning)

---

## 🚀 Future Enhancements (v1.2.0+)

### Enhanced Code Fixes
- **Auto-propagate to nested calls**: When adding token to method signature, automatically pass it to all inner async calls
- **Batch fix**: Apply fixes to entire projects/solutions
- **Smart parameter naming**: Detect existing parameter naming conventions (ct, token, cancellationToken)

### Configuration & Customization
- **Severity configuration**: Allow users to configure rule severities via .editorconfig
- **Exclude patterns**: Ability to exclude specific methods, types, or namespaces
- **Custom naming conventions**: Configure preferred parameter names
- **Framework-specific rules**: Enable/disable EF Core, HttpClient, MediatR rules individually

### Additional Rules
- **CC007**: Detect `CancellationToken.None` usage (suggest passing actual token)
- **CC008**: Detect unused CancellationToken parameters
- **CC009**: Suggest `ThrowIfCancellationRequested()` in long-running loops
- **CC010**: Detect async void methods (should return Task)

### Performance & Quality
- **Benchmark suite**: Measure analyzer performance on large codebases
- **Code coverage**: Achieve 90%+ test coverage
- **Documentation**: XML comments for all public APIs
- **Performance optimizations**: Reduce allocations, use object pools

---

## 📦 Infrastructure Improvements

### Release Management
- [ ] Add `AnalyzerReleases.Shipped.md` for released versions
- [ ] Add `AnalyzerReleases.Unshipped.md` for upcoming changes
- [ ] Fix RS2008 warning by adding release tracking files
- [ ] Add CHANGELOG.md for user-facing changes
- [ ] Semantic versioning documentation

### NuGet Package Enhancement
- [ ] Add package icon (icon.png)
- [ ] Add package README.md (displayed on NuGet.org)
- [ ] Add repository tags for better discoverability
- [ ] Add package release notes
- [ ] Fix RS1038 warning (separate analyzer/code fix assemblies)

### Documentation
- [ ] Add wiki with detailed rule explanations
- [ ] Add code samples for each rule
- [ ] Add configuration examples
- [ ] Add troubleshooting guide
- [ ] Create video walkthrough/demo

### Community & Contribution
- [ ] Add CONTRIBUTING.md with development guidelines
- [ ] Add CODE_OF_CONDUCT.md
- [ ] Add issue templates for bug reports and feature requests
- [ ] Add PR template
- [ ] Add contributor recognition (all-contributors)

### CI/CD Enhancements
- [ ] Add code coverage reporting (Coverlet + Codecov)
- [ ] Add automated release notes generation
- [ ] Add dependency update automation (Dependabot)
- [ ] Add preview package publishing to GitHub Packages
- [ ] Add benchmark comparison in PRs

---

## 🧪 Testing Strategy

### Test Categories to Add:
1. **Integration Tests**: Test analyzer in real-world scenarios
2. **Performance Tests**: Benchmark analyzer on large files
3. **Compatibility Tests**: Test against different .NET versions
4. **Regression Tests**: Ensure fixes don't break existing functionality

### Test Coverage Goals:
- Analyzer logic: 100%
- Code fix providers: 100%
- Edge cases: 95%+
- Error handling: 100%

---

## 📊 Metrics & Success Criteria

### v1.1.0 Goals:
- [ ] CC002-CC004 implemented with >95% test coverage
- [ ] All tests passing (green CI)
- [ ] NuGet package published
- [ ] Documentation updated
- [ ] Zero high-severity warnings in build

### v1.2.0 Goals:
- [ ] CC005-CC006 implemented
- [ ] 90%+ overall code coverage
- [ ] Performance benchmarks established
- [ ] Community contributions enabled

---

## 🔄 Development Workflow (TDD)

For each new rule:

1. **Red Phase**: Write failing tests
   - Analyzer tests (should detect violations)
   - Code fix tests (should fix violations)
   - Edge case tests (boundary conditions)

2. **Green Phase**: Implement minimum code to pass
   - DiagnosticAnalyzer implementation
   - CodeFixProvider implementation
   - Register in analyzer list

3. **Refactor Phase**: Improve code quality
   - Extract helper methods
   - Add XML documentation
   - Optimize performance
   - Add comments for complex logic

4. **Verify**: Build and test
   - Run all tests: `dotnet test`
   - Build solution: `dotnet build`
   - Test sample project shows expected warnings
   - Update documentation

---

## 📝 Notes

- Always maintain backward compatibility
- Follow Roslyn analyzer best practices
- Keep tests fast (< 2 seconds total)
- Document all public APIs
- Add samples for each new rule
- Consider performance impact of analyzers

---

**Last Updated**: 2025-10-01
**Current Version**: v1.0.0
**Next Planned Release**: v1.1.0 (CC002-CC004)
