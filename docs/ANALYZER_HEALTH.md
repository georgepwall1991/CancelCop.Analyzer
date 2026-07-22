# Analyzer Health

Reviewed: 2026-07-22 (refreshed through the v1.27.209 hardening loop)

A deliberately harsh health audit for the twenty-eight implemented CancelCop rule IDs (CC001–CC006, CC009–CC028).
Scores are 1–5, where `5` means reference-quality and hard to improve, `3` means usable but
meaningfully incomplete, and `1` means unreliable or underbuilt. A `5` is rare.

## Rubric

| Metric | Meaning |
| --- | --- |
| Analyzer | Semantic depth, framework-awareness, scope walking (methods/local functions/lambdas), externally-controlled-signature handling, and diagnostic placement accuracy. |
| False Positives | Conservatism around lookalike APIs (same method name, different type), explicit configuration, signatures fixed by a base type/interface, and intentional usage. |
| Fix Strategy | Safety, compilability, idempotence, and whether the generated code builds rather than emitting broken syntax. `n/a` for deliberately analyzer-only rules. |
| Tests | Strength of analyzer, fixer, negative, and edge-case coverage. |
| Docs/Samples | Clarity and consistency of XML docs, the sample project, README rule table, and severity accuracy. |
| Importance | User-facing usefulness based on frequency, runtime/resource-leak risk, and actionability. |

Calibration notes:

- Info/style rules are scored by product value, not implementation effort. A healthy convention rule
  can still have low Importance.
- Docs/Samples are penalised for drift: a doc that claims behaviour the analyzer does not implement
  scores lower than a thinner-but-accurate doc.
- A scope gap (analyzer ignores local functions or lambdas that a sibling rule handles) is an Analyzer
  penalty even when no user has reported it, because it is a silent false negative.

## Scorecard

