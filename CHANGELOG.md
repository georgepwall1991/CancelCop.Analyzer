# Changelog

All notable changes to CancelCop.Analyzer are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [1.27.1] - 2026-06-14

### Fixed

- **CC002 / CC003 / CC004** no longer fire (with a non-compiling fix) when the only
  `CancellationToken`-accepting overload has incompatible parameters. Firing now requires an overload
  whose non-token parameters match the call's *by type*, so e.g. `await writer.WriteAsync(text)` —
  whose token overloads take `ReadOnlyMemory<char>`/`StringBuilder`, not `string` — is left alone
  instead of being "fixed" to the invalid `WriteAsync(text, cancellationToken)`. (The named-argument
  lookup keeps its count/first-overload fallback; only the firing gate is tightened.)

### Tests / Samples

- Added a clean-code FP guard pinning that idiomatic async `StreamWriter` usage
  (`await writer.WriteAsync(text)` + `await writer.FlushAsync(cancellationToken)`) produces zero
  diagnostics across all analyzers — the shape CC028 (v1.27.0) steers toward.
- Extended the CC028 sample with a `StreamWriter.Write`/`Flush` violation and its `WriteAsync`/
  `FlushAsync` fix, demonstrating the new write-side coverage.

## [1.27.0] - 2026-06-14

### Added

- **CC028** now also flags the write side of `System.IO`: `StreamWriter.Write` / `WriteLine` / `Flush`
  in async code (the symmetric complement to the already-covered `StreamReader.ReadToEnd`/`ReadLine`).

### Changed

- **CC028** detection is hardened: instead of a name-only `<name>Async` lookup, it now requires a
  *signature-compatible* async counterpart — an overload whose parameters equal the blocking call's,
  optionally plus one trailing `CancellationToken`. This guarantees the rewrite always compiles
  (e.g. `StreamWriter.Write(bool)`, which has no async form, is no longer a candidate), and the fixer
  only flows the in-scope token when the matched overload actually accepts one (so
  `StreamWriter.Write(string)` becomes `await writer.WriteAsync(text)` with no spurious token, while
  `Flush()` becomes `await writer.FlushAsync(cancellationToken)`).

## [1.26.9] - 2026-06-14

### Fixed

- Resolved three `CS1574` build warnings: the class-doc `<see cref="…"/>` references to
  `IAsyncEnumerable<T>` (CC010/CC011) and `IAsyncDisposable` (CC025) could not be resolved under the
  `netstandard2.0` target. Converted them to `<c>…</c>` code formatting, matching how the other type
  names in those doc blocks are written. The analyzer assembly now builds warning-free.

## [1.26.8] - 2026-06-14

### Tests

- Pinned CC028 firing inside an async local function (nested async context), completing the
  async-context coverage for the rule.

## [1.26.7] - 2026-06-14

### Tests

- Added a mixed-type CC028 Fix All test (a `File` helper and a `StreamReader.ReadToEnd()` rewritten in
  one batch), confirming the batch fixer spans the generalized `System.IO` type map.

## [1.26.6] - 2026-06-14

### Tests

- Pinned the remaining receiver shapes of the CC028 fix parenthesization: element access
  (`File.ReadAllLines(p)[0]`) and conditional access (`reader.ReadLine()?.Trim()`) both wrap the
  inserted `await` correctly.

## [1.26.5] - 2026-06-14

### Fixed

- CC028's code fix now parenthesizes the inserted `await` when the blocking call is the receiver of a
  further access, e.g. `File.ReadAllText(p).Trim()` → `(await File.ReadAllTextAsync(p, token)).Trim()`.
  Previously it produced `await File.ReadAllTextAsync(p).Trim()`, which binds the `await` to the whole
  chain and does not compile (`.Trim()` is on `Task<string>`). Mirrors the existing CC015 handling.

## [1.26.4] - 2026-06-14

### Docs

- Completed the CC028 sample (`CC028_BlockingFileIo.cs`) with a `StreamReader.ReadToEnd()` →
  `await reader.ReadToEndAsync(token)` before/after, matching the rule's broadened scope.

## [1.26.3] - 2026-06-14

### Tests

- Pinned two CC028 negatives for the `StreamReader` branch: a non-curated method (`Peek()`) and a
  user-defined `StreamReader` outside `System.IO` both stay clean (curated-set + namespace guards).

## [1.26.2] - 2026-06-14

### Tests

- Broadened CC028 fix coverage across the curated method map: `StreamReader.ReadLine()` →
  `await reader.ReadLineAsync(token)` and `File.AppendAllText(...)` → `await File.AppendAllTextAsync(..., token)`.

## [1.26.1] - 2026-06-14

### Docs

- Refreshed the rule count to **28** (CC001–CC006, CC009–CC028) across the README Roadmap, the
  health-doc scorecard, and `NEXT_STEPS.md`, and listed CC028 under the blocking-sync-over-async family.

## [1.26.0] - 2026-06-14

### Changed

