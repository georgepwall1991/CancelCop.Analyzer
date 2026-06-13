# Changelog

All notable changes to CancelCop.Analyzer are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [1.9.0] - 2026-06-13

### Added

- **New rule CC014 — `CancellationTokenSource` should be disposed.** A `CancellationTokenSource`
  owns disposable resources (a timer and a wait handle). CC014 flags a local variable initialized
  with `new CancellationTokenSource(...)` or `CancellationTokenSource.CreateLinkedTokenSource(...)`
  that is not already a `using` declaration, is never disposed, and never escapes — it is not
  returned, assigned out, passed as an argument, or captured by a nested function. The code fix
  converts the declaration into a `using` declaration. Conservative escape analysis keeps false
  positives down: any path by which the source could be disposed elsewhere suppresses the
  diagnostic. Pinned by 9 new tests (7 analyzer, 2 fixer).

## [1.8.1] - 2026-06-13

### Fixed

- **CC010** now also flags an `await foreach` whose source is `source.ConfigureAwait(false)` without
  a `.WithCancellation(token)` — previously the configured-cancelable wrapper hid the missing token
  (a false negative). The analyzer peels trailing `.WithCancellation`/`.ConfigureAwait` calls off
  the source: a chain that already contains `.WithCancellation` stays quiet, while a `ConfigureAwait`-
  only chain is reported on the underlying enumerable, and the code fix inserts
  `.WithCancellation(token)` before the `.ConfigureAwait(...)`. Pinned by 2 new tests.

## [1.8.0] - 2026-06-13

### Added

- **New rule CC013 — avoid `Thread.Sleep` in async code.** `Thread.Sleep` blocks the calling
  thread (risking thread-pool starvation) and cannot be cancelled. CC013 flags a
  `System.Threading.Thread.Sleep` call lexically inside an `async` method, local function, lambda,
  or anonymous method, and the code fix rewrites it to `await Task.Delay(delay, token)` — flowing
  the in-scope token when one is available, otherwise `await Task.Delay(delay)`. The async-context
  check stops at the first function boundary, so a synchronous lambda inside an async method is not
  flagged. Pinned by 7 new tests (5 analyzer, 2 fixer).

## [1.7.0] - 2026-06-13

### Added

- **New rule CC012 — avoid passing `CancellationToken.None`/`default` when a token is in scope.**
  Passing `CancellationToken.None`, `default`, or `default(CancellationToken)` to a call explicitly
  opts it out of cancellation; when the surrounding scope already has a token, that is usually an
  oversight. CC012 flags the none-ish argument (binding to a `CancellationToken`) whenever an
  in-scope token parameter exists, and the code fix replaces it with that token. Reported as
  **Info** because the pattern is occasionally intentional (best-effort cleanup). Conservative: no
  diagnostic when no token is in scope or when the argument does not bind to a `CancellationToken`.
  Pinned by 8 new tests (6 analyzer, 2 fixer).

## [1.6.0] - 2026-06-13

### Added

- **New rule CC011 — async-iterator `CancellationToken` should be `[EnumeratorCancellation]`.**
  The producer-side complement to CC010: an `async IAsyncEnumerable<T>` iterator (method or local
  function with `yield`) that declares a `CancellationToken` parameter but does not mark any token
  parameter `[EnumeratorCancellation]` silently drops a token passed via `.WithCancellation(token)`
  — the parameter just receives `default`. CC011 flags the first unmarked token parameter; the code
  fix adds the `[EnumeratorCancellation]` attribute and the `System.Runtime.CompilerServices` import.
  Conservative gating: non-iterator methods that merely return an `IAsyncEnumerable<T>`, iterators
  with no token, and iterators where a token is already marked are all left alone. Pinned by 8 new
  tests (6 analyzer, 2 fixer). The using-insertion fixer helper was generalized from
  `AddSystemThreadingUsing` to a namespace-parameterized `AddUsing`.

## [1.5.0] - 2026-06-13

### Added

- **New rule CC010 — `await foreach` should flow a `CancellationToken`.** Consuming an
  `IAsyncEnumerable<T>` with `await foreach` can block indefinitely on the next element; CC010
  flags an `await foreach` whose source is (or implements) `IAsyncEnumerable<T>` when a token is in
  scope and the source neither already passes a token argument nor is wrapped in a configured
  cancelable enumerable (`.WithCancellation`/`.ConfigureAwait`). The code fix rewrites the source to
  `source.WithCancellation(token)`, routing the token to the producer's `[EnumeratorCancellation]`
  parameter. Conservative by design: no token in scope, a synchronous `foreach`, or a producer call
  that already receives a token are all left alone. Pinned by 9 new tests (7 analyzer, 2 fixer).

## [1.4.8] - 2026-06-09

### Added

- **Rule-catalog trust contract** (`RuleCatalogTests`): drift-guard tests asserting that every
  descriptor a shipped analyzer registers has a README rule-table row with the correct severity
  and fix mark, is tracked in `AnalyzerReleases.Shipped.md` with the correct severity, and that
  every exported code-fix provider targets a shipped rule. A rule can no longer be added, renamed,
  or re-severitied without the public docs following.

## [1.4.7] - 2026-06-09

### Changed

- **Internal refactor, no behavior change.** The triplicated tail of CC002/CC003/CC004
  (token-argument check → scope walk → expression-tree guard → overload check → diagnostic
  construction) is now a single `CancellationTokenHelpers.ReportIfTokenNotPropagated`. Each
  analyzer is reduced to its rule-specific gating plus one call, eliminating the three-way drift
  class that earlier hardening loops repeatedly had to re-synchronise. All 200 tests pass
  unchanged.

## [1.4.6] - 2026-06-09

### Fixed

