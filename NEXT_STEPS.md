# CancelCop - Next Steps & Roadmap

## ✅ Completed (v1.1.0)

- [x] **CC001**: Public/protected async methods must have CancellationToken parameter
  - Analyzer implementation
  - Code fix provider with smart using directive handling
  - Comprehensive test coverage (10/10 passing)
- [x] **CC002**: CancellationToken Propagation Detection
  - Detects missing tokens in Task.Delay, Task.Run, custom async calls
  - Code fix provider auto-adds token parameter (21 tests passing)
- [x] **CC003**: EF Core Query Detection
  - Detects missing tokens in ToListAsync, FirstOrDefaultAsync, SaveChangesAsync, etc.
  - Code fix provider for EF Core methods (11 tests passing)
- [x] **CC004**: HttpClient Methods
  - Detects missing tokens in GetAsync, PostAsync, SendAsync, etc.
  - Code fix provider for HttpClient calls (13 tests passing)
- [x] **CC005A**: MediatR Handler Detection
  - Detects missing CancellationToken in IRequestHandler.Handle methods
  - Code fix provider adds token parameter
- [x] **CC005B**: Controller Action Detection
  - Detects missing tokens in ASP.NET Core controller actions with HTTP attributes
  - Code fix provider adds token parameter (8 tests passing)
- [x] **CC005C**: Minimal API Detection
  - Detects missing tokens in MapGet, MapPost, MapPut, MapDelete, MapPatch lambdas
  - Code fix provider for lambda expressions (11 tests passing)
- [x] **CC006**: Parameter Position Convention
  - Detects when CancellationToken is not the last parameter (Info severity)
  - Analyzer-only (no code fix for parameter reordering yet) (7 tests passing)
- [x] Project structure with src/tests/samples folders
- [x] TDD infrastructure with XUnit and Roslyn testing framework (111 tests passing)
- [x] NuGet package configuration
- [x] GitHub Actions CI/CD workflows
- [x] Documentation (README, .editorconfig, Directory.Build.props, AnalyzerReleases)
- [x] Release tracking files (AnalyzerReleases.Shipped.md, .Unshipped.md)

---

## 🎯 Next Steps (v1.2.0+)

### CC006 Code Fix: Parameter Reordering
**Priority: MEDIUM** | **Effort: HIGH**

Add code fix provider for CC006 to automatically reorder parameters so CancellationToken is last.

#### Challenges:
- Must update all call sites when reordering parameters
- Requires semantic analysis to find all references
- May affect many files in large codebases

#### Implementation Steps:
1. Write tests for parameter reordering code fix
2. Implement code fix provider using Roslyn rename/refactoring APIs
3. Handle edge cases (overloads, virtual methods, interface implementations)
4. Build and verify

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
- **CC010**: Detect async void methods (should return Task)

### Performance & Quality
- **Benchmark suite**: Measure analyzer performance on large codebases
- **Code coverage**: Achieve 90%+ test coverage
- **Documentation**: XML comments for all public APIs
- **Performance optimizations**: Reduce allocations, use object pools

---

## 📦 Infrastructure Improvements

### Release Management
- [x] Add `AnalyzerReleases.Shipped.md` for released versions
- [x] Add `AnalyzerReleases.Unshipped.md` for upcoming changes
- [x] Fix RS2008 warning by adding release tracking files
- [ ] Add CHANGELOG.md for user-facing changes
- [ ] Semantic versioning documentation

### NuGet Package Enhancement
- [x] Add package icon (icon.png)
- [x] Add package README.md (displayed on NuGet.org)
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
- [x] Add CONTRIBUTING.md with development guidelines
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

## ✅ Completed (v1.2.0)

- [x] **ValueTask Support**: CC001 and CC005B now detect `ValueTask` and `ValueTask<T>` return types
- [x] **Local Function Support**: CC002 now detects token propagation issues in local functions
- [x] **ControllerBase Namespace Verification**: CC005B only triggers for `Microsoft.AspNetCore.Mvc.ControllerBase`
- [x] **Code Quality**: Extracted shared `CancellationTokenHelpers` class to reduce duplication
- [x] **Test Coverage**: Increased from 82 to 92 tests

---

## ✅ Completed (v1.3.0)

- [x] **CC009**: Loop Cancellation Check Analyzer
  - Detects `for`, `foreach`, `while`, and `do-while` loops without cancellation checks
  - Only triggers in methods with a CancellationToken parameter
  - Code fix adds `{tokenName}.ThrowIfCancellationRequested();` as first statement in loop
  - Supports local functions with CancellationToken parameters
  - 19 new tests (13 analyzer + 6 code fix)
- [x] **Improved Samples**: Reorganized sample project with separate files per rule
  - CC001_PublicAsyncMethods.cs - CC009_LoopCancellation.cs
  - Detailed comments explaining why each rule matters
  - Both violation examples and correct patterns
- [x] **Documentation Improvements**
  - Comprehensive README with all 9 rules documented
  - Updated CONTRIBUTING.md with detailed guidelines
  - XML documentation on key analyzers
  - NuGet README updated with CC009
- [x] **Test Coverage**: Increased from 92 to 111 tests

---

**Last Updated**: 2025-12-14
**Current Version**: v1.3.0
**Next Planned Release**: v1.4.0 (Additional rules and enhancements)