- **CC028** now also flags blocking `StreamReader.ReadToEnd()` and `StreamReader.ReadLine()` (in
  addition to the `System.IO.File` read/write/append helpers), rewriting them to
  `await reader.ReadToEndAsync(token)` / `await reader.ReadLineAsync(token)`. The diagnostic message
  generalised from `File.<name>` to `<name>`; the rule remains Warning with a code fix.

## [1.25.2] - 2026-06-14

### Tests

- Added a CC028 false-positive guard to the cross-analyzer clean-code suite: idiomatic async File I/O
  (`File.ReadAllTextAsync` / `File.WriteAllTextAsync` flowing the in-scope token) produces zero
  diagnostics across all 28 analyzers.

## [1.25.1] - 2026-06-14

### Fixed

- CC028's code fix is now named-argument-safe: when the original `File.<name>(...)` call uses a named
  argument, the token is added as `cancellationToken: token` (via the shared `AddTokenArgument` helper)
  rather than appended positionally, which would have produced invalid code (CS8323).

## [1.25.0] - 2026-06-14

### Added

- **CC028 code fix**: rewrites a blocking `File.<name>(...)` call to `await File.<name>Async(..., token)`,
  flowing the in-scope `CancellationToken` when one is available (and falling back to the token-less
  async overload otherwise). Fix All supported. CC028's README fix mark is now ✅.

## [1.24.0] - 2026-06-14

### Added

- **CC028** (Warning): flags a blocking synchronous `System.IO.File` call (`ReadAllText`,
  `ReadAllBytes`, `ReadAllLines`, `WriteAllText`, `WriteAllBytes`, `WriteAllLines`, `AppendAllText`,
  `AppendAllLines`) made inside async code when an `<name>Async` counterpart exists. This rounds out
  the blocking-in-async family alongside CC013 (`Thread.Sleep`), CC015 (`Task.Wait`/`.Result`) and
  CC026 (`SemaphoreSlim.Wait`). Analyzer-only; the async counterpart also accepts a `CancellationToken`.
  Includes README rule-table row + Quick Example and a sample (`CC028_BlockingFileIo.cs`).

## [1.23.45] - 2026-06-14

### Tests

- Pinned CC013's fix on a fully-qualified `System.Threading.Thread.Sleep(1000)` — flagged by symbol
  and rewritten to `await Task.Delay(1000, ct)` regardless of how the receiver is spelled.

## [1.23.44] - 2026-06-14

### Tests

- Pinned CC012's fix on a named argument: `DoAsync(token: CancellationToken.None)` rewrites to
  `DoAsync(token: cancellationToken)`, confirming the `token:` name-colon survives (the fix replaces
  only the expression node, never the whole argument).

## [1.23.43] - 2026-06-14

### Tests

- Pinned two CC001 surface-area behaviors: an `internal` async method stays clean (accessibility FP
  guard — only the public/protected surface is flagged), and a public async method on a `record` is
  flagged (type-kind-agnostic positive case).

## [1.23.42] - 2026-06-14

### Tests

- Pinned CC015 fix correctness when the blocking access is itself a receiver: `GetAsync().Result.ToString()`
  and `GetAsync().GetAwaiter().GetResult().ToString()` both rewrite to `(await GetAsync()).ToString()`,
  confirming the parenthesized `await` binds before the trailing member access.

## [1.23.41] - 2026-06-14

### Tests

- Pinned receiver-agnosticism for the blocking/lifecycle rules: CC015 flags `.Result` on a Task-typed
  field, CC022 flags `Cancel()` on a `CancellationTokenSource` field, and CC026's fix carries a field
  receiver (`_gate.Wait()` → `await _gate.WaitAsync(ct)`). Guards against a future regression that
  only inspects parameter receivers.

## [1.23.40] - 2026-06-14

### Tests

- Added two false-positive guard scenarios for modern C# shapes: primary-constructor classes/records
  with a file-scoped namespace (token captured from a primary constructor and propagated through an
  expression-bodied async method), and pattern matching / generics (a `switch` with awaited arms, a
  generic async method, and a `when (ex is not OperationCanceledException)` catch filter). All 27
  analyzers stay silent — confirming the scope walk and rule gating handle these shapes.

## [1.23.39] - 2026-06-14

### Tests

- Added Fix All correctness tests for the two line-inserting fixers, completing Fix All coverage for
  every fixer in the analyzer: CC009 (two `foreach` loops both get a `ThrowIfCancellationRequested()`
  guard line) and CC019 (two `catch` blocks both get an `is OperationCanceledException` rethrow guard).
  Confirms the inserted-line indentation and ordering stay correct under a batch fix.

## [1.23.38] - 2026-06-14

### Tests

- Added Fix All correctness tests for the add-token handler fixers across all three handler families:
  CC005B (two controller actions), CC005C (two Minimal API endpoint lambdas), and CC018 (two SignalR
  Hub methods). Confirms `HandlerPatternCodeFixProvider` and `MinimalApiCodeFixProvider` insert the
  token parameter at every flagged site when run as a batch fix.

## [1.23.37] - 2026-06-14

### Tests

