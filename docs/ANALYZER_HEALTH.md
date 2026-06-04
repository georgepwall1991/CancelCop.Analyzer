# Analyzer Health

Reviewed: 2026-06-04 (refreshed through the v1.4.2 hardening loop)

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
| CC003 | EF Core async call missing CancellationToken | Usage | Warning | 3 | 4 | 4 | 4 | 3 | 4 | Medium | Namespace-gated to `Microsoft.EntityFrameworkCore`, overload-checked. Resolves the containing method via `FirstAncestorOrSelf<MethodDeclarationSyntax>`, so a call inside a local function or lambda with its own token is missed (false negative) — inconsistent with CC002/CC009. No analyzer XML doc. |
| CC004 | HttpClient async call missing CancellationToken | Usage | Warning | 3 | 4 | 4 | 4 | 3 | 4 | Medium | Type-gated to `System.Net.Http.HttpClient`, overload-checked. Same containing-method-only scope gap as CC003. No analyzer XML doc. |
| CC005A | MediatR handler missing CancellationToken | Usage | Warning | 3 | 4 | 4 | 4 | 3 | 2 | Low | Gated to `MediatR.IRequestHandler.Handle`. Real MediatR's interface already mandates the token, so the rule mostly assists a non-compiling handler rather than catching a live omission — low product importance. Uses an inline token check instead of the shared helper. |
| CC005B | Controller action missing CancellationToken | Usage | Warning | 4 | 4 | 4 | 4 | 3 | 4 | Low | Heavily hardened in v1.4.0: public non-static, `ControllerBase`/`Controller` by namespace, inherited `[NonAction]`, MVC HTTP-method attribute by identity + subclass. Conservative and accurate. |
| CC005C | Minimal API handler missing CancellationToken | Usage | Warning | 4 | 4 | 4 | 4 | 3 | 4 | Low | **v1.4.1:** now gated on the receiver implementing `Microsoft.AspNetCore.Routing.IEndpointRouteBuilder`, so an unrelated `MapGet`-named method no longer false-positives (closes the last name-only match in the CC005 family). Remaining false negatives (both pre-existing, low value): method-group handlers (`app.MapGet("/", Handler)`) and the unreduced static-call form (`EndpointRouteBuilderExtensions.MapGet(app, …)`) are not analysed. |
| CC006 | CancellationToken should be last parameter | Style | Info | 4 | 4 | n/a | 4 | 3 | 2 | Low | v1.4.0: methods, constructors, primary constructors, local functions; excludes externally-controlled signatures and unmovable tokens (before trailing `params`, extension `this`). Analyzer-only by design (reordering would touch every call site). Convention rule, low importance. |
| CC009 | Loop missing cancellation check | Usage | Warning | 4 | 4 | 4 | 4 | 4 | 4 | Low | v1.4.0: semantic receiver resolution (no name matching), walks methods/local functions/lambdas, all four loop kinds, fixer inserts `ThrowIfCancellationRequested()`. The strongest rule in the set. |

## Planning Shortlist

| Priority | Rules | Work |
| --- | --- | --- |
| High | None | No rule has a correctness defect severe enough to block a release. |
| Medium | CC003, CC004 | Scope-walking gap: both ignore local functions and lambdas (`FirstAncestorOrSelf<MethodDeclarationSyntax>`), a silent false negative that the shared `FindEnclosingCancellationTokenParameter` (now used by CC002/CC009) solves. |
| Low | CC001, CC002, CC005A, CC005B, CC005C, CC006, CC009 | Currently acceptable or low-impact. Improve opportunistically. |

## Prioritized Fix Backlog

Grading: **P0** = release-blocking; **P1** = next hardening loop; **P2** = opportunistic; **P3** = directional.

### P0 — Release-blocking
- _None._

### P1 — Next hardening loop
- **CC003 / CC004 scope consistency.** Both resolve the containing scope with
  `FirstAncestorOrSelf<MethodDeclarationSyntax>`, so an EF Core / HttpClient call inside a local function
  or lambda with its own token is missed (false negative) — inconsistent with CC002/CC009. Adopt the
  shared `CancellationTokenHelpers.FindEnclosingCancellationTokenParameter` (added in v1.4.2) so all four
  rules share one scope walk. _(Loop 3 target.)_

### P2 — Opportunistic
- **CC005C method-group handlers.** Minimal APIs accept method groups (`app.MapGet("/", Handler)`), not
  just lambdas. Analyse the referenced method's signature for a token parameter.

### Resolved
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
  `HasOverloadWithCancellationToken`, `IsSignatureExternallyControlled`, and — as of v1.4.2 —
  `FindEnclosingCancellationTokenParameter`, the scope walk now shared by CC002 and CC009). CC005A
  still hand-rolls its token check; CC003/CC004 still use the shallower
  `FirstAncestorOrSelf<MethodDeclarationSyntax>` walk (P1 backlog).
- Diagnostic placement is good: CC001/CC005A/CC005B on the method identifier, CC002/CC003/CC004 on the
  invoked member name, CC006 on the offending parameter, CC009 on the loop keyword.
- Release tracking (`AnalyzerReleases.Shipped.md` / `.Unshipped.md`) is wired as `AdditionalFiles` with
  RS2008/RS1038/RS1036/RS1041 as errors, and the package is split analyzer/code-fix to clear RS1038.

## Verification Baseline

- `dotnet test CancelCop.sln` — 154 passed, 0 failed after the CC002 lambda-scope fix (150 after
  v1.4.1 + 4 new CC002 tests).
- `dotnet test … --filter FullyQualifiedName~TokenPropagationAnalyzer` — 15 passed (11 prior + 4 new:
  lambda-owns-token, lambda-captures-outer-token, lambda-already-propagates negative, and
  lambda-inside-`Expression<>`-tree negative — the last guards against firing on non-executable
  expression-tree code, surfaced during review).
- `dotnet test … --filter FullyQualifiedName~LoopCancellation` — unchanged and green after CC009 was
  migrated to the shared scope walk (refactor, no behaviour change).
- `dotnet test … --filter FullyQualifiedName~MinimalApi` — 18 passed after the v1.4.1 CC005C hardening
  (15 prior + 2 negative `MapGet`-lookalike tests + 1 positive `this IEndpointRouteBuilder` test).
- Local SDK: .NET 10.0.300; `global.json` pins `10.0.300`. Tests target `net10.0`.
