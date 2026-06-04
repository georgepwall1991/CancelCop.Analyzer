# Changelog

All notable changes to CancelCop.Analyzer are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [1.4.2] - 2026-06-04

### Fixed

- **CC002** (`TokenPropagationAnalyzer`) now detects missing token propagation inside **lambda
  expressions**, not just methods and local functions — closing a false negative where the analyzer's
  own documentation already promised lambda support. A `Task.Delay(…)`/custom async call inside an
  async lambda that owns a `CancellationToken` parameter (or captures one from an enclosing scope) is
  now flagged. Example now caught:
  ```csharp
  Func<CancellationToken, Task> handler = async (CancellationToken ct) =>
  {
      await Task.Delay(100); // CC002: should pass ct
  };
  ```
  The lambda walk deliberately excludes **expression-tree lambdas** (`Expression<TDelegate>`, e.g. an
  `IQueryable`/EF predicate): code there is data, not executed, so a token cannot be propagated into it
  (and an expression tree may not contain such a call anyway, CS0853/CS0854).

### Changed

- **Token-scope walk unified.** CC002 and CC009 previously each carried a near-identical private
  "walk up to the nearest enclosing scope that declares a `CancellationToken`" routine; the
  lambda-aware version is now a single shared `CancellationTokenHelpers.FindEnclosingCancellationTokenParameter`.
  This removed the duplication and is what gives CC002 its lambda support. CC009's behavior is
  unchanged (its 19 tests still pass).

## [1.4.1] - 2026-06-04

### Changed

- **CC005C** (`MinimalApiAnalyzer`) now confirms the call targets an ASP.NET Core endpoint route
  builder before firing: it checks that the receiver implements
  `Microsoft.AspNetCore.Routing.IEndpointRouteBuilder` instead of matching the method name
  (`MapGet`/`MapPost`/`MapPut`/`MapDelete`/`MapPatch`) alone. A user-defined method or extension that
  merely shares one of those names — e.g. `customRouteTable.MapGet(pattern, async () => …)` — no longer
  produces a false positive. This closes the last name-only match in the CC005 family (CC005B was given
  framework-identity gating in v1.4.0). The receiver type is resolved from the invocation's receiver
  expression, so the check still fires when an untyped handler lambda leaves the `MapXxx` overload
  itself unbound. New negative tests cover both extension-method and instance-method `MapGet`
  lookalikes.

### Added

- **`docs/ANALYZER_HEALTH.md`:** a rule-by-rule health scorecard (analyzer depth, false positives, fix
  strategy, tests, docs, importance) and a prioritized hardening backlog, refreshed each hardening loop.

## [1.4.0] - 2026-06-02

### Fixed

- **CC005C code fix** (`MinimalApiCodeFixProvider`) now produces compilable output:
  - adds `using System.Threading;` when missing (was emitting CS0246);
  - picks a non-colliding parameter name (was emitting CS0100);
  - no longer rewrites a simple (untyped) lambda into a typed/untyped mix (CS0748) —
    such a lambda is not a bindable minimal-API handler, so no fix is offered instead.
- **Token name selection** (shared by the CC001 and CC005 code fixes) now avoids names
  declared as locals in the target method/lambda body, not just parameter names, so the
  injected token cannot shadow a body local (CS0136).
- **CC005A/CC005B code fix** (`HandlerPatternCodeFixProvider`) now produces compilable
  output for MediatR handlers and controller actions:
  - adds `using System.Threading;` when it is missing (was emitting CS0246);
  - gives the token `= default` when it would otherwise follow an optional parameter
    (was emitting CS1737);
  - picks a non-colliding parameter name and inserts before a trailing `params`.
  These cases are covered by new tests that compile the fixed output (no
  `CompilerDiagnostics.None`).
- **Using insertion** (shared by the CC001 and CC005 code fixes) now inserts
  `using System.Threading;` after any `global using` block instead of before it
  (which would otherwise produce CS8915), and no longer treats an alias or static
  using of `System.Threading` as satisfying the namespace import (which would have
  left the unqualified `CancellationToken` unresolved, CS0246).