- Added Fix All correctness tests for the three token-propagation fixers: CC002 (two `Task.Delay`
  calls both receive the in-scope token), CC003 (two EF Core `CountAsync` queries), and CC004 (two
  `HttpClient.GetStringAsync` calls). Confirms the shared `ReportIfTokenNotPropagated` fixer appends
  the token argument correctly when applied across multiple sites at once.

## [1.23.36] - 2026-06-14

### Tests

- Added Fix All correctness tests for three more fixers: CC015 (two `.Result` both become `(await ...)`),
  CC026 (two `SemaphoreSlim.Wait()` both become `await WaitAsync(ct)`), and CC001 (two token-less public
  async methods both gain a `CancellationToken` parameter while the `System.Threading` import is added
  exactly once).

## [1.23.35] - 2026-06-14

### Tests

- Added Fix All correctness tests for three in-place fixers: CC010 (two `await foreach` sources both
  wrapped in `.WithCancellation`), CC022 (two `Cancel()` both become `await CancelAsync()`), and
  CC025 (two `using` both become `await using`).

## [1.23.34] - 2026-06-14

### Tests

- Added Fix All correctness tests for three more fixers: CC014 (two undisposed sources both become
  `using`), CC023 (two `async void` methods both become `Task`, import added once), and CC012 (two
  none-ish arguments both replaced with the in-scope token).

## [1.23.33] - 2026-06-14

### Tests

- Pinned that CC011's Fix All across two async iterators adds the
  `System.Runtime.CompilerServices` import exactly once (no duplicate using). Also pinned that CC013
  and CC015 flag the `TimeSpan` overloads (`Thread.Sleep(TimeSpan)`, `task.Wait(TimeSpan)`).

## [1.23.32] - 2026-06-14

### Tests

- Pinned three more edge cases: CC011 does not flag an outer method whose only `yield` belongs to a
  nested local-function iterator; CC027 does not flag a `using (expr)` statement with no declared
  variable; CC009 fires for a loop inside a lambda that captures the enclosing token.

## [1.23.31] - 2026-06-14

### Tests

- Pinned three more edge cases: CC021 stays quiet when `RequestAborted` is observed via a local
  alias; CC017 treats a stopping token passed to a constructor as observed; CC027 documents its
  precision boundary (an aliased task local is not flagged — only the direct-receiver case is).

## [1.23.30] - 2026-06-14

### Tests

- Pinned three framework-rule edge cases: CC017 flags an expression-bodied `ExecuteAsync` that
  ignores its stopping token; CC020 stays quiet when the token is observed via a local alias
  (`var token = context.CancellationToken;`); CC018 does not flag a `static` hub method.

## [1.23.29] - 2026-06-14

### Tests

- Pinned the blocking rules across every async function kind: CC013 fires in an `async delegate`
  anonymous method, and CC015/CC026 fire inside an `async` local function (exercising the shared
  `IsInAsyncFunction` for methods, local functions, lambdas, and anonymous methods).

## [1.23.28] - 2026-06-14

### Tests

- Pinned three more edge cases: CC015 flags `ValueTask<T>.GetAwaiter().GetResult()`; CC024 flags an
  async lambda assigned to a generic `Action<T>`; CC010 fires for an `await foreach` inside a local
  function that captures the enclosing token.

## [1.23.27] - 2026-06-14

### Tests

- Pinned three more edge cases: CC016 treats a token passed to a constructor as used; CC019 stays
  quiet when the catch rethrows via `throw ex;`; CC012 flags `new Worker(CancellationToken.None)`
  (explicit object creation) when a token is in scope.

## [1.23.26] - 2026-06-14

### Tests

- Pinned three more edge cases: CC022 and CC026 fire inside an `async` lambda (not just methods), and
  CC023 flags a `protected async void` method (not only `public`).

## [1.23.25] - 2026-06-14

### Tests

- Pinned three edge cases: CC013 flags a static-imported `Sleep(...)` (symbol-resolved, not name-only
  on a member access); CC015 flags `ValueTask<T>.Result`; CC014 flags a target-typed
  `CancellationTokenSource cts = new();` that is never disposed.

## [1.23.24] - 2026-06-14

### Tests

- Pinned that CC012 flags a **named** none-ish argument (`DoAsync(token: default)`) when a token is
  in scope, not just positional ones.

## [1.23.23] - 2026-06-14

### Documentation

- Added a `samples/CancelCop.Sample` file for CC027 (premature disposal), completing sample parity
  for the non-framework rules. A clean sample build confirms it fires on the violation.

## [1.23.22] - 2026-06-14

### Documentation

- Added the missing CC027 "Quick Examples" section to the packaged README, restoring per-rule
  example parity (all 27 rules now have a violation-and-fix snippet).

## [1.23.21] - 2026-06-14

### Tests

- Pinned a CC005A non-false-positive: a class that merely has a method named `Handle` (not a
  `MediatR.IRequestHandler` implementation) is not flagged.

## [1.23.20] - 2026-06-14

### Tests