- The **CC002/CC003/CC004 code fixes** no longer produce non-compiling calls when the original
  invocation uses named arguments. Appending a positional token after an out-of-position named
  argument is CS8323; the fixes now append a **named token argument** using the target overload's
  parameter name whenever any existing argument is named:
  ```csharp
  await client.PostAsync(content: body, requestUri: url);
  // fix now produces:
  await client.PostAsync(content: body, requestUri: url, cancellationToken: ct);
  ```
  The analyzers carry the overload's token parameter name in new `TokenArgumentName` diagnostic
  metadata, so the fixers stay purely syntactic. Plain positional calls keep positional fixes.

## [1.4.5] - 2026-06-09

### Fixed

- The shared token-scope walk (CC002/CC003/CC004/CC009) now finds `CancellationToken` parameters
  declared on **constructors** and **C# 12 primary constructors** (classes and records). Previously
  the walk only terminated at method declarations, so all four rules were silent in these scopes:
  ```csharp
  public class Worker(CancellationToken cancellationToken)
  {
      public Task RunAsync() => Task.Delay(100);   // CC002: should pass cancellationToken
  }

  public TestClass(CancellationToken cancellationToken)
  {
      _init = Task.Delay(100);                     // CC002: should pass cancellationToken
  }
  ```
  Primary-constructor tokens are also found from instance field initializers. The walk stays
  conservative where capture is illegal: static members, static field initializers, and non-primary
  constructor bodies (CS9105) never see the primary-constructor token, and operators end the search.

### Review hardening (caught before release)

- **Static event-field initializers** get the same CS9105 guard as static fields (the walk now
  matches `BaseFieldDeclarationSyntax`, covering `event` fields), so a lambda in a static event
  initializer can no longer be told to capture an uncapturable primary-constructor token.
- **Partial types** whose primary constructor is declared on another part are now resolved through
  the type symbol, so instance members on any part see the token (capture across parts is legal).
- CC002 and CC009 XML-doc scope descriptions updated to match the widened walk.

## [1.4.4] - 2026-06-09

### Added

- **CC005C** (`MinimalApiAnalyzer`) now analyses **method-group handlers**, not just lambdas:
  `app.MapGet("/users", GetUsersAsync)`, `app.MapGet("/users", UserHandlers.Get)`, and local-function
  method groups are resolved to the referenced method, which is flagged when it is async-shaped
  (`async` or Task/ValueTask-returning) without a `CancellationToken` parameter. Synchronous
  handlers, delegate-typed variables, ambiguous method groups, and externally-controlled signatures
  (override/interface/extern) stay quiet.
- The CC005C **code fix** follows: for a method-group diagnostic it adds
  `CancellationToken cancellationToken = default` to the referenced method or local function
  (`= default` keeps any other call sites compiling). Only same-document declarations are rewritten;
  a handler defined in another file keeps the diagnostic but gets no automatic fix.

### Fixed

- The CC005C lambda code fix now matches the diagnostic span exactly, so a CC005C diagnostic can
  never be "fixed" by adding a token parameter to an unrelated enclosing lambda (e.g. a registration
  lambda wrapping the `MapGet` call).

### Review hardening (caught before release)

- `handler.Invoke` member access resolves to the delegate type's `Invoke` method and is never
  flagged — the developer cannot change that signature.
- Handlers defined in another assembly (metadata) are never flagged — no editable signature exists
  in the solution.
- Parenthesized method groups (`(Handler)`) and generic method groups (`Handler<T>`) no longer
  evade analysis.
- The fix is withheld for **virtual/abstract** handlers (rewriting the base would orphan overrides,
  CS0115) and **partial** methods (both parts must keep matching signatures, CS8795); the
  diagnostic still reports for manual action.
- Fix All on two routes sharing one handler adds the token parameter exactly once (pinned by test).
- The rewritten declaration now carries the formatter annotation, matching the CC001 fix.

## [1.4.3] - 2026-06-09

### Fixed

- **CC003** (`EFCoreAnalyzer`) and **CC004** (`HttpClientAnalyzer`) now find the available
  `CancellationToken` with the same scope walk as CC002/CC009: the nearest enclosing **local
  function, lambda, or method** that declares (or captures) a token parameter. Previously both
  rules only looked at the containing method declaration
  (`FirstAncestorOrSelf<MethodDeclarationSyntax>`), so an EF Core or `HttpClient` call inside a
  local function or lambda that owned its own token was silently missed. Examples now caught:
  ```csharp
  async Task<int> CountUsersAsync(CancellationToken ct)   // local function
  {
      return await query.CountAsync();                    // CC003: should pass ct
  }

  Func<CancellationToken, Task<string>> fetch = async ct =>
      await httpClient.GetStringAsync(url);               // CC004: should pass ct
  ```
- Both rules also gained CC002's **expression-tree guard**: a matching call inside an
  `Expression<TDelegate>` lambda is data, not executable code, so it is never flagged (the token
  could not be propagated there, and the fix would not compile).
- The shared scope walk (CC002/CC003/CC004/CC009) now stops at a **`static` lambda or static local
  function** that has no token of its own: a static anonymous function cannot capture the enclosing
  method's token (CS8820/CS8421), so suggesting it was a false positive whose code fix did not
  compile. Surfaced during review of this release.
- The shared scope walk now also recognises **anonymous methods** (`async delegate (CancellationToken ct)
  { … }`), which declare parameters just like lambdas but were previously invisible — a silent false
  negative for all four propagation rules.

### Changed

- All four token-propagation rules (CC002, CC003, CC004, CC009) now share the single
  `CancellationTokenHelpers.FindEnclosingCancellationTokenParameter` scope walk, closing the
  P1 "scope consistency" item from `docs/ANALYZER_HEALTH.md`.

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