- **CC001 code fix** now produces compilable, clean output in three previously broken cases:
  - inserts the `CancellationToken` parameter *before* a trailing `params` parameter
    instead of after it (was emitting CS0231);
  - picks a non-colliding parameter name (`ct`, then `cancellationToken2`, …) when a
    parameter named `cancellationToken` already exists (was emitting CS0100);
  - inserts `using System.Threading;` without leaving a spurious blank line between
    using directives (the malformed output was previously baked into the tests).

### Added

- **CI code coverage:** the test step now collects Cobertura coverage (`coverlet.collector`)
  and uploads it as a build artifact.
- **Dependabot:** weekly update checks for NuGet (grouped `Microsoft.CodeAnalysis.*` and test
  packages) and GitHub Actions.
- **CI `.nupkg` consumer smoke test:** CI now packs the package, asserts its layout
  (`analyzers/dotnet/cs/*.dll` present, no `<dependencies>`), builds a throwaway consumer
  against it, and fails if a known diagnostic (CC001) does not fire — catching packaging
  regressions before release.

### Changed

- **Reproducible builds:** enabled `Deterministic`, `ContinuousIntegrationBuild` (on CI),
  `PublishRepositoryUrl`, `EmbedUntrackedSources`, and `Microsoft.SourceLink.GitHub` so the
  published package is deterministic and source-debuggable.
- **Analyzer release tracking:** `AnalyzerReleases.Shipped.md` / `AnalyzerReleases.Unshipped.md`
  are now `AdditionalFiles`, re-enabling RS2008, and the analyzer/code-fix projects treat the
  analyzer-authoring rules (RS1038, RS2008, RS1036, RS1041) as errors so packaging/release-tracking
  regressions fail the build.
- **Roslyn compatibility:** lowered the compile-time floor from `Microsoft.CodeAnalysis.*`
  4.14.0 to **4.8.0** (VS 17.8 / .NET 8 SDK) for both the analyzer and code-fix assemblies,
  widening the range of hosts the package loads in. Consumers on newer Roslyn are unaffected.
- **Packaging:** split into an analyzer assembly (`CancelCop.Analyzer`, no
  `Microsoft.CodeAnalysis.Workspaces` reference) and a code-fix assembly
  (`CancelCop.Analyzer.CodeFixes`), packed together into `analyzers/dotnet/cs/` by a
  dedicated `CancelCop.Analyzer.Package` host. This clears the **RS1038** warning on all
  nine analyzers and removes the fragile `$(OutputPath)` packaging in favour of
  `TargetsForTfmSpecificContentInPackage`. The published package id, layout, and empty
  dependency set are unchanged.
- **CC009** now decides whether a loop is cancellation-checked by resolving the receiver
  symbol through the semantic model and comparing it to the in-scope token, instead of
  substring-matching its name. A look-alike named `…token` no longer satisfies the check (was
  a false negative), a real `CancellationToken` with a plain name now does (was a false
  positive), and a check on a *different* token no longer suppresses the diagnostic for the
  reported token. The `IsCancellationTokenExpression` heuristic is removed.
- **CC001** and **CC006** no longer fire on methods whose signature is dictated by a base
  type or interface — `override` methods, explicit/implicit interface implementations, and
  `extern` methods. The previously offered fixes broke compilation (CS0115/CS0535) on these,
  so this removes the highest-noise false-positive category (mirrors CA1068's exceptions).
- **CC005B** (controller actions) no longer fires on methods that are not routable actions:
  non-public methods, `static` methods, and `[NonAction]` methods. HTTP-method attributes are
  now matched by `Microsoft.AspNetCore.Mvc` identity (including subclasses), so a user-defined
  `HttpGetAttribute` no longer triggers a false positive and a subclass of a framework attribute
  is now correctly recognized.
- **CC006** no longer fires when the token cannot legally be moved last: when it sits
  immediately before a trailing `params` parameter, or when it is the `this` receiver of an
  extension method. It now also checks **constructors** and **local functions** (previously a
  false negative — only methods were analyzed).
- CI now installs both the .NET 9 and .NET 10 SDKs and `global.json` is pinned to
  `10.0.300`, so the `net10.0` projects build deterministically in CI (was failing
  with NETSDK1045).