- Added a resource-lifecycle case to the cross-analyzer FP guard: a linked `CancellationTokenSource`
  disposed via `using`, an `await using` async-disposable, and `await CancelAsync()` together produce
  zero diagnostics (CC014/CC022/CC025 stay quiet on idiomatic resource management).

## [1.23.19] - 2026-06-14

### Tests

- Pinned the CC005C → CC002 guided sequence with a combined test: a tokenless Minimal API handler
  lambda reports **only** CC005C (the handler needs a token); CC002 does not fire until the CC005C
  fix has added the parameter. Closes the long-standing "combined-analyzer test" backlog item.

## [1.23.18] - 2026-06-14

### Added

- `helpLinkUri` extended to CC022–CC027 — **every rule now carries a help link**. Added a
  `RuleCatalogTests` drift guard (`EveryShippedRule_HasAHelpLink`) so any future rule without a
  `helpLinkUri` fails the build.

## [1.23.17] - 2026-06-14

### Added

- `helpLinkUri` extended to CC016–CC021 (continuing the rollout).

## [1.23.16] - 2026-06-14

### Added

- `helpLinkUri` extended to CC010–CC015 (continuing the rollout).

## [1.23.15] - 2026-06-14

### Added

- `helpLinkUri` extended to CC005A, CC005B, CC005C, CC006, and CC009 (continuing the rollout begun
  in v1.23.14).

## [1.23.14] - 2026-06-14

### Added

- Diagnostics now carry a `helpLinkUri` so IDEs (Visual Studio, Rider) show a clickable "learn more"
  link on each diagnostic that opens the rule documentation. Applied to CC001–CC004 in this release;
  the remaining rules follow in subsequent patches.

## [1.23.13] - 2026-06-14

### Tests

- Pinned a CC004 non-false-positive: a user-defined `GetAsync` on a type outside
  `System.Net.Http.HttpClient` is not flagged (the rule is type-gated).

## [1.23.12] - 2026-06-14

### Tests

- Pinned a CC003 non-false-positive: a user-defined `ToListAsync` on a type outside
  `Microsoft.EntityFrameworkCore` is not flagged as an EF Core call (the rule is namespace-gated).

## [1.23.11] - 2026-06-14

### Tests

- Pinned a key CC002 non-false-positive: `Task.WhenAll`/`Task.WhenAny` have no `CancellationToken`
  overload, so these ubiquitous calls are never flagged for token propagation even with a token in
  scope.

## [1.23.10] - 2026-06-14

### Tests

- Pinned the tokenless-async-iterator guided sequence with a combined CC001+CC011 test: a public
  async iterator with no token reports **only** CC001 (add a token); once it has an unmarked token,
  **only** CC011 fires (add `[EnumeratorCancellation]`). The two never fire simultaneously.

## [1.23.9] - 2026-06-14

### Tests

- Pinned a critical CC024 non-false-positive: `Task.Run(async () => ...)` binds the async lambda to
  `Task.Run`'s `Func<Task>` overload (not `Action`), so this extremely common pattern is never
  flagged.

## [1.23.8] - 2026-06-14

### Documentation

- Replaced the badly-stale `NEXT_STEPS.md` (it stopped at the v1.3 era and listed already-shipped
  rules as "future") with a concise current-state roadmap that summarizes the 27 rules by family and
  points to CHANGELOG.md and docs/ANALYZER_HEALTH.md as the live trackers.

## [1.23.7] - 2026-06-14

### Documentation

- Refreshed stale packaged-README sections: "Project Quality" (test count 111 → 350+, mentions the
  cross-analyzer FP guard), "Roadmap" (the originally-planned rules have shipped as CC012/CC016/CC023;
  now 27 rules), and "Supported Frameworks" (added SignalR, hosted services, gRPC, async streams,
  middleware).

## [1.23.6] - 2026-06-14

### Tests

- Added a multi-occurrence fixer test for CC013 (two `Thread.Sleep` calls in one method, both
  rewritten to `await Task.Delay(..., ct)`), exercising the batch/iterative fix-application path.

## [1.23.5] - 2026-06-14

### Documentation

- Refreshed `docs/ANALYZER_HEALTH.md`: the Planning Shortlist and Cross-Cutting Findings now reflect
  the current 27-rule, FP-guarded state (they still described the original 9-rule set), including the
  full list of shared helpers and the FP/FN edge cases fixed by per-rule review.

## [1.23.4] - 2026-06-14

### Tests

- Added a non-async `using`-pattern case to the cross-analyzer FP guard: a method that reads a using
  resource synchronously into a completed task (`return Task.FromResult(resource.Value);`) and one
  that awaits it both produce zero diagnostics — locking in that CC027 only flags the
  deferred-receiver case.

## [1.23.3] - 2026-06-14

### Fixed

- **CC001 false positive:** an `async Task Main` program entry point (`static [async] Task Main()`
  or `Main(string[] args)`) was flagged to add a `CancellationToken`, but the runtime dictates the
  entry-point signature — adding a parameter stops it being recognised as `Main`. The entry-point
  shape is now excluded. Pinned by 2 new tests.

