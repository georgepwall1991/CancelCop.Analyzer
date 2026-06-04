# Analyzer Health

Reviewed: 2026-06-04 (post v1.4.0, during the v1.4.1 hardening loop)

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
| CC002 | CancellationToken not propagated | Usage | Warning | 3 | 4 | 4 | 4 | 3 | 4 | **Medium** | Walks local functions + containing method, but **not lambdas** — yet the XML docs claim "checks … lambda expressions for token availability." Silent false negative + docs drift. CC009 already walks lambdas; CC002 should mirror it. |
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
| Medium | CC002, CC003, CC004 | Scope-walking gaps: CC002 ignores lambdas (and its docs claim otherwise); CC003/CC004 ignore local functions and lambdas. All three are silent false negatives that CC009's scope walk already solves. |
| Low | CC001, CC005A, CC005B, CC005C, CC006, CC009 | Currently acceptable or low-impact. Improve opportunistically. |

## Prioritized Fix Backlog

Grading: **P0** = release-blocking; **P1** = next hardening loop; **P2** = opportunistic; **P3** = directional.

### P0 — Release-blocking
- _None._

### P1 — Next hardening loop
- **CC002 lambda scope + docs drift.** `FindContainingCancellationTokenParameter` walks
  `LocalFunctionStatementSyntax` and `MethodDeclarationSyntax` but not `LambdaExpressionSyntax`, so a
  `Task.Delay(…)`/custom async call inside an async lambda that owns a `CancellationToken` parameter is
  never flagged — even though the XML docs explicitly promise lambda support. Mirror CC009's walk and
  add tests. _(Loop 2 target.)_

### P2 — Opportunistic
- **CC003 / CC004 scope consistency.** Replace `FirstAncestorOrSelf<MethodDeclarationSyntax>` with the
  shared local-function/lambda-aware walk so EF Core / HttpClient calls inside local functions and
  lambdas are covered. Extract the walk into `CancellationTokenHelpers` so CC002/CC003/CC004/CC009 share
  one implementation.
- **CC005C method-group handlers.** Minimal APIs accept method groups (`app.MapGet("/", Handler)`), not
  just lambdas. Analyse the referenced method's signature for a token parameter.

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
  `HasOverloadWithCancellationToken`, `IsSignatureExternallyControlled`). CC005A is the one analyzer
  still hand-rolling a token check; CC002/CC003/CC004/CC009 each hand-roll the containing-scope walk.
- Diagnostic placement is good: CC001/CC005A/CC005B on the method identifier, CC002/CC003/CC004 on the
  invoked member name, CC006 on the offending parameter, CC009 on the loop keyword.
- Release tracking (`AnalyzerReleases.Shipped.md` / `.Unshipped.md`) is wired as `AdditionalFiles` with
  RS2008/RS1038/RS1036/RS1041 as errors, and the package is split analyzer/code-fix to clear RS1038.

## Verification Baseline

- `dotnet test CancelCop.sln` — 149 passed, 0 failed after the CC005C hardening (147 pre-v1.4.1
  baseline + 2 new negative tests + 1 new positive endpoint-module test, net of the unchanged total).
- `dotnet test … --filter FullyQualifiedName~MinimalApi` — 18 passed after the CC005C hardening
  (15 prior + 2 negative tests for non-endpoint `MapGet` lookalikes + 1 positive test for the
  `this IEndpointRouteBuilder` endpoint-module idiom).
- Local SDK: .NET 10.0.300; `global.json` pins `10.0.300`. Tests target `net10.0`.