| Rule | Title | Category | Severity | Analyzer | False Positives | Fix Strategy | Tests | Docs/Samples | Importance | Priority | Notes |
| --- | --- | --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | --- | --- |
| CC001 | Public async method missing CancellationToken | Usage | Warning | 4 | 4 | 4 | 4 | 4 | 4 | Low | Public/protected async returning Task/ValueTask, excludes override/interface/extern signatures (v1.4.0), compilable fixer (using insertion, name-collision, `params`). Solid entry-point guard. |
| CC002 | CancellationToken not propagated | Usage | Warning | 4 | 4 | 4 | 4 | 4 | 4 | Low | **v1.4.2:** walks lambdas/local functions/containing method via the shared `FindEnclosingCancellationTokenParameter` (also CC009); expression-tree lambdas excluded. **v1.27.1/v1.27.7 (FP+fix):** firing now requires a *type-compatible* token overload (`GetTypeCompatibleTokenParameterName`) — case A (a sibling overload whose non-token params match the call by type) or case B (the bound overload's own omitted optional token). A merely-same-name token overload with different params no longer yields a non-compiling fix (e.g. `StreamWriter.WriteAsync(string)`, whose token overload takes `ReadOnlyMemory<char>`, is left alone). Parameter types compare with an ordinal-aware equivalence so generic overload pairs (`FooAsync<T>(T)` / `FooAsync<T>(T, CancellationToken)`) still fire. |
| CC003 | EF Core async call missing CancellationToken | Usage | Warning | 4 | 4 | 4 | 4 | 4 | 4 | Low | **v1.4.3:** now uses the shared `FindEnclosingCancellationTokenParameter` scope walk (local functions, lambdas, containing method) plus CC002's expression-tree guard, closing the scope-gap false negative and aligning all four propagation rules on one walk. Namespace-gated to `Microsoft.EntityFrameworkCore`, overload-checked. Class-level XML `<remarks>`/`<example>` doc present (v1.14.2). |
| CC004 | HttpClient async call missing CancellationToken | Usage | Warning | 4 | 4 | 4 | 4 | 4 | 4 | Low | **v1.4.3:** same shared scope walk + expression-tree guard as CC003. Type-gated to `System.Net.Http.HttpClient`, overload-checked. Class-level XML `<remarks>`/`<example>` doc present (v1.14.2). |
| CC005A | MediatR handler missing CancellationToken | Usage | Warning | 3 | 4 | 4 | 4 | 3 | 2 | Low | Gated to `MediatR.IRequestHandler.Handle`. Real MediatR's interface already mandates the token, so the rule mostly assists a non-compiling handler rather than catching a live omission — low product importance. Uses the shared `HasCancellationTokenParameter`/`IsAsyncReturnType` helpers (moved off the hand-rolled checks in v1.14.3); only the `IRequestHandler.Handle` gating is rule-specific. |
| CC005B | Controller action missing CancellationToken | Usage | Warning | 4 | 4 | 4 | 4 | 3 | 4 | Low | Heavily hardened in v1.4.0: public non-static, `ControllerBase`/`Controller` by namespace, inherited `[NonAction]`, MVC HTTP-method attribute by identity + subclass. **v1.27.182:** externally controlled override/interface signatures are excluded so the suggested parameter addition cannot break their contract. Conservative and accurate. |
| CC005C | Minimal API handler missing CancellationToken | Usage | Warning | 4 | 4 | 4 | 4 | 3 | 4 | Low | **v1.4.4:** method-group handlers (`app.MapGet("/", Handler)`, `Handlers.Get`, local functions) are now analysed and fixed (token added to the referenced declaration, `= default`, same-document only). v1.4.1 gated the receiver on `IEndpointRouteBuilder`. Remaining false negative (pre-existing, low value): the unreduced static-call form (`EndpointRouteBuilderExtensions.MapGet(app, …)`). |
| CC006 | CancellationToken should be last parameter | Style | Info | 4 | 4 | n/a | 4 | 3 | 2 | Low | v1.4.0: methods, constructors, primary constructors, local functions; excludes externally-controlled signatures and unmovable tokens (before trailing `params`, extension `this`). Analyzer-only by design (reordering would touch every call site). Convention rule, low importance. |
| CC009 | Loop missing cancellation check | Usage | Warning | 4 | 4 | 4 | 4 | 4 | 4 | Low | v1.4.0: semantic receiver resolution (no name matching), walks methods/local functions/lambdas, all four loop kinds, fixer inserts `ThrowIfCancellationRequested()`. **v1.27.180:** a compile-time-only `nameof(token.IsCancellationRequested)` reference no longer counts as a runtime cancellation check. The strongest rule in the set. |
| CC010 | `await foreach` missing CancellationToken flow | Usage | Warning | 4 | 4 | 4 | 4 | 3 | 4 | Low | **v1.5.0 (new); fixes v1.27.183/v1.27.199:** flags `await foreach` over an `IAsyncEnumerable<T>` (or implementer) when a token is in scope, the source does not already pass a token argument, and it is not already a configured cancelable enumerable; fixer rewrites the source to `.WithCancellation(token)`. `WithCancellation` wrapper recognition is semantic and framework-gated, so look-alikes do not suppress the rule. Custom `ConfigureAwait` overloads that receive a `CancellationToken` remain visible to the producer-token check rather than being mistaken for framework-only configuration; boolean configuration without token flow still reports. Uses the shared `FindEnclosingCancellationTokenParameter` scope walk. Conservative: synchronous `foreach`, no-token scopes, and producer calls already receiving a token are quiet. No analyzer XML `<remarks>` example variety yet (P3). |
| CC028 | Blocking `System.IO` read/write/append in async code | Usage | Warning | 4 | 4 | 4 | 4 | 4 | 4 | Low | **v1.24.0 (new); fixes v1.25.0/v1.27.198/v1.27.202:** flags a blocking synchronous `System.IO` helper inside async code (method/local function/lambda/anonymous method) when a signature-compatible `<name>Async` counterpart exists on the type — `File` read/write/append (`ReadAllText`/`ReadAllBytes`/`ReadAllLines`, `WriteAll*`, `AppendAll*`), `StreamReader.ReadToEnd`/`ReadLine`, and (v1.27.0) `StreamWriter.Write`/`WriteLine`/`Flush` (generalised from `System.IO.File` to `System.IO` in v1.26.0). Qualified and `using static` File calls are supported. Null-conditional instance calls are diagnosed but intentionally receive no fix because preserving null semantics is context-dependent. **v1.27.0** replaced the name-only counterpart lookup with a parameter-signature match (overload equals the call's params, optionally + a trailing token), so the rewrite always compiles (`StreamWriter.Write(bool)` has no async form → quiet) and the token is only flowed when the matched overload accepts one (`Write(string)`→`await WriteAsync(text)` tokenless; `Flush()`→`await FlushAsync(token)`). Fixer rewrites safe direct-access shapes to `await …Async(…[, token])`, flowing the in-scope token via `FindEnclosingCancellationTokenParameter`. Symbol-resolved + namespace-gated to `System.IO` (look-alikes ignored); only in async context via the shared `IsInAsyncFunction`. Fix-All batches across the type→method map. Rounds out the blocking-in-async family (CC013/CC015/CC026). |
| CC027 | Returned task uses a disposed `using` resource | Usage | Warning | 4 | 4 | n/a | 4 | 3 | 4 | Low | **v1.23.0 (new); fixes v1.27.184/v1.27.206:** flags a non-async `Task`-returning method/local function whose `return` is a call on a local disposed by a `using` declaration, declaration-form using statement, or expression-form `using (resource)` — the resource is disposed before the returned task completes (premature disposal). Expression-form analysis is scoped to returns inside that exact using body. Receiver walking unwraps interface/base casts, which do not change the using local's lifetime. High confidence: only the receiver case is flagged (a synchronous read into a completed task like `Task.FromResult(resource.Value)` is not). Analyzer-only (fix = make async + await). |
| CC026 | `SemaphoreSlim.Wait()` in async code | Usage | Warning | 4 | 4 | 4 | 4 | 3 | 4 | Low | **v1.22.0 (new); fixes v1.27.185/v1.27.191/v1.27.193/v1.27.196:** flags potentially blocking `SemaphoreSlim.Wait()` overloads (parameterless, timeout, token), including null-conditional calls, inside async code — a classic deadlock source; fixer → `await gate.WaitAsync(…)` for safe direct-access shapes, carrying the original arguments through and injecting the in-scope token when `Wait()` was parameterless (v1.22.2). Provably zero integer and framework `TimeSpan` timeout forms (zero field, defaults, zero-argument construction) are excluded because they are immediate try-enter probes. Symbol-resolved to `System.Threading.SemaphoreSlim`. |
| CC025 | Prefer `await using` for `IAsyncDisposable` | Usage | Info | 4 | 4 | 4 | 4 | 3 | 3 | Low | **v1.21.0 (new); fix v1.27.187:** flags a `using` statement/declaration (no `await`) over an `IAsyncDisposable` resource in async code; fixer prepends `await`. Both the declaration (`using var x = …`) and statement (`using (…)`) forms, expression and variable receivers. Top-level programs are covered when their synthesized entry point contains `await`; purely synchronous top-level code stays quiet. Info. |
| CC024 | `async` lambda converted to a void-returning delegate | Usage | Warning | 4 | 4 | n/a | 4 | 3 | 4 | Low | **v1.20.0 (new); fix v1.27.186:** the lambda counterpart of CC023. Flags an `async` lambda whose converted delegate returns `void`, including custom delegate types (binds as async void). Catches the `Parallel.ForEach(..., async x => …)` trap. Task-returning delegates and the sanctioned `(object, EventArgs-derived)` event-handler shape are excluded. Analyzer-only (the right delegate depends on the consuming API). |
| CC023 | `async void` (non-event-handler) | Usage | Warning | 4 | 4 | 4 | 4 | 3 | 4 | Low | **v1.19.0 (new):** flags an `async void` method that is not an event handler (`(object, EventArgs)` shape, EventArgs subclasses included) and not externally-controlled; fixer changes `void`→`Task` + adds the Tasks import. Classic async anti-pattern (cf. VSTHRD100) — `async void` can't be awaited or cancelled and crashes on unhandled exceptions. |
| CC022 | Prefer `CancelAsync()` over `Cancel()` in async | Usage | Info | 4 | 4 | 4 | 4 | 3 | 3 | Low | **v1.18.0 (new); fix v1.27.187:** flags a parameterless `CancellationTokenSource.Cancel()` inside async code; fixer rewrites to `await cts.CancelAsync()`. Top-level programs are covered when their synthesized entry point contains `await`; purely synchronous top-level code stays quiet. Info (`Cancel()` is still valid). The `Cancel(bool)` overload and sync contexts are excluded. Modern .NET 8 guidance — `Cancel()` runs callbacks synchronously on the caller. |
| CC021 | `HttpContext.RequestAborted` not observed | Usage | Info | 4 | 3 | n/a | 4 | 3 | 3 | Low | **v1.16.0 (new); fixes v1.27.181/v1.27.200:** the HttpContext parallel of CC020. Flags a method with a `Microsoft.AspNetCore.Http.HttpContext` parameter that does async work but never reads `context.RequestAborted` and never passes the context on. Compile-time-only `nameof(context.RequestAborted)` does not count as observation. Passing the context as a direct argument or as a reduced extension-method receiver counts as handing it off; ordinary instance calls do not. Info because HttpContext is often taken for non-cancellation reasons (hence FP score 3). Shares `AccessesMember`/`ParameterEscapesAsArgument` with CC020. |
| CC020 | gRPC method ignores `ServerCallContext.CancellationToken` | Usage | Warning | 4 | 4 | n/a | 4 | 3 | 3 | Low | **v1.15.0 (new); fixes v1.27.181/v1.27.200:** flags a method with a `Grpc.Core.ServerCallContext` parameter that does async work but never reads `context.CancellationToken` and never passes the context on. Compile-time-only `nameof(context.CancellationToken)` does not count as observation. Passing the context as a direct argument or as a reduced extension-method receiver counts as handing it off; ordinary instance calls do not. Fills a genuine gap — the token is a property, not a parameter, so CC002 can't see it (cf. CC017 for BackgroundService). Analyzer-only; gated by parameter type name+namespace (tests use a stub). |
| CC019 | Broad catch swallows `OperationCanceledException` | Usage | Info | 4 | 3 | 4 | 4 | 3 | 3 | Low | **v1.14.0 (new); fixes v1.17.0/v1.27.174/v1.27.175:** flags a catch-all/`catch (Exception)` with no `when` filter, over a `try` containing awaited work in the current function scope, whose body never rethrows. Covers explicit `await`, `await foreach`, and both `await using` forms; awaits inside nested local functions/lambdas are ignored because that deferred work does not execute in the `try` itself. Info because boundary handlers are sometimes intended. Conservative (filter/rethrow/specific-type/no-await all suppress). The fix inserts `if (ex is OperationCanceledException) throw;` (typed catches only). |
| CC018 | SignalR hub method missing `CancellationToken` | Usage | Warning | 4 | 4 | 4 | 4 | 3 | 4 | Low | **v1.13.0 (new):** SignalR analogue of CC005B. Flags a public non-static async method on a `Microsoft.AspNetCore.SignalR.Hub`/`Hub<T>` subclass without a token; excludes lifecycle overrides + externally-controlled signatures. Reuses the shared add-token-parameter fixer. Base-type gated by name+namespace (tests use a faithful Hub stub, no package). |
| CC017 | `BackgroundService.ExecuteAsync` ignores stopping token | Usage | Warning | 4 | 4 | n/a | 4 | 3 | 4 | Low | **v1.12.0 (new); fixes v1.27.177/v1.27.209:** flags an `override` of `ExecuteAsync(CancellationToken)` on a `Microsoft.Extensions.Hosting.BackgroundService` subclass whose body never observes the incoming stopping token at runtime — the override case CC016 skips. Compile-time-only `nameof(stoppingToken)` and a write-only overwrite do not count as observation. Analyzer-only; token passed to a helper or observed in a loop counts as used. Framework-gated to BackgroundService by base-type walk. |
| CC016 | Unused `CancellationToken` parameter | Usage | Info | 4 | 4 | n/a | 4 | 3 | 3 | Low | **v1.11.0 (new); fixes v1.27.11/v1.27.177/v1.27.178/v1.27.209:** flags a method/local function that does async work (has `await` in its current function scope) but never observes its incoming `CancellationToken` parameter at runtime; excludes externally-controlled signatures and sync bodies. Await eligibility stops at nested lambdas/local functions, while token-reference analysis still descends into them because captures are real usage. Compile-time-only `nameof(token)` and a write-only simple assignment do not count; a right-hand-side read still does. A token marked `[EnumeratorCancellation]` is excluded because the async-iterator infrastructure observes it (cf. CC011). Analyzer-only by design. |
| CC015 | Blocking on async code (sync-over-async) | Usage | Warning | 4 | 4 | 4 | 4 | 3 | 4 | Low | **v1.10.0 (new); fixes v1.27.188–v1.27.190/v1.27.192/v1.27.194/v1.27.195:** flags `.Result` and potentially blocking `.Wait(...)` (including null-conditional access), plus `.GetAwaiter().GetResult()`, on a `Task`/`Task<T>`/`ValueTask` inside an `async` function; fixer rewrites safe direct-access shapes to `await`. Provably zero integer and exact framework `TimeSpan` timeout forms (zero field, defaults, zero-argument construction) are excluded because they are immediate completion probes; this applies to instance `Wait` and static `WaitAll`/`WaitAny`. Symbol-resolved (lookalikes ignored). Shares `IsInAsyncFunction` with CC013. |
| CC014 | `CancellationTokenSource` never disposed | Usage | Warning | 4 | 4 | 4 | 4 | 3 | 4 | Low | **v1.9.0 (new); fixes v1.27.179/v1.27.197/v1.27.203/v1.27.205/v1.27.207:** flags a local `new CancellationTokenSource(...)`/`CreateLinkedTokenSource(...)` that is not a `using` decl, never disposed, and never escapes (return/out-assign/argument/nested-capture); fixer converts to a `using` declaration. Top-level-program locals use the compilation unit as their synthesized function boundary and receive the same safe `using var` fix. Compile-time-only `nameof(cts.Dispose)` does not count as disposal. Parentheses and null-forgiving operators are compile-time-only and are unwrapped before disposal/escape shape checks. An actual parameterless `System.IDisposable.Dispose()` invocation through an exact non-user-defined interface cast also counts as disposal; arbitrary casted calls do not. Conservative escape analysis — any disposal-elsewhere path suppresses it (like a scoped CA2000 for CTS). |
| CC013 | `Thread.Sleep` in async code | Usage | Warning | 4 | 4 | 4 | 4 | 3 | 4 | Low | **v1.8.0 (new); fixes v1.27.201/v1.27.204:** flags `System.Threading.Thread.Sleep` lexically inside an `async` method/local function/lambda/anonymous method; fixer rewrites to `await Task.Delay(delay, token)` (token flowed when in scope). Provably zero integer and exact framework `TimeSpan` duration forms (zero field, defaults, zero-argument construction) are excluded because `Thread.Sleep(0)` is a scheduler yield rather than a timed wait and `Task.Delay(0)` completes synchronously; positive and runtime-determined sleeps still report. Async-context check stops at the first function boundary, so a synchronous lambda inside an async method is quiet. Symbol-resolved (no name-only match). |
| CC012 | Explicit `CancellationToken.None`/`default` when a token is in scope | Usage | Info | 4 | 4 | 4 | 4 | 3 | 3 | Low | **v1.7.0 (new); fixes v1.27.176/v1.27.208:** flags the actual `System.Threading.CancellationToken.None` property or `default`/`default(CancellationToken)` bound to a `CancellationToken` parameter when an in-scope token exists, including repeatedly parenthesized forms; fixer replaces the whole argument expression with the token. The framework property is symbol-resolved, so a custom token-valued property merely named `None` stays quiet. Info severity because best-effort cleanup legitimately opts out. Uses the shared scope walk + converted-type gate (a bare `default` only counts in token context). |
| CC011 | Async-iterator token missing `[EnumeratorCancellation]` | Usage | Warning | 4 | 4 | 4 | 4 | 3 | 4 | Low | **v1.6.0 (new):** producer-side complement to CC010. Flags an `async IAsyncEnumerable<T>` iterator (method or local function with `yield`) whose `CancellationToken` parameter lacks `[EnumeratorCancellation]`, so a token passed via `.WithCancellation` would be silently dropped. Fixer adds the attribute + `System.Runtime.CompilerServices` import. Conservative: non-iterators returning the type, tokenless iterators, and already-marked params are quiet. Yield detection stops at nested local functions/lambdas. |

## Planning Shortlist

| Priority | Rules | Work |
| --- | --- | --- |
| High | None | No rule has a correctness defect severe enough to block a release. |
| Medium | None | No P0/P1 items open; two P2 (opportunistic) items remain — see backlog. |
| Low | All 28 rules | Mature and FP-clean. Every rule is covered by a clean-code FP guard (`AllAnalyzersCleanCodeTests`) spanning core, framework (controllers/MediatR/SignalR/Minimal API/BackgroundService/gRPC), nested-scope, exotic-syntax, modern-C#-shape, async-File-I/O, and non-async `using` cases. Improve opportunistically. |

The rule set has grown from the original 9 (CC001–CC006, CC009) to 28 (adding CC010–CC028 across the
async-stream, blocking, lifecycle, async-hygiene, and property-token families). Recent hardening loops
have shifted from new rules to FP/FN edge cases found by reviewing each rule against representative
code — three real false positives (CC009 loop condition, CC014 `cts?.Dispose()`, CC001 `async Main`)
and several false negatives (CC023 local functions, CC024 anonymous methods, CC027 `using` statement)
were fixed this way.

## Prioritized Fix Backlog

Grading: **P0** = release-blocking; **P1** = next hardening loop; **P2** = opportunistic; **P3** = directional.

### P0 — Release-blocking
- _None._

### P1 — Next hardening loop
- _None._ The backlog is down to P2/P3 items; the next loop should re-audit rule health rather
  than work a pre-named item.

### P2 — Opportunistic
- **Dedupe the add-token-to-declaration recipe.** The CC005C method-group fix and the CC001 fix
  both build `CancellationToken cancellationToken = default` and insert it via
  `InsertTokenParameter`; the method-group symbol resolution (symbol-or-single-candidate) is also
  duplicated between `MinimalApiAnalyzer` and `MinimalApiCodeFixProvider`. Shared helpers would
  keep analyzer and fixer matching in lockstep.

### Resolved
- ~~**CC005C → CC002 cascade** (v1.23.19).~~ `MinimalApiPropagationCascadeTests` pins the intentional
  guided sequence: applying the method-group fix first introduces a token, after which propagation
  diagnostics can guide it through the handler body.
- ~~**Shared report pipeline** (v1.4.7).~~ CC002/CC003/CC004 now delegate their identical tail to
  `CancellationTokenHelpers.ReportIfTokenNotPropagated`; each analyzer is rule-specific gating
  plus one call. Pure refactor pinned by the existing 200 tests.
- ~~**Named-argument code fixes** (v1.4.6).~~ CC002/CC003/CC004 fixes append a named token argument
  (`cancellationToken: ct`, using the overload's parameter name carried in `TokenArgumentName`
  diagnostic metadata) whenever the call already uses a named argument, avoiding CS8323. Pinned by
  3 new fixer tests (EF named predicate, HttpClient out-of-position named args, CC002 custom
  overload with a differently-named token parameter).
- ~~**Constructor / primary-constructor token parameters** (v1.4.5).~~ The shared walk now inspects
  constructor parameter lists and, for tokenless non-static instance members and instance field
  initializers, falls through to the containing type's primary-constructor parameters (classes and
  records), resolving through the type symbol when the primary constructor sits on another partial
  part. Conservative guards: static members, static field **and event-field** initializers
  (`BaseFieldDeclarationSyntax`), non-primary constructor bodies (CS9105), and operators never see
  the primary token; the first containing type ends the search. Pinned by 12 new tests across
  CC002/CC003/CC004/CC009.
- ~~**CC005C method-group handlers** (v1.4.4).~~ `app.MapGet("/", Handler)`, `Handlers.Get`,
  `Handler<T>`, `(Handler)`, and local-function method groups are resolved to the referenced method
  and flagged when async-shaped without a token; the fixer adds
  `CancellationToken cancellationToken = default` to the referenced declaration (same-document
  only). Review hardening: `handler.Invoke` and metadata methods never flag; virtual/abstract and
  partial handlers report but get no automatic fix (CS0115/CS8795 guards); Fix All on a shared
  handler adds the parameter once; the lambda fixer matches the diagnostic span exactly so it
  cannot patch an unrelated enclosing lambda. Pinned by 16 new tests.
- ~~**CC003 / CC004 scope consistency** (v1.4.3).~~ Both now use the shared
  `FindEnclosingCancellationTokenParameter` walk (local functions, lambdas, containing method) and
  CC002's expression-tree guard; pinned by 9 new tests (5 EF Core, 4 HttpClient), including an
  expression-tree negative built on a no-optional-args EF-namespace stub (real EF Core signatures
  cannot appear in an expression tree, CS0854).
- ~~**Static anonymous functions in the shared walk** (v1.4.3, surfaced in review).~~ A tokenless
  `static` lambda / static local function now stops the walk — the outer token is not capturable
  (CS8820/CS8421), so reporting it was a false positive with a non-compiling fix. The walk also
  matches `AnonymousFunctionExpressionSyntax`, so `delegate (CancellationToken ct) { … }` parameters
  are now found (previously a silent false negative). Pinned by 5 new tests across CC002/CC003/CC004.
- ~~**CC002 lambda scope + docs drift** (v1.4.2).~~ CC002 now walks lambdas via the shared
  `FindEnclosingCancellationTokenParameter`; the docs' lambda-support promise is now true and pinned by
  three new tests.

### P3 — Directional
- ~~**CC005A product value + shared helper** (docs v1.14.2, refactor v1.14.3).~~ CC005A's class doc
  now records that it mainly assists a handler not yet satisfying the MediatR interface, and its
  inline token-parameter / async-return checks were replaced with
  `CancellationTokenHelpers.HasCancellationTokenParameter` / `IsAsyncReturnType`. No behavior change
  (the `IRequestHandler.Handle` return type is interface-mandated `Task`).
- ~~**Analyzer XML docs** (v1.14.2).~~ CC003, CC004, CC005A, CC005B now carry class-level
  `<remarks>`/`<example>` doc blocks matching CC001/CC002/CC009 and the CC010+ rules; every shipped
  analyzer is now self-documenting.
- ~~**Rule-catalog trust contract** (v1.4.8).~~ `RuleCatalogTests` now asserts every shipped
  descriptor has a README rule-table row (severity + fix mark accurate), is tracked in
  `AnalyzerReleases.Shipped.md` with matching severity, and that every exported code-fix provider
  targets a shipped rule — plus a discovery canary so reflection finding zero analyzers cannot
  vacuously pass.

## Cross-Cutting Findings

- Every analyzer calls `ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None)` and
  `EnableConcurrentExecution()` — correct and consistent.
- Shared logic lives in `CancellationTokenHelpers`: `IsCancellationToken`, `IsAsyncReturnType`,
  `HasOverloadWithCancellationToken`, `IsSignatureExternallyControlled`, the
  `FindEnclosingCancellationTokenParameter` scope walk (CC002/003/004/009/010/012/013/026/028),
  `IsInAsyncFunction` (CC013/015/022/025/026/028), `IsParameterReferenced` (CC016/017),
  `ReportIfTokenNotPropagated` (CC002/003/004), and `AccessesMember`/`ParameterEscapesAsArgument`
  (CC020/021). `CancellationTokenFixHelpers` shares the fixer plumbing (`InsertTokenParameter`,
  `AddUsing`). CC005A was moved onto the shared helpers in v1.14.3, so no analyzer hand-rolls a token
  check any more.
- Diagnostic placement is good: CC001/CC005A/CC005B on the method identifier, CC002/CC003/CC004 on the
  invoked member name, CC006 on the offending parameter, CC009 on the loop keyword.
- Release tracking (`AnalyzerReleases.Shipped.md` / `.Unshipped.md`) is wired as `AdditionalFiles` with
  RS2008/RS1038/RS1036/RS1041 as errors, and the package is split analyzer/code-fix to clear RS1038.

## Verification Baseline

- v1.27.209: 687 tests, green locally. **CC016/CC017 FN fix:** a write-only simple
  assignment no longer counts as token observation while right-hand-side reads still do.
- v1.27.208: 684 tests, green locally. **CC012 FN/fix:** parenthesized `None` and
  `default` arguments are classified while the whole outer expression is replaced.
- v1.27.207: 682 tests, green locally. **CC014 FP fix:** exact framework
  `IDisposable.Dispose()` cast calls count as disposal while unrelated casted calls still report.
- v1.27.206: 680 tests, green locally. **CC027 FN fix:** expression-form
  `using (resource)` returns are diagnosed only within that using statement's scope.
- v1.27.205: 678 tests, green locally. **CC014 FP fix:** parenthesized source references
  retain disposal and conservative escape recognition while non-disposal calls still report.
- v1.27.204: 675 tests, green locally. **CC013 FP fix:** provably zero framework
  `TimeSpan` durations are excluded while runtime-determined sleeps still report.
- v1.27.203: 673 tests, green locally. **CC014 FP fix:** null-forgiven source references
  retain disposal and conservative escape recognition while non-disposal calls still report.
- v1.27.202: 669 tests, green locally. **CC028 FN/fix:** `using static System.IO.File`
  calls are diagnosed and rewritten to the bare async counterpart with token flow.
- v1.27.201: 667 tests, green locally. **CC013 FP fix:** compile-time-zero millisecond
  scheduler yields are excluded while positive and runtime-determined sleeps still report.
- v1.27.200: 665 tests, green locally. **CC020/CC021 FP fix:** reduced extension-method
  receivers count as context handoff while ordinary instance calls remain diagnostic.
- v1.27.199: 661 tests, green locally. **CC010 FP fix:** token-taking custom
  `ConfigureAwait` calls retain their producer token flow while boolean configuration still reports.
- v1.27.198: 660 tests, green locally. **CC028 FN fix:** null-conditional blocking
  `StreamReader`/`StreamWriter` calls are diagnosed while unsupported overloads remain quiet.
- v1.27.197: 658 tests, green locally. **CC014 FN fix:** compile-time-only
  `nameof(cts.Dispose)` no longer suppresses an otherwise undisposed source diagnostic.
- v1.27.196: 657 tests, green locally. **CC026 FN fix:** null-conditional semaphore waits
  are diagnosed while conditional zero-timeout probes remain excluded.
- v1.27.195: 656 tests, green locally. **CC015 FN fix:** null-conditional `Task.Wait`
  calls are diagnosed while conditional zero-timeout probes remain excluded.
- v1.27.194: 655 tests, green locally. **CC015 FN fix:** null-conditional task-like
  `.Result` access is diagnosed; context-dependent null-preserving fixes remain intentionally absent.
- v1.27.193: 654 tests, green locally. **CC026 FP fix:** zero-argument `TimeSpan`
  construction and named target-typed `new()` are excluded as immediate semaphore probes.
- v1.27.192: 653 tests, green locally. **CC015 FP fix:** zero-argument framework `TimeSpan`
  construction and target-typed `new()` are excluded, using exact type-symbol identity.
- v1.27.191: 652 tests, green locally. **CC026 FP fix:** exact `TimeSpan.Zero`, explicit default,
  and target-typed default semaphore timeouts are excluded as immediate try-enter probes.
- v1.27.190: 651 tests, green locally. **CC015 FP fix:** static `Task.WaitAll` and `Task.WaitAny`
  share the semantic zero-timeout exclusion used by instance `Task.Wait`.
- v1.27.189: 650 tests, green locally. **CC015 FP fix:** exact `TimeSpan.Zero`, `default(TimeSpan)`,
  and target-typed `default` timeout probes are excluded while nonzero/factory timeout forms remain.
- v1.27.188: 649 tests, green locally. **CC015 FP fix:** `Task.Wait(0)` is excluded as a guaranteed
  non-blocking completion probe, with semantic binding covering named arguments and integer constants.
- v1.27.187: 648 tests, green locally. **CC022/CC025 FN fix:** top-level statements are recognized as
  async context only when the synthesized entry point contains a top-level `await`; positive and
  purely synchronous negative cases are pinned for both rules.
- v1.27.186: 644 tests, green locally. **CC024 FN fix:** custom void-returning delegates now receive
  the async-void-lambda diagnostic; custom event-handler delegates retain the sanctioned exclusion.
- v1.27.185: 642 tests, green locally. **CC026 FP fix:** `SemaphoreSlim.Wait(0)` is excluded as a
  guaranteed non-blocking probe, with semantic binding covering named arguments and integer constants.
- v1.27.184: 641 tests, green locally. **CC027 FN fix:** a returned async call through an
  interface/base cast of the using-scoped receiver is now diagnosed because the same local is still
  disposed before task completion.
- v1.27.183: 640 tests, green locally. **CC010 FN fix:** a custom method merely named
  `WithCancellation` no longer counts as configured token flow; only the framework API is unwrapped
  as a cancellation-aware enumerable.
- v1.27.182: 639 tests, green locally. **CC005B FP/fix-safety correction:** controller actions
  with externally controlled override or interface signatures are excluded because adding a token
  only to the implementation can break compilation.
- v1.27.181: 638 tests, green locally. **CC020/CC021 FN fix:** compile-time-only
  `nameof(context.CancellationToken)` and `nameof(context.RequestAborted)` references no longer
  suppress the runtime token-observation diagnostics.
- v1.27.180: 636 tests, green locally. **CC009 FN fix:** a loop that mentions
  `token.IsCancellationRequested` only inside compile-time `nameof(...)` is now diagnosed because it
  still performs no runtime cancellation check.
- v1.27.179: 635 tests, green locally. **CC014 FN fix:** undisposed CTS locals in top-level
  programs now use the compilation unit as their synthesized function scope; analyzer and code-fix
  regressions pin the warning and the valid top-level `using var` rewrite.
- v1.27.178: 633 tests, green locally. **CC016 FP fix:** an `await` owned only by a nested lambda
  or local function no longer makes a synchronous containing method eligible for an unused-token
  diagnostic; token references in nested functions still count as captures.
- v1.27.177: 632 tests, green locally. **CC016/CC017 FN fix:** token references contained only
  within `nameof(...)` no longer suppress unused-token diagnostics because they have no runtime
  cancellation effect; real references in nested lambdas and local functions remain observed.
- v1.27.176: 630 tests, green locally. **CC012 FP fix:** a custom token-valued property named
  `None` is no longer treated as `CancellationToken.None`; the framework property must resolve by
  symbol identity before the analyzer offers the in-scope token replacement.
- v1.27.175: 629 tests, green locally. **CC019 FN fix:** `await foreach`, `await using var`, and
  `await using (...)` now count as awaited work in the current `try` scope, closing the syntax gap
  where their await keywords were invisible to the prior `AwaitExpressionSyntax`-only check.
- v1.27.174: 626 tests, green locally. **CC019 FP fix:** an `await` owned by a local function or
  lambda declared inside a `try` no longer makes the enclosing broad catch report; only awaited work
  executed in the current function scope can establish the cancellation-swallowing risk.
- v1.27.173: 625 tests, green locally. **Capstone of the second 100-iteration hardening loop.** This
  run landed three real bug fixes — **CC002/003/004 incompatible-overload FP** (type-compatible token
  overload required, with ordinal-aware generic matching), **CC016 `[EnumeratorCancellation]` FP**, and
  the **CC016/CC014/CC027 escape/receiver** confirmations — plus CC028 write-side coverage
  (`StreamWriter.Write`/`WriteLine`/`Flush`), and a large, diverse battery of cross-analyzer clean-code
  FP guards in `AllAnalyzersCleanCodeTests` covering real-world async shapes (raw Stream I/O, HttpClient
  streaming, Channels producer/consumer + WaitToReadAsync, Parallel.ForEachAsync, PeriodicTimer,
  retry/backoff, semaphore-gated and bounded-concurrency sections, async generators/transform pipelines,
  background-task lifecycle, transaction commit/rollback, ArrayPool, Lazy<Task>, and more). The analyzer
  remains feature-complete and FP-clean; every rule has a clean-code guard and every fixer has
  Fix-All + receiver-correctness coverage.
- v1.27.3x: ~477 tests, green locally. This hardening loop landed three real bug fixes, now reflected
  in the scorecard above: **CC002/003/004 incompatible-overload FP** (require a type-compatible token
  overload; ordinal-aware generic match), **CC016 `[EnumeratorCancellation]` FP** (excluded), plus the
  CC028 write-side coverage (`StreamWriter.Write`/`WriteLine`/`Flush`) and many clean-code FP guards
  (raw Stream/HttpClient, Channels/Parallel.ForEachAsync, library-style async, linked-CTS timeout,
  async-stream producer) and escape/receiver pins (CC014 field-assign, CC027 receiver-vs-argument).
  Note: release tags v1.27.13+ were produced by two concurrent loop instances; the version in this
  file is approximate — the published package version always comes from the release tag.
- v1.27.19: 469 tests (+1 CC027 receiver-vs-argument pin: a helper-produced task with the using
  resource read into an argument is not flagged). Green locally.
- v1.27.18: 468 tests (+1 CC014 field-assignment escape pin). Green locally. (Confirmed CC014 already
  treats assignment-to-field as an escape — no bug; pinned to prevent regression.) Note: v1.27.13–17
  were released concurrently by a parallel loop instance (static-context coverage pins for
  CC013/CC015/CC026/CC028 + lookalike); this build rebased on top of them.
- v1.27.12: 465 tests (+1 cross-cutting clean-code guard: canonical async-stream producer —
  [EnumeratorCancellation] iterator with a cancellation-checked loop — clean across all analyzers).
  Green locally.
- v1.27.11: 464 tests (+1 CC016 FP guard). Green locally. **Real FP fix:** CC016 no longer flags an
  async-iterator `CancellationToken` marked `[EnumeratorCancellation]` as unused — the attribute
  delivers the consumer's `WithCancellation` token to it, so it is observed even without a body
  reference (new `HasEnumeratorCancellation` guard, mirrors CC011's detection).
- v1.27.10: 463 tests (+1 CC028 mixed 3-type Fix-All: File + StreamReader + StreamWriter in one
  batch). Green locally.
- v1.27.9: 461 tests (+1 clean-code FP guard: linked-CTS timeout idiom — CreateLinkedTokenSource +
  CancelAfter + linked.Token). Green locally.
- v1.27.8: 460 tests (+1 CC002 generic-overload-pair fixer pin: appended token binds to the
  generic token overload and compiles). Green locally.
- v1.27.7: 458 tests (457 + 1 CC002 generic-overload-pair regression pin). Green locally. **Fixed a
  regression from 1.27.1:** the type-compatible overload match now compares parameter types with an
  ordinal-aware equivalence (`ParameterTypesEquivalent`), so generic overload pairs like
  `FooAsync<T>(T)` / `FooAsync<T>(T, CancellationToken)` fire again (distinct per-overload type-param
  symbols were wrongly treated as different types).
- v1.27.6: 457 tests (456 + 1 clean-code FP guard: library-style async — ConfigureAwait(false),
  ValueTask, await using with a token-flowing factory, TaskCompletionSource). Green locally.
- v1.27.5: 456 tests (455 + 1 CC028 FP guard: in-memory `StringWriter` stays quiet — not in the
  curated map). Green locally.
- v1.27.4: 455 tests (453 + 2 CC028 `StreamWriter.WriteLine` pins: analyzer fires, fixer →
  `await WriteLineAsync(text)` tokenless). Green locally.
- v1.27.3: 453 tests (452 + 1 clean-code FP guard: `System.Threading.Channels` producer/consumer +
  `Parallel.ForEachAsync`, all threading the token). Green locally. (Confirmed CC009 is deliberately
  strict: an `await foreach` body still needs an explicit `ThrowIfCancellationRequested()` even when
  its source flows the token — matches the existing positive tests.)
- v1.27.2: 452 tests (451 + 1 clean-code FP guard: idiomatic raw-`Stream` async I/O +
  `HttpClient.SendAsync`/`ReadAsStringAsync`, all threading the token). Green locally.
- v1.27.1: 451 tests (449 + 1 CC002 incompatible-token-overload FP guard + 1 idiomatic async
  `StreamWriter` clean-code guard). Green locally. **Real FP fix:** CC002/CC003/CC004 now require a
  *type-compatible* token overload before firing (via the new
  `CancellationTokenHelpers.GetTypeCompatibleTokenParameterName`), so a same-name token overload with
  different parameters (e.g. `StreamWriter.WriteAsync(string)`, whose token overload takes
  `ReadOnlyMemory<char>`) no longer produces a non-compiling propagation fix. Also extended the CC028
  sample with a `StreamWriter` violation + fix (sample-only).
- v1.27.0: 448 tests (443 + 5 CC028 StreamWriter coverage: 3 analyzer — `Write`/`Flush` fire,
  sync-method negative — and 2 fixer — `Flush()`→`await FlushAsync(token)` (token-taking overload),
  `Write(string)`→`await WriteAsync(text)` (no token, no token overload)). Green locally. CC028 extended
  to the write side (`StreamWriter.Write`/`WriteLine`/`Flush`) and hardened to require a
  signature-compatible async counterpart, so the fix always compiles and only flows the token when the
  matched overload accepts one.
- v1.26.9: 443 tests. Green locally. Code-quality fix: resolved three `CS1574` broken-cref build
  warnings (CC010/CC011 `IAsyncEnumerable<T>`, CC025 `IAsyncDisposable` — unresolvable under the
  `netstandard2.0` target); converted to `<c>` formatting so the analyzer assembly builds warning-free.
- v1.26.8: 443 tests (442 + 1 CC028 async-local-function pin). Green locally. **28 rules, fully covered:
  every rule has a clean-code FP guard and every fixer has a Fix-All + receiver-correctness pin.**
- v1.26.7: 442 tests (441 + 1 CC028 mixed-type Fix-All: File + StreamReader in one batch). Green locally.
- v1.26.6: 441 tests (439 + 2 CC028 parenthesization branches: element access, conditional access).
  Green locally.
- v1.26.5: 439 tests (438 + 1). Real fixer bug fixed: CC028 now parenthesizes the await when the
  blocking call is a receiver (`File.ReadAllText(p).Trim()` → `(await ...Async(p, token)).Trim()`).
- v1.26.4: 438 tests (sample-only: CC028 sample completed with the StreamReader before/after). Green locally.
- v1.26.3: 438 tests (436 + 2 CC028 StreamReader-branch negatives: non-curated method, lookalike type).
  Green locally.
- v1.26.2: 436 tests (434 + 2 CC028 fixer pins: StreamReader.ReadLine, File.AppendAllText). Green locally.
- v1.26.1: 434 tests (docs-only: rule count refreshed to 28 across README/health/NEXT_STEPS). Green locally.
- v1.26.0: 434 tests (431 + 3 CC028 StreamReader coverage: analyzer fire/clean + fixer). Green locally.
  CC028 generalised from `System.IO.File` to `System.IO` (now also `StreamReader.ReadToEnd`/`ReadLine`);
  message format `File.<name>` → `<name>`. Type→method map is self-limiting via GetMembers(name+"Async").
- v1.25.2: 431 tests (430 + 1 CC028 cross-analyzer clean FP-guard: idiomatic async File I/O). Green locally.
- v1.25.1: 430 tests (429 + 1 CC028 named-argument fixer safety pin). Green locally. CC028 fixer uses
  the shared `AddTokenArgument` helper so a named-arg call stays valid (`cancellationToken: token`).
- v1.25.0: 429 tests (426 + 3 CC028 code-fix tests incl. Fix-All). Green locally. CC028 now has a code
  fix (`File.<name>` → `await File.<name>Async(..., token)`); README fix mark ✅.
- v1.24.0: 426 tests (420 + 6 for NEW rule CC028). Green locally. CC028 (Warning, analyzer-only) flags
  blocking `System.IO.File` read/write/append helpers in async code when an `<name>Async` counterpart
  exists — extends the blocking-in-async family (CC013/CC015/CC026). 28 rules now: CC001-006, CC009-028.
- v1.23.45: 420 tests (419 + 1 CC013 fully-qualified `System.Threading.Thread.Sleep` fix pin). Green locally.
- v1.23.44: 419 tests (418 + 1 CC012 named-argument fix pin: `token:` name-colon preserved). Green locally.
- v1.23.43: 418 tests (416 + 2 CC001 surface-area pins: `internal` clean, public async in a `record`
  flagged). Green locally.
- v1.23.42: 416 tests (414 + 2 CC015 parenthesized-await-as-receiver correctness pins). Green locally.
- v1.23.41: 414 tests (411 + 3 receiver-agnosticism pins: CC015 field `.Result`, CC022 field `Cancel()`,
  CC026 field-receiver fix). Green locally.
- v1.23.40: 411 tests (409 + 2 FP-guard scenarios: modern C# shapes — primary-constructor class/record
  struct + file-scoped namespace; pattern matching / generics — switch arms, generic async, catch
  filter). Green locally. No FPs surfaced.
- v1.23.39: 409 tests (407 + 2 Fix-All tests for the line-inserting fixers: CC009 loop-guard, CC019
  rethrow-guard). Green locally. **Fix All is now pinned for every fixer in the analyzer** — presence/
  handler, propagation, in-place, import-adding, and line-inserting alike.
- v1.23.38: 407 tests (404 + 3 Fix-All tests for the add-token handler fixers: CC005B, CC005C, CC018).
  Green locally. Fix All now pinned for every fixer family: presence/handler (CC001, CC005B/C, CC018),
  propagation (CC002-CC004), and the in-place/import-adding fixers (CC010-CC015, CC022, CC023, CC025,
  CC026). Only the line-inserting fixers (CC009 loop-guard, CC019 rethrow-guard) remain single-site only.
- v1.23.37: 404 tests (401 + 3 Fix-All tests for the propagation fixers: CC002, CC003, CC004). Green
  locally. Fix All is now pinned for every multi-site fixer: CC001-CC004, CC010-CC015, CC022, CC023,
  CC025, CC026.
- v1.23.36: 401 tests (398 + 3 Fix-All tests: CC015, CC026, CC001 import-dedup). Green locally. Fix All
  is now pinned for CC001, CC010-CC015, CC022, CC023, CC025, CC026.
- v1.23.35: 398 tests (395 + 3 Fix-All tests: CC010, CC022, CC025). Green locally. Fix All is now
  pinned for CC010-CC014, CC022, CC023, CC025 (the import-adding and in-place fixers).
- v1.23.34: 395 tests (392 + 3 Fix-All tests: CC014, CC023 import-dedup, CC012). Green locally.
- v1.23.33: 392 tests (389 + 3: CC011 Fix-All single-import, CC013/CC015 `TimeSpan` overloads).
  Green locally.
- v1.23.32: 389 tests (386 + 3 edge pins: CC011 nested-yield scoping, CC027 using-statement
  expression form, CC009 loop in a capturing lambda). Green locally.
- v1.23.31: 386 tests (383 + 3 edge pins: CC021 alias, CC017 ctor-arg, CC027 aliased-return
  precision boundary). Green locally.
- v1.23.30: 383 tests (380 + 3 framework edge pins: CC017 expression-bodied, CC020 alias, CC018
  static hub method). Green locally.
- v1.23.29: 380 tests (377 + 3 edge pins: CC013 in `async delegate`, CC015/CC026 in an async local
  function). Green locally.
- v1.23.28: 377 tests (374 + 3 edge pins: CC015 ValueTask GetResult, CC024 `Action<T>`, CC010 in a
  capturing local function). Green locally.
- v1.23.27: 374 tests (371 + 3 edge pins: CC016 ctor-arg use, CC019 `throw ex;` rethrow, CC012
  explicit `new`). Green locally.
- v1.23.26: 371 tests (368 + 3 edge pins: CC022/CC026 in an async lambda, CC023 protected async
  void). Green locally.
- v1.23.25: 368 tests (365 + 3 edge pins: CC013 static-import Sleep, CC015 `ValueTask<T>.Result`,
  CC014 target-typed `new` CTS). Green locally.
- v1.23.24: 365 tests (364 + 1 CC012 named-argument coverage). Green locally.
- v1.23.23: docs/samples — added a CC027 sample file (fires on its violation in a clean sample
  build). 364 tests unchanged.
- v1.23.22: docs only — added the missing CC027 README Quick Examples section (all 27 rules now have
  one). 364 tests unchanged.
- v1.23.21: 364 tests (363 + 1 CC005A non-handler look-alike non-FP pin). Green locally.
- v1.23.20: 363 tests (362 + 1 resource-lifecycle clean-code FP guard). Green locally.
- v1.23.19: 362 tests (361 + 1 CC005C→CC002 cascade pin). Green locally.
- v1.23.18: 361 tests (360 + 1 `EveryShippedRule_HasAHelpLink` drift guard). `helpLinkUri` now on all
  27 rules. Green locally.
- v1.23.17: `helpLinkUri` extended to CC016–CC021. No behavior change; 360 tests unchanged.
- v1.23.16: `helpLinkUri` extended to CC010–CC015. No behavior change; 360 tests unchanged.
- v1.23.15: `helpLinkUri` extended to CC005A/B/C, CC006, CC009. No behavior change; 360 tests
  unchanged (descriptor metadata is not matched by the diagnostic verifier).
- v1.23.14: `helpLinkUri` added to CC001–CC004 descriptors (shared `DiagnosticHelp.LinkUri`). No
  behavior change; 360 tests unchanged.
- v1.23.13: 360 tests (359 + 1 CC004 non-HttpClient look-alike non-FP pin). Green locally.
- v1.23.12: 359 tests (358 + 1 CC003 non-EF look-alike non-FP pin). Green locally.
- v1.23.11: 358 tests (357 + 1 CC002 `Task.WhenAll`/`WhenAny` non-FP pin). Green locally.
- v1.23.10: 357 tests (355 + 2 CC001/CC011 cascade — tokenless iterator → only CC001; unmarked-token
  iterator → only CC011). Green locally.
- v1.23.9: 355 tests (354 + 1 CC024 `Task.Run(async () => ...)` non-FP pin). Green locally.
- v1.23.8: docs only — rewrote the stale `NEXT_STEPS.md` roadmap to the current 27-rule state. 354
  tests unchanged.
- v1.23.7: docs only — refreshed stale README sections (Project Quality / Roadmap / Supported
  Frameworks). 354 tests unchanged.
- v1.23.6: 354 tests (353 + 1 CC013 multi-occurrence fixer test — two Thread.Sleep calls both
  rewritten). Green locally.
- v1.23.5: docs only — refreshed the health doc's narrative sections to the 27-rule state. 353 tests
  unchanged.
- v1.23.4: 353 tests (352 + 1 CC027 non-async `using` clean-code FP guard). Green locally.
- v1.23.3: 352 tests (350 + 2 CC001 FP fix — an `async Task Main` entry point is no longer flagged).
  Green locally.
- v1.23.2: 350 tests (349 + 1 CC014 FP fix — `cts?.Dispose()` null-conditional disposal is now
  recognised). Green locally.
- v1.23.1: 349 tests (348 + 1 CC027 using-statement coverage — `using (var r = ...) { return r... }`
  is flagged too). Green locally.
- v1.23.0: 348 tests (343 + 5 for new rule CC027: return-task-from-using-resource positive;
  completed-task-read, non-using-resource, async-await, unrelated-return negatives). Green locally.
- v1.22.13: 343 tests (342 + 1 Minimal API clean-code FP guard). Every rule, including all framework
  rules, is now covered by a clean-code FP guard. Green locally.
- v1.22.12: 342 tests (341 + 1 MediatR/SignalR clean-code FP guard — tokenized handler + hub method
  produce zero diagnostics). Green locally.
- v1.22.11: 341 tests (340 + 1 controller clean-code FP guard — a tokenized `[HttpGet]` action
  satisfies CC001 + CC005B with zero diagnostics). Green locally.
- v1.22.10: 340 tests (339 + 1 CC024 anonymous-method coverage: `async delegate { }` converted to
  `Action` is now flagged). Green locally.
- v1.22.9: 339 tests (337 + 2 CC023 local-function coverage: an `async void` local function is
  flagged and the fix changes its return type to `Task`). Green locally.
- v1.22.8: 337 tests (336 + 1 exotic-syntax clean-code FP guard — switch expressions / expression
  bodies / non-async Task methods produce zero diagnostics). Green locally.
- v1.22.7: docs/samples only — sample files for CC022–CC026 (each fires on its `Bad` member in a
  clean sample build). 336 tests unchanged.
- v1.22.6: 336 tests (335 + 1 nested-scope clean-code FP guard — captured tokens in a local function
  and a lambda produce zero diagnostics). Green locally.
- v1.22.5: docs only — packaged README "Quick Examples" sections for CC020–CC026 (all 26 rules now
  have a runnable example). 335 tests unchanged.
- v1.22.4: 335 tests (334 + 1 framework clean-code FP guard — BackgroundService + gRPC overrides
  across all analyzers = zero diagnostics). Green locally.
- v1.22.3: 334 tests (331 + 3 CC009 FP fix: a cancellation check in the loop *condition*
  (while/for/do-while) now satisfies the rule — surfaced while building a BackgroundService-style
  clean-code sample). Green locally.
- v1.22.2: 331 tests (329 + 2 CC026 coverage: `Wait(timeout)` and `Wait(token)` now flagged; fixer
  carries the original args through to `WaitAsync`). Green locally.
- v1.22.1: 329 tests (328 + 1 cross-analyzer clean-code FP guard — all 26 analyzers run together
  over idiomatic async code produce zero diagnostics). Green locally.
- v1.22.0: 328 tests (322 + 6 for new rule CC026: Wait-in-async positive; sync-method,
  Wait(timeout), non-semaphore negatives; and 2 fixer tests — with/without in-scope token). Green.
- v1.21.0: 322 tests (316 + 6 for new rule CC025: using-declaration and using-statement positives;
  await-using, sync-disposable, sync-method negatives; and a fixer test). Net90 refs for
  IAsyncDisposable. Green locally.
- v1.20.0: 316 tests (311 + 5 for new rule CC024: async-lambda-to-Action and
  passed-where-Action-expected positives; Func<Task>, sync-Action, EventHandler negatives). Green.
- v1.19.1: 311 tests (309 + 2 CC015 coverage: `Wait(timeout)` and `Task.WaitAll(...)` now flagged;
  fixer guarded to the parameterless `Wait()`). Green locally.
- v1.19.0: 309 tests (303 + 6 for new rule CC023: async-void positive; event-handler,
  custom-EventArgs-handler, async-Task, sync-void negatives; and a fixer test that changes
  `void`→`Task` and adds the import). Green locally.
- v1.18.1: 303 tests (302 + 1 CC010 fixer hardening: an awaited source is now parenthesized before
  `.WithCancellation`, fixing a mis-bound fix). Green locally.
- v1.18.0: 302 tests (297 + 5 for new rule CC022: Cancel-in-async positive; sync-method,
  Cancel(bool)-overload, non-CancellationTokenSource negatives; and a fixer test). Net90 refs for
  CancelAsync. Green locally.
- v1.17.0: 297 tests (295 + 2 CC019 fixer: named-exception adds rethrow guard, unnamed adds the
  variable too). CC019 is no longer analyzer-only. Green locally.
- v1.16.1: 295 tests (294 + 1 CC012 target-typed-`new` coverage). `BaseObjectCreationExpressionSyntax`
  now covers both `new T(...)` and `new(...)`. Green locally.
- v1.16.0: 294 tests (289 + 5 for new rule CC021: ignores-RequestAborted positive; observes-token,
  passes-context-on, no-async-work, non-HttpContext negatives). CC020 refactored onto the shared
  context-probe helpers (no behavior change). Green locally.
- v1.15.0: 289 tests (284 + 5 for new rule CC020: ignores-token positive; observes-token,
  passes-context-on, no-async-work, non-gRPC negatives). Uses a ServerCallContext stub. Green.
- v1.14.4: 284 tests (281 + 3 CC001 async-iterator coverage: public-async-iterator-without-token
  positive, with-token and private-iterator negatives). Closes the FN where a tokenless public
  `async IAsyncEnumerable<T>` was flagged by neither CC001 nor CC011. Green locally.
- v1.14.3: refactor only — CC005A now uses `CancellationTokenHelpers.HasCancellationTokenParameter`
  / `IsAsyncReturnType` instead of hand-rolled checks. No behavior change; 281 tests unchanged.
- v1.14.2: analyzer XML docs only — added `<remarks>`/`<example>` blocks to CC003/CC004/CC005A/CC005B
  (P3 closure). No behavior change; 281 tests unchanged.
- v1.14.1: docs/samples only — README example sections for CC016–CC019 and sample files for CC016 /
  CC019. 281 tests unchanged; sample project compiles (clean rebuild fires CC016/CC019 on the
  intended `Bad` members only).
- v1.14.0: 281 tests (274 + 7 for new rule CC019: catch (Exception) and catch-all positives;
  rethrow, when-filter, specific-type, no-await, catch (OperationCanceledException) negatives).
  Green locally.
- v1.13.0: 274 tests (268 + 6 for new rule CC018: missing-token positive; with-token,
  OnConnectedAsync-override, non-hub, private-method negatives; and a fixer test that the shared
  add-token-parameter fix applies to CC018). Tests use a faithful Hub stub. Green locally.
- v1.12.0: 268 tests (264 + 4 for new rule CC017: ignores-stopping-token positive;
  observes-token, non-BackgroundService, passes-token-to-helper negatives). Uses the
  `Microsoft.Extensions.Hosting.Abstractions` 9.0.0 package in tests. Green locally.
- v1.11.0: 264 tests (258 + 6 for new rule CC016: async-unused-token and local-function positives;
  used-token, sync-method, interface-implementation, used-inside-lambda negatives). Green locally.
- v1.10.2: 258 tests (256 + 2 CC015 hardening: a `ConfigureAwait(false).GetAwaiter().GetResult()`
  positive and its fixer producing `(await task.ConfigureAwait(false))`). Recognises configured
  awaiters, not just bare ones. Green locally.
- v1.10.1: docs/samples only — added README per-rule example sections and
  `samples/CancelCop.Sample` files for CC010–CC015 (each a violation + fix). No analyzer change;
  256 tests unchanged, sample project compiles (intended CC010–CC015 warnings on build).
- v1.10.0: 256 tests (248 + 8 for new rule CC015: 5 analyzer — Result/Wait/GetAwaiter-GetResult
  positives, sync-method and non-task negatives — and 3 fixer: each form → await). Green locally.
- v1.9.0: 248 tests (239 + 9 for new rule CC014: 7 analyzer — never-disposed and linked-source
  positives; using-declaration, disposed, returned, passed-as-argument, captured-by-lambda
  negatives — and 2 fixer: new and linked source → `using` declaration). Green locally.
- v1.8.1: 239 tests (237 + 2 CC010 hardening: a `ConfigureAwait(false)`-without-`WithCancellation`
  positive and its fixer, which inserts `.WithCancellation(token)` before `.ConfigureAwait`). Green.
- v1.8.0: 237 tests (230 + 7 for new rule CC013: 5 analyzer — async-method-with-token/async-method-
  without-token/async-lambda positives, sync-method and sync-lambda-in-async negatives — and 2
  fixer: with-token and without-token rewrites). Green locally.
- v1.7.0: 230 tests (222 + 8 for new rule CC012: 6 analyzer — None/default/default(CancellationToken)
  positives, no-token-in-scope/real-token/non-token-parameter negatives — and 2 fixer: None→token
  and default→differently-named token). Green locally.
- v1.6.0: 222 tests (214 + 8 for new rule CC011: 6 analyzer — unmarked-token positive,
  marked-token/no-token/non-iterator/second-token-marked negatives, local-function positive —
  and 2 fixer: add attribute+import, add attribute when import already present). Green locally.
- v1.5.0: 214 tests (205 + 9 for new rule CC010: 7 analyzer — async-enumerable positive,
  with-cancellation/no-token-in-scope/synchronous-foreach/producer-already-passes-token/configured-
  cancelable negatives, lambda-scope positive — and 2 fixer: identifier source and invocation
  source). `dotnet test CancelCop.sln -c Release` green locally (SAC blocker lifted 2026-06-13).
- v1.4.8: 205 tests (200 + 5 `RuleCatalogTests` drift guards), verified via CI (`build-and-test`).
- v1.4.7: 200 tests, pure refactor verified via CI (`build-and-test`).
- v1.4.6: 200 tests (196 after v1.4.5 + 4 named-argument fixer tests incl. the overload-name
  trap case) — verified via CI (`build-and-test`) because local test execution is currently
  blocked (see below).
- `dotnet test CancelCop.sln` — 196 passed, 0 failed after the constructor/primary-constructor
  scope support and its review hardening (184 after v1.4.4 + 12 new tests: 9 CC002 incl.
  record/static/CS9105/static-event-field negatives and a partial-type positive, 1 CC003
  constructor, 1 CC004 primary-constructor, 1 CC009 primary-constructor).
- **Local runtime limitation (2026-06-09):** Windows Smart App Control entered full enforcement on
  this machine mid-session and now blocks freshly built unsigned test DLLs
  (`FileLoadException … Application Control policy has blocked this file`, 0x800711C7). Local
  `dotnet test` is unavailable until SAC is relaxed; CI remains the verification baseline.
- `dotnet test … --filter FullyQualifiedName~MinimalApi` — 34 passed (18 prior + 10 analyzer tests:
  method group/member-access/local-function/generic/parenthesized positives,
  with-token/synchronous/delegate-variable/delegate-Invoke/metadata negatives + 6 fixer tests:
  method, local function, fix-targets-method-not-enclosing-lambda, virtual no-fix, partial no-fix,
  Fix All shared handler).
- `dotnet test … --filter "FullyQualifiedName~EFCore|FullyQualifiedName~HttpClient"` — 39 passed
  (27 prior + 12 new: local-function/lambda/captured-token positives, no-token and static-function
  negatives, anonymous-method positive, and an EF expression-tree negative).
- `dotnet test … --filter FullyQualifiedName~TokenPropagationAnalyzer` — 17 passed (15 prior +
  static-lambda negative + anonymous-method positive).
- Local SDK: .NET 10.0.300; `global.json` pins `10.0.300`. Tests target `net10.0`.
- Note: the Roslyn-testing NuGet cache at `%TEMP%\test-packages` can become torn (missing nuspec /
  half-deleted package dirs) and fail every test with packaging exceptions; deleting the whole
  folder and re-running restores it.