## [1.23.2] - 2026-06-14

### Fixed

- **CC014 false positive:** a `CancellationTokenSource` disposed through the null-conditional
  `cts?.Dispose()` (or `cts?.DisposeAsync()`) was still flagged as undisposed — the analyzer only
  recognised the plain `cts.Dispose()` member access, not the `ConditionalAccessExpression` form. It
  now treats both as disposal. Pinned by 1 new test.

## [1.23.1] - 2026-06-14

### Fixed

- **CC027** now also covers the `using` *statement* form, not just `using` declarations:
  `using (var r = ...) { return r.DoAsync(); }` disposes `r` before the returned task completes just
  as `using var r = ...; return r.DoAsync();` does. Pinned by 1 new test.

## [1.23.0] - 2026-06-14

### Added

- **New rule CC027 — a returned task must not use a disposed `using` resource.** A `using`
  declaration disposes its resource when the method returns; if the method returns a task produced
  by calling that resource (`return resource.DoAsync();`), the resource is disposed while the task is
  still running, so the caller awaits an operation on a disposed object. CC027 flags a non-`async`
  `Task`/`ValueTask`-returning method or local function where a `return` expression is a call whose
  left-most receiver is a `using`-declared local. High confidence by design: only the receiver case
  is flagged — a resource read synchronously into a completed task (e.g.
  `Task.FromResult(resource.Value)`) is not. The fix is to make the method `async` and `await` the
  call. Pinned by 5 new tests.

## [1.22.13] - 2026-06-14

### Tests

- Added a Minimal API case to the cross-analyzer FP guard: an endpoint whose handler lambda accepts
  a token satisfies CC005C with zero diagnostics (faithful `IEndpointRouteBuilder` + `MapGet`
  extension stubs). **Every rule — including all framework rules — is now covered by a clean-code
  guard.**

## [1.22.12] - 2026-06-14

### Tests

- Extended the cross-analyzer FP guard with MediatR and SignalR scenarios: a tokenized
  `IRequestHandler.Handle` and a tokenized hub method satisfy CC005A / CC018 (and CC001) with zero
  diagnostics. The property-token, framework, and handler rules are now all covered by clean-code
  guards.

## [1.22.11] - 2026-06-14

### Tests

- Added an MVC controller case to the cross-analyzer FP guard: a `[HttpGet]` action that accepts a
  `CancellationToken` must satisfy both the general CC001 and the controller-specific CC005B, with
  zero diagnostics across every analyzer (faithful `ControllerBase`/`[HttpGet]` stubs).

## [1.22.10] - 2026-06-14

### Fixed

- **CC024** now also flags an `async delegate { }` anonymous method converted to `Action`, not just
  `async` lambdas — both bind as async void. Pinned by 1 new test.

## [1.22.9] - 2026-06-14

### Fixed

- **CC023** now also flags an `async void` **local function**, not just methods — it is the same
  anti-pattern (cannot be awaited; exceptions crash the process). The code fix changes the local
  function's return type to `Task`. Local functions can't be event handlers or override an external
  signature, so no exclusions apply. Pinned by 2 new tests.

## [1.22.8] - 2026-06-14

### Tests

- Added an exotic-syntax case to the cross-analyzer FP guard: expression-bodied members, a `switch`
  expression returning a `Task`, a non-async `Task`-returning method, and a delegating method — all
  with the token threaded correctly — must produce zero diagnostics across every analyzer.

## [1.22.7] - 2026-06-14

### Documentation

- Added `samples/CancelCop.Sample` files for the framework-free newer rules (CC022 CancelAsync,
  CC023 async void, CC024 async-void lambda, CC025 await using, CC026 SemaphoreSlim.Wait), each a
  violation and its fix. A clean sample build confirms each fires on its violation. The remaining
  framework-specific rules (CC017/CC018/CC020/CC021) stay covered by their analyzer tests.

## [1.22.6] - 2026-06-14

### Tests

- Added a nested-scope case to the cross-analyzer FP guard: an outer `CancellationToken` captured by
  a local function and a lambda, plus a token-checked loop, must produce zero diagnostics across all
  analyzers. Locks in that the shared scope walk recognises captured tokens (no false propagation
  warnings inside nested functions).

## [1.22.5] - 2026-06-14

### Documentation

- The packaged README now has a "Quick Examples" section for every rule: added violation-and-fix
  snippets for CC020–CC026 (gRPC, HttpContext, CancelAsync, async void, async-void lambda,
  await using, SemaphoreSlim.Wait). All 26 rules are now documented with a runnable example.

## [1.22.4] - 2026-06-14

### Tests

- Extended the cross-analyzer false-positive guard with a framework scenario: a `BackgroundService`
  override and a gRPC-style override (both observing their cancellation source, via faithful base-type
  stubs) are run through every analyzer and must produce zero diagnostics. Confirms the property-token
  rules (CC017/CC020) and the CC009 loop-condition fix stay quiet on idiomatic framework code, and
  that CC001 correctly excludes such overrides.

