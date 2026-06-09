# Analyzer Health

Reviewed: 2026-06-09 (refreshed through the v1.4.6 hardening loop)

A deliberately harsh health audit for the nine implemented CancelCop rule IDs (CC001–CC006, CC009).
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
| CC002 | CancellationToken not propagated | Usage | Warning | 4 | 4 | 4 | 4 | 4 | 4 | Low | **v1.4.2:** now walks lambdas in addition to local functions and the containing method, via the shared `CancellationTokenHelpers.FindEnclosingCancellationTokenParameter` (also used by CC009). Closes the lambda false negative its docs already promised; docs now match behaviour. Expression-tree lambdas (`Expression<TDelegate>`) are deliberately excluded — code there is non-executable data. |
| CC003 | EF Core async call missing CancellationToken | Usage | Warning | 4 | 4 | 4 | 4 | 3 | 4 | Low | **v1.4.3:** now uses the shared `FindEnclosingCancellationTokenParameter` scope walk (local functions, lambdas, containing method) plus CC002's expression-tree guard, closing the scope-gap false negative and aligning all four propagation rules on one walk. Namespace-gated to `Microsoft.EntityFrameworkCore`, overload-checked. No analyzer XML doc (P3). |
| CC004 | HttpClient async call missing CancellationToken | Usage | Warning | 4 | 4 | 4 | 4 | 3 | 4 | Low | **v1.4.3:** same shared scope walk + expression-tree guard as CC003. Type-gated to `System.Net.Http.HttpClient`, overload-checked. No analyzer XML doc (P3). |
| CC005A | MediatR handler missing CancellationToken | Usage | Warning | 3 | 4 | 4 | 4 | 3 | 2 | Low | Gated to `MediatR.IRequestHandler.Handle`. Real MediatR's interface already mandates the token, so the rule mostly assists a non-compiling handler rather than catching a live omission — low product importance. Uses an inline token check instead of the shared helper. |
| CC005B | Controller action missing CancellationToken | Usage | Warning | 4 | 4 | 4 | 4 | 3 | 4 | Low | Heavily hardened in v1.4.0: public non-static, `ControllerBase`/`Controller` by namespace, inherited `[NonAction]`, MVC HTTP-method attribute by identity + subclass. Conservative and accurate. |
| CC005C | Minimal API handler missing CancellationToken | Usage | Warning | 4 | 4 | 4 | 4 | 3 | 4 | Low | **v1.4.4:** method-group handlers (`app.MapGet("/", Handler)`, `Handlers.Get`, local functions) are now analysed and fixed (token added to the referenced declaration, `= default`, same-document only). v1.4.1 gated the receiver on `IEndpointRouteBuilder`. Remaining false negative (pre-existing, low value): the unreduced static-call form (`EndpointRouteBuilderExtensions.MapGet(app, …)`). |
| CC006 | CancellationToken should be last parameter | Style | Info | 4 | 4 | n/a | 4 | 3 | 2 | Low | v1.4.0: methods, constructors, primary constructors, local functions; excludes externally-controlled signatures and unmovable tokens (before trailing `params`, extension `this`). Analyzer-only by design (reordering would touch every call site). Convention rule, low importance. |
| CC009 | Loop missing cancellation check | Usage | Warning | 4 | 4 | 4 | 4 | 4 | 4 | Low | v1.4.0: semantic receiver resolution (no name matching), walks methods/local functions/lambdas, all four loop kinds, fixer inserts `ThrowIfCancellationRequested()`. The strongest rule in the set. |

## Planning Shortlist

| Priority | Rules | Work |
| --- | --- | --- |
| High | None | No rule has a correctness defect severe enough to block a release. |
| Medium | None | The CC003/CC004 scope-walk gap was closed in v1.4.3; no medium-priority rule remains. |
| Low | CC001, CC002, CC003, CC004, CC005A, CC005B, CC005C, CC006, CC009 | Currently acceptable or low-impact. Improve opportunistically. |

## Prioritized Fix Backlog

Grading: **P0** = release-blocking; **P1** = next hardening loop; **P2** = opportunistic; **P3** = directional.

### P0 — Release-blocking
- _None._

### P1 — Next hardening loop
- **Extract the shared report pipeline** *(promoted from P2 — last structural debt item; loop 7
  target)*. CC002/CC003/CC004 end in a near-verbatim block (token-argument check → scope walk →
  expression-tree guard → overload check → properties → report); the named-argument loop added a
  fourth copy of the properties block. One helper would prevent the drift class this loop series
  keeps fixing.

### P2 — Opportunistic
- **Dedupe the add-token-to-declaration recipe.** The CC005C method-group fix and the CC001 fix
  both build `CancellationToken cancellationToken = default` and insert it via
  `InsertTokenParameter`; the method-group symbol resolution (symbol-or-single-candidate) is also
  duplicated between `MinimalApiAnalyzer` and `MinimalApiCodeFixProvider`. Shared helpers would
  keep analyzer and fixer matching in lockstep.
- **CC005C → CC002 cascade.** Applying the CC005C method-group fix gives the handler a token that
  its body does not yet propagate, so CC002/CC003/CC004 fire next — an intentional guided sequence,
  but worth documenting (and a combined-analyzer test would pin it).

### Resolved
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
- **CC005A product value.** Document that CC005A mainly assists a handler that does not yet satisfy the
  MediatR interface; switch its inline token check to `CancellationTokenHelpers`.
- **Analyzer XML docs.** CC003, CC004, CC005A, CC005B lack the `<remarks>`/`<example>` doc blocks that
  CC001/CC002/CC009 carry.
- **Rule-catalog trust contract.** There is no test asserting every shipped analyzer's descriptor is
  documented in README + `AnalyzerReleases.Shipped.md` (AutoMapper's `RuleCatalogTests` is the model).
  A drift guard would catch undocumented or renamed rules.

## Cross-Cutting Findings

- Every analyzer calls `ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None)` and
  `EnableConcurrentExecution()` — correct and consistent.
- Shared logic lives in `CancellationTokenHelpers` (`IsCancellationToken`, `IsAsyncReturnType`,
  `HasOverloadWithCancellationToken`, `IsSignatureExternallyControlled`, and
  `FindEnclosingCancellationTokenParameter` — the scope walk shared by CC002, CC003, CC004, and CC009
  as of v1.4.3). CC005A still hand-rolls its token check (P3 backlog).
- Diagnostic placement is good: CC001/CC005A/CC005B on the method identifier, CC002/CC003/CC004 on the
  invoked member name, CC006 on the offending parameter, CC009 on the loop keyword.
- Release tracking (`AnalyzerReleases.Shipped.md` / `.Unshipped.md`) is wired as `AdditionalFiles` with
  RS2008/RS1038/RS1036/RS1041 as errors, and the package is split analyzer/code-fix to clear RS1038.

## Verification Baseline

- v1.4.6: 199 tests (196 after v1.4.5 + 3 named-argument fixer tests) — verified via CI
  (`build-and-test`) because local test execution is currently blocked (see below).
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
