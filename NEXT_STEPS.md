# CancelCop — Roadmap & Status

> This file used to track the early (v1.0–v1.3) feature plan. That plan is delivered. The live,
> authoritative records are now:
> - **[CHANGELOG.md](CHANGELOG.md)** — every release and what changed.
> - **[docs/ANALYZER_HEALTH.md](docs/ANALYZER_HEALTH.md)** — the per-rule health scorecard and the
>   prioritized fix backlog.

## Current status

CancelCop ships **28 rules** (CC001–CC006, CC009–CC028) covering:

- **Token presence** — public async methods (CC001), MediatR/controller/Minimal-API/SignalR handlers
  (CC005A/B/C, CC018), async iterators (CC001 + CC011).
- **Token propagation** — general (CC002), EF Core (CC003), HttpClient (CC004).
- **Token positioning** — CancellationToken should be last (CC006).
- **Loop checks** — loops should observe cancellation (CC009).
- **Async streams** — `await foreach` should flow a token (CC010); async-iterator
  `[EnumeratorCancellation]` (CC011).
- **Token misuse** — explicit `None`/`default` when a token is in scope (CC012); unused token
  parameter (CC016).
- **Blocking sync-over-async** — `Thread.Sleep` (CC013), `.Result`/`.Wait()`/`GetAwaiter().GetResult()`
  (CC015), `SemaphoreSlim.Wait()` (CC026), blocking `File`/`StreamReader` I/O (CC028).
- **Resource lifecycle** — undisposed `CancellationTokenSource` (CC014), prefer `CancelAsync()`
  (CC022), prefer `await using` (CC025), returned task uses a disposed `using` resource (CC027).
- **Async hygiene** — swallowed `OperationCanceledException` (CC019), `async void` (CC023),
  async-void lambdas (CC024).
- **Framework cancellation sources** — `BackgroundService.ExecuteAsync` (CC017),
  `ServerCallContext.CancellationToken` (CC020), `HttpContext.RequestAborted` (CC021).

The originally-planned items have all shipped (under their final IDs): `CancellationToken.None` misuse
→ CC012, unused token parameters → CC016, async void → CC023.

## How the project evolves now

Each release is a small, verified step: a new rule when a common cancellation pitfall surfaces, a
false-positive or false-negative fix found by reviewing a rule against representative code, expanded
test coverage, or documentation upkeep. Every rule is covered by a cross-analyzer false-positive
guard (`AllAnalyzersCleanCodeTests`) and by `RuleCatalogTests` drift guards that keep the README rule
table, `AnalyzerReleases.Shipped.md`, and the code-fix mapping in lockstep.

See [CONTRIBUTING.md](CONTRIBUTING.md) for the development workflow.