## [1.22.3] - 2026-06-14

### Fixed

- **CC009 false positive:** a loop whose *condition* checks the token —
  `while (!token.IsCancellationRequested)`, `for (...; !token.IsCancellationRequested; ...)`,
  `do { } while (!token.IsCancellationRequested)` — is the canonical cancellation-aware loop but was
  flagged because the analyzer only scanned the loop *body*. CC009 now also accepts a cancellation
  check in the loop condition. Pinned by 3 new tests (while/for/do-while condition checks).

## [1.22.2] - 2026-06-14

### Fixed

- **CC026** now flags every blocking `SemaphoreSlim.Wait` overload, not just the parameterless one:
  `Wait(timeout)`, `Wait(token)`, and `Wait(timeout, token)` all block the thread. The code fix
  carries the original arguments through to `WaitAsync(...)` (e.g. `gate.Wait(ct)` →
  `await gate.WaitAsync(ct)`); only a parameterless `Wait()` has the in-scope token added. Pinned by
  2 new tests.

## [1.22.1] - 2026-06-14

### Tests

- Added a cross-analyzer false-positive regression guard (`AllAnalyzersCleanCodeTests`): a single
  idiomatic async sample — proper token propagation, loop checks, `WithCancellation`,
  `[EnumeratorCancellation]`, `await using`, `await WaitAsync`, and a cancellation-excluding catch —
  is run through **every** analyzer in the package at once and must produce zero diagnostics. A
  future rule that over-fires on correct code now fails here.

## [1.22.0] - 2026-06-14

### Added

- **New rule CC026 — avoid `SemaphoreSlim.Wait()` in async code.** A synchronous `Wait()` on a
  `SemaphoreSlim` blocks the calling thread and is a classic deadlock source under a synchronization
  context. CC026 flags a parameterless `Wait()` on a `System.Threading.SemaphoreSlim` inside an
  `async` method, local function, lambda, or anonymous method, and the code fix rewrites it to
  `await gate.WaitAsync(token)` — flowing the in-scope token when one is available, otherwise
  `await gate.WaitAsync()`. The timeout/token `Wait` overloads are left alone. Pinned by 6 new tests
  (4 analyzer, 2 fixer).

## [1.21.0] - 2026-06-14

### Added

- **New rule CC025 — prefer `await using` for `IAsyncDisposable`.** A type implementing
  `IAsyncDisposable` releases resources asynchronously; disposing it through a synchronous `using`
  calls `Dispose()` (typically blocking on the async cleanup). CC025 flags a `using` statement or
  declaration (without `await`) over a resource whose type implements `System.IAsyncDisposable`,
  inside async code, and the fix turns it into `await using` so `DisposeAsync()` is awaited. Info
  severity. Pinned by 6 new tests (5 analyzer, 1 fixer).

## [1.20.0] - 2026-06-14

### Added

- **New rule CC024 — avoid `async` lambdas converted to `Action`.** When an `async` lambda is
  assigned to `System.Action`/`Action<T>` (or passed where one is expected), it binds as
  `async void`: the caller cannot await it and an unhandled exception crashes the process. The
  classic trap is `Parallel.ForEach(items, async item => await ...)`, where the body runs
  fire-and-forget. CC024 flags an `async` lambda whose converted delegate type is `System.Action`/
  `Action<…>`; `Func<Task>` and event-handler delegates are not `Action` and are left alone. The
  lambda counterpart of CC023; analyzer-only (the right delegate type depends on the consuming API).
  Pinned by 5 new tests.

## [1.19.1] - 2026-06-14

### Fixed

- **CC015** now also flags the blocking forms it previously missed: `task.Wait(timeout)` /
  `task.Wait(token)` (not just the parameterless `Wait()`), and the static joins `Task.WaitAll(...)`
  / `Task.WaitAny(...)`. The code fix still applies only to the forms with a clean `await`
  equivalent (parameterless `Wait()`, `.Result`, `.GetAwaiter().GetResult()`); the timeout/token
  `Wait` overloads and `WaitAll`/`WaitAny` report without a fix (their `await` rewrite would change
  semantics). Pinned by 2 new tests.

## [1.19.0] - 2026-06-14

### Added

- **New rule CC023 — avoid `async void`.** An `async void` method cannot be awaited, so callers
  cannot observe completion, flow cancellation into it, or catch its exceptions (an unhandled one
  crashes the process). CC023 flags an `async void` method whose signature is not the event-handler
  shape (`(object sender, EventArgs e)`, including `EventArgs` subclasses) and is not dictated by an
  override/interface/extern, and the code fix changes the return type to `Task` (adding the
  `System.Threading.Tasks` import when missing). Pinned by 6 new tests (5 analyzer, 1 fixer).

### Fixed

