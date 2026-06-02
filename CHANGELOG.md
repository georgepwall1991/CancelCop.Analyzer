# Changelog

All notable changes to CancelCop.Analyzer are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

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
  non-public methods, `static` methods, and `[NonAction]` methods.
- **CC006** no longer fires when the token cannot legally be moved last: when it sits
  immediately before a trailing `params` parameter, or when it is the `this` receiver of an
  extension method.
- CI now installs both the .NET 9 and .NET 10 SDKs and `global.json` is pinned to
  `10.0.300`, so the `net10.0` projects build deterministically in CI (was failing
  with NETSDK1045).