- The shared using-insertion helper no longer leaves a blank line between directives when appending
  an import **after** the last existing using (it was copying the last using's full leading trivia,
  re-inserting the file's leading newline). Surfaced while wiring CC023's fix; affects any fix that
  adds an alphabetically-last import.

## [1.18.1] - 2026-06-14

### Fixed

- **CC010's code fix** no longer produces mis-bound code when the `await foreach` source is a loose
  expression. `await foreach (var x in await GetAsync())` now becomes
  `(await GetAsync()).WithCancellation(token)` instead of the wrong
  `await GetAsync().WithCancellation(token)` (which binds `.WithCancellation` to the inner call and
  awaits the result). The fix now parenthesizes the receiver unless it already binds tighter than
  member access. Pinned by 1 new fixer test.

## [1.18.0] - 2026-06-14

### Added

- **New rule CC022 — prefer `await CancelAsync()` over `Cancel()` in async code.**
  `CancellationTokenSource.Cancel()` runs every registered callback synchronously on the calling
  thread, so a slow callback blocks the canceller. .NET 8's `CancelAsync()` schedules them instead.
  CC022 flags a parameterless `Cancel()` on a `CancellationTokenSource` inside an `async` method,
  local function, lambda, or anonymous method (Info severity — `Cancel()` is still valid), and the
  code fix rewrites it to `await cts.CancelAsync()`. The `Cancel(bool)` overload (no async
  counterpart) and a sync context are left alone. Pinned by 5 new tests (4 analyzer, 1 fixer).

## [1.17.0] - 2026-06-14

### Added

- **CC019 now ships a code fix.** The "Rethrow OperationCanceledException" fix inserts
  `if (ex is OperationCanceledException) throw;` as the first statement of the flagged catch block
  (introducing an `ex` variable when the catch has none), so cancellation propagates instead of
  being swallowed. The `System` import is added when missing. Offered for typed catches; a bare
  catch-all (no exception variable to test) still reports without a fix. Pinned by 2 new fixer tests.

## [1.16.1] - 2026-06-14

### Fixed

- **CC012** now also flags `CancellationToken.None`/`default` passed to a target-typed `new(...)`
  constructor (e.g. `Worker w = new(CancellationToken.None);`). The argument-parent check only
  recognised the explicit `new T(...)` form; it now uses `BaseObjectCreationExpressionSyntax`, which
  covers both. Pinned by 1 new test.

## [1.16.0] - 2026-06-13

### Added

- **New rule CC021 — observe `HttpContext.RequestAborted`.** The ASP.NET Core request cancellation
  token is exposed as `HttpContext.RequestAborted` (a property, invisible to CC002, like gRPC's
  `ServerCallContext.CancellationToken`). CC021 flags a method with a
  `Microsoft.AspNetCore.Http.HttpContext` parameter that does async work but never reads
  `context.RequestAborted` and never passes the context on. Info severity (an `HttpContext` is often
  taken for non-cancellation reasons). Analyzer-only. Pinned by 5 new tests (HttpContext stub).

### Changed

- The context-token probing used by CC020/CC021 is now shared
  (`CancellationTokenHelpers.AccessesMember` / `ParameterEscapesAsArgument`); CC020 was refactored
  onto it.

## [1.15.0] - 2026-06-13

### Added

- **New rule CC020 — gRPC method should observe `ServerCallContext.CancellationToken`.** In a gRPC
  service the per-call cancellation token is exposed as a property
  (`ServerCallContext.CancellationToken`), not a parameter, so the general propagation rule (CC002)
  cannot see it. CC020 flags a method with a `Grpc.Core.ServerCallContext` parameter whose body does
  async work (contains an `await`) but never reads `context.CancellationToken` and never passes the
  context on to another method. Analyzer-only (which call to thread the token into is ambiguous).
  Pinned by 5 new tests (using a faithful `ServerCallContext` stub, no gRPC package).

## [1.14.4] - 2026-06-13

### Fixed

- **CC001** now also flags a public/protected **async iterator**
  (`async IAsyncEnumerable<T>`/`IAsyncEnumerator<T>`) that has no `CancellationToken` parameter.
  Previously these slipped through both CC001 (which only matched `Task`/`ValueTask`) and CC011
  (which only checks an *existing* token parameter for `[EnumeratorCancellation]`), so a tokenless
  public async stream was flagged by nothing. The existing "Add CancellationToken parameter" fix
  applies; CC011 then prompts the `[EnumeratorCancellation]` attribute once the parameter exists.
  Pinned by 3 new tests. The shared `IsAsyncReturnType` is intentionally left unchanged (it feeds
  many rules); the iterator check is local to CC001.

## [1.14.3] - 2026-06-13

### Changed

- **Internal refactor, no behavior change.** CC005A (MediatR) now uses the shared
  `CancellationTokenHelpers.HasCancellationTokenParameter` and `IsAsyncReturnType` instead of its
  own inline token and `Task`-return checks, so all the rules share one definition of "is a
  CancellationToken" and "is an async return type". The `IRequestHandler.Handle` return type is an
  interface-mandated `Task`, so behavior is unchanged. Closes the last P3 backlog item.

## [1.14.2] - 2026-06-13

### Documentation

- Added class-level `<remarks>`/`<example>` XML documentation to the CC003 (EF Core), CC004
  (HttpClient), CC005A (MediatR), and CC005B (controller) analyzers, matching the doc style the
  other rules already carry. Closes the P3 "analyzer XML docs" backlog item; no behavior change.

## [1.14.1] - 2026-06-13

### Documentation

- Completed README "Quick Examples" parity for every rule by adding sections for CC016–CC019, and
  added `samples/CancelCop.Sample` files for the two general rules (CC016, CC019). The packaged
  README now shows a violation-and-fix for all nineteen rules. CC017 (BackgroundService) and CC018
  (SignalR) remain framework-specific and are covered by their analyzer tests.

## [1.14.0] - 2026-06-13

### Added

- **New rule CC019 — broad `catch` swallows `OperationCanceledException`.** A catch-all or
  `catch (Exception)` over awaited work that does not rethrow turns a cancelled operation into a
  generic, handled failure, so callers awaiting the cancellation never observe it. CC019 flags such
  a catch (Info severity) when it has no `when` filter, its `try` block contains an `await`, and its
  body never rethrows. Conservative: a `when` filter, a rethrow, a more specific exception type, or
  a `try` with no awaited work all suppress it. Pinned by 7 new tests.

## [1.13.0] - 2026-06-13

### Added

- **New rule CC018 — SignalR hub methods should accept a `CancellationToken`.** SignalR binds a
  hub method's `CancellationToken` parameter to the invocation/connection abort token, so a
  long-running hub method without one keeps running after the client disconnects. CC018 flags a
  public, non-static, async (or `Task`/`ValueTask`-returning) method on a
  `Microsoft.AspNetCore.SignalR.Hub`/`Hub<T>` subclass that has no token parameter — the SignalR
  analogue of CC005B. Hub lifecycle overrides (`OnConnectedAsync`/`OnDisconnectedAsync`) and other
  externally-controlled signatures are excluded. The existing "Add CancellationToken parameter"
  code fix now also serves CC018. Pinned by 6 new tests (5 analyzer, 1 fixer).

## [1.12.0] - 2026-06-13

### Added

- **New rule CC017 — `BackgroundService.ExecuteAsync` should observe its stopping token.** A hosted
  service whose `ExecuteAsync` override never references its `stoppingToken` will not stop when the
  host shuts down, stalling graceful shutdown until a forced timeout. CC017 flags an `override` of
  `ExecuteAsync(CancellationToken)` on a `Microsoft.Extensions.Hosting.BackgroundService` subclass
  whose body never references the token. This is the high-value override case that CC016
  deliberately skips (it excludes externally-controlled signatures). A token passed to a helper or
  observed in a loop counts as used. Analyzer-only. Pinned by 4 new tests. The
  parameter-reference check was extracted to a shared `CancellationTokenHelpers.IsParameterReferenced`
  now used by both CC016 and CC017.

## [1.11.0] - 2026-06-13

### Added

- **New rule CC016 — `CancellationToken` parameter is accepted but never used.** A method or local
  function whose body performs asynchronous work (contains an `await`) but never references its
  declared `CancellationToken` parameter silently fails to honour cancellation. CC016 flags the dead
  token (Info severity; the token is occasionally reserved deliberately). Signatures dictated
  elsewhere — `override`, interface implementations, `extern` — are excluded since they cannot drop
  the parameter, as are sync bodies with no `await`. A token referenced anywhere, including inside a
  nested lambda or local function, is considered used. Analyzer-only (removing or wiring up a
  parameter is too invasive to automate). Pinned by 6 new tests.

## [1.10.2] - 2026-06-13

### Fixed

- **CC015** now also flags `task.ConfigureAwait(false).GetAwaiter().GetResult()` — a very common
  sync-over-async blocking form that the awaiter-type check previously missed (it only recognised
  the bare `TaskAwaiter`/`ValueTaskAwaiter`, not the configured awaiters). The fix rewrites it to
  `(await task.ConfigureAwait(false))`, preserving the `ConfigureAwait`. Pinned by 2 new tests.

## [1.10.1] - 2026-06-13

### Documentation

- Added per-rule README "Quick Examples" sections and sample-project files for the six newer rules
  (CC010–CC015), each showing a violation and its fix. The packaged README now documents every
  shipped rule with a runnable example, and `samples/CancelCop.Sample` demonstrates them on build.

## [1.10.0] - 2026-06-13

### Added

- **New rule CC015 — avoid blocking on async code.** Synchronously blocking on a task
  (`task.Result`, `task.Wait()`, `task.GetAwaiter().GetResult()`) inside an `async` function can
  deadlock under a synchronization context and wraps cancellation in an `AggregateException`. CC015
  flags these three forms on a `Task`/`Task<T>`/`ValueTask` inside an `async` method, local
  function, lambda, or anonymous method (symbol-resolved, so a look-alike `.Result` on a non-task
  type is ignored), and the code fix rewrites them to `await` the task. Pinned by 8 new tests
  (5 analyzer, 3 fixer). The async-context check was extracted to a shared
  `CancellationTokenHelpers.IsInAsyncFunction` now used by both CC013 and CC015.

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
