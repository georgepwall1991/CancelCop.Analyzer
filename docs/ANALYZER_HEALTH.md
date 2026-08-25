# Analyzer Health

Reviewed: 2026-07-27 (refreshed through CC033 / v1.34.0; await-insertion fix-safety sweep in v1.31.0; CC001 iterator-fix correction in v1.35.0)

A deliberately harsh health audit for the fifty implemented CancelCop rule IDs (CC001–CC006, CC009–CC050).
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
| CC001 | Public async method missing CancellationToken | Usage | Warning | 4 | 4 | 4 | 4 | 4 | 4 | Low | Public/protected async returning Task/ValueTask **or an async-iterator type**, excludes override/interface/extern signatures (v1.4.0), compilable fixer (using insertion, name-collision, `params`). **v1.35.0 (fix):** on an async iterator the fixer now adds `[EnumeratorCancellation]` and its import as well — a bare token there is ignored by the generated `GetAsyncEnumerator`, so the fix used to trade CC001 for CC011 and leave the stream uncancellable. Pinned by a test that runs CC011 over the fixed output. **v1.39.0:** convention middleware `Invoke`/`InvokeAsync` whose first parameter is `Microsoft.AspNetCore.Http.HttpContext` is skipped — adding a token parameter is not injected by the pipeline (remaining half of issue #1). Solid entry-point guard. |
| CC002 | CancellationToken not propagated | Usage | Warning | 4 | 4 | 4 | 4 | 4 | 4 | Low | **v1.4.2:** walks lambdas/local functions/containing method via the shared `FindEnclosingCancellationToken` (also CC009); expression-tree lambdas excluded. **v1.39.0:** the walk also sees `HttpContext.RequestAborted` and `ServerCallContext.CancellationToken` when no token parameter is in scope (parameter still wins when both exist); fixers emit the member-access expression. **v1.27.1/v1.27.7 (FP+fix):** firing now requires a *type-compatible* token overload (`GetTypeCompatibleTokenParameterName`) — case A (a sibling overload whose non-token params match the call by type) or case B (the bound overload's own omitted optional token). A merely-same-name token overload with different params no longer yields a non-compiling fix (e.g. `StreamWriter.WriteAsync(string)`, whose token overload takes `ReadOnlyMemory<char>`, is left alone). Parameter types compare with an ordinal-aware equivalence so generic overload pairs (`FooAsync<T>(T)` / `FooAsync<T>(T, CancellationToken)`) still fire. **v1.27.214:** explicit arguments are classified by their parameter-converted type, so a token boxed into `object` does not masquerade as propagation while contextual `default` remains recognized. |
| CC003 | EF Core async call missing CancellationToken | Usage | Warning | 4 | 4 | 4 | 4 | 4 | 4 | Low | **v1.4.3:** now uses the shared `FindEnclosingCancellationTokenParameter` scope walk (local functions, lambdas, containing method) plus CC002's expression-tree guard, closing the scope-gap false negative and aligning all four propagation rules on one walk. Namespace-gated to `Microsoft.EntityFrameworkCore`, overload-checked. Class-level XML `<remarks>`/`<example>` doc present (v1.14.2). |
| CC004 | HttpClient async call missing CancellationToken | Usage | Warning | 4 | 4 | 4 | 4 | 4 | 4 | Low | **v1.4.3:** same shared scope walk + expression-tree guard as CC003. Type-gated to `System.Net.Http.HttpClient`, overload-checked. Class-level XML `<remarks>`/`<example>` doc present (v1.14.2). |
| CC005A | MediatR handler missing CancellationToken | Usage | Warning | 3 | 4 | 4 | 4 | 3 | 2 | Low | Gated to `MediatR.IRequestHandler.Handle`. Real MediatR's interface already mandates the token, so the rule mostly assists a non-compiling handler rather than catching a live omission — low product importance. Uses the shared `HasCancellationTokenParameter`/`IsAsyncReturnType` helpers (moved off the hand-rolled checks in v1.14.3); only the `IRequestHandler.Handle` gating is rule-specific. |
| CC005B | Controller action missing CancellationToken | Usage | Warning | 4 | 4 | 4 | 4 | 3 | 4 | Low | Heavily hardened in v1.4.0: public non-static, `ControllerBase`/`Controller` by namespace, inherited `[NonAction]`, MVC HTTP-method attribute by identity + subclass. **v1.27.182:** externally controlled override/interface signatures are excluded so the suggested parameter addition cannot break their contract. **v1.27.221:** MVC `[AcceptVerbs(...)]` actions are analyzed and fixed while namespace lookalikes remain excluded. Conservative and accurate. |
| CC005C | Minimal API handler missing CancellationToken | Usage | Warning | 4 | 4 | 4 | 4 | 3 | 4 | Low | **v1.4.4/v1.27.220:** method-group handlers (`app.MapGet("/", Handler)`, `Handlers.Get`, local functions) and positional unreduced static calls (`EndpointRouteBuilderExtensions.MapGet(app, …)`) are analysed and fixed. Static admission requires the exact framework extension type plus an `IEndpointRouteBuilder` argument; named/reordered calls and lookalikes remain quiet. v1.4.1 gated reduced receivers on `IEndpointRouteBuilder`. |
| CC006 | CancellationToken should be last parameter | Style | Info | 4 | 4 | n/a | 4 | 3 | 2 | Low | v1.4.0: methods, constructors, primary constructors, local functions; excludes externally-controlled signatures and unmovable tokens (before trailing `params`, extension `this`). Analyzer-only by design (reordering would touch every call site). Convention rule, low importance. |
| CC009 | Loop missing cancellation check | Usage | Warning | 4 | 4 | 4 | 4 | 4 | 4 | Low | v1.4.0: semantic receiver resolution (no name matching), walks methods/local functions/lambdas, all four loop kinds, fixer inserts `ThrowIfCancellationRequested()`. **v1.27.180/v1.27.212:** compile-time-only `nameof(token.IsCancellationRequested)` and checks deferred to a nested lambda/local function no longer satisfy the enclosing loop; loops inside those functions are analyzed independently. The strongest rule in the set. |
| CC010 | `await foreach` missing CancellationToken flow | Usage | Warning | 4 | 4 | 4 | 4 | 3 | 4 | Low | **v1.5.0 (new); fixes v1.27.183/v1.27.199:** flags `await foreach` over an `IAsyncEnumerable<T>` (or implementer) when a token is in scope, the source does not already pass a token argument, and it is not already a configured cancelable enumerable; fixer rewrites the source to `.WithCancellation(token)`. `WithCancellation` wrapper recognition is semantic and framework-gated, so look-alikes do not suppress the rule. Custom `ConfigureAwait` overloads that receive a `CancellationToken` remain visible to the producer-token check rather than being mistaken for framework-only configuration; boolean configuration without token flow still reports. Uses the shared `FindEnclosingCancellationTokenParameter` scope walk. Conservative: synchronous `foreach`, no-token scopes, and producer calls already receiving a token are quiet. No analyzer XML `<remarks>` example variety yet (P3). |
| CC029 | Timeout CTS ignores in-scope parent token | Usage | Warning | 4 | 4 | 4 | 4 | 4 | 4 | Low | **v1.28.0 (new):** flags `new CancellationTokenSource(TimeSpan|int)` and `CancelAfter` on a parameterless local CTS when `FindEnclosingCancellationTokenParameter` finds a parent token that is not linked. Symbol-gated to `System.Threading.CancellationTokenSource`; look-alikes quiet. Timeout-ctor path reports on the creation; parameterless + `CancelAfter` reports on `CancelAfter` only (no double-report when both appear). Fixer rewrites simple local declarations to `CreateLinkedTokenSource(token)` + `CancelAfter(delay)` without injecting `using` (CC014 owns disposal). Complements CC002/CC012/CC014 — a timeout source can look “propagated” while still ignoring request abort. Intentional isolated timeouts with a parent still in scope need a suppress (documented). |
| CC030 | Blocking `Process.WaitForExit()` in async code | Usage | Warning | 4 | 4 | 4 | 4 | 4 | 4 | Low | **v1.30.0 (new):** flags a parameterless `Process.WaitForExit()` inside async code; fixer rewrites to `await WaitForExitAsync(token)`, flowing the in-scope token. The worst-behaved member of the blocking family — the wait is unbounded and depends on an external program, so a hung child pins a thread-pool thread nothing can reclaim. CC002 cannot see it (differently-named method, not an overload). Symbol-gated to `System.Diagnostics.Process`; quiet unless the target framework exposes `WaitForExitAsync` (.NET 5+); the `WaitForExit(int)` timeout overload is excluded because no async form has that shape. Null-conditional calls and `await`-forbidden contexts are reported without a fix via the shared `AwaitIsForbiddenHere` guard. **v1.52.24:** null-conditional `WaitForExit()` statements now fix too — hoisted via the shared `NullConditionalHoist`, with the parameterless fallback preserved and a speculative rebind guarding hidden members. |
| CC031 | Blocking synchronization primitives in async code | Usage | Warning | 4 | 4 | n/a | 4 | 4 | 4 | Low | **v1.32.0 (new):** flags `ManualResetEventSlim.Wait`, `CountdownEvent.Wait`, `WaitHandle.WaitOne`/`WaitAll`/`WaitAny`, `Monitor.Wait`, and `Thread.Join` in async code. Analyzer-only **by design**: these have no `…Async` counterpart, so the resolution is a design change (SemaphoreSlim/TaskCompletionSource/await the task) rather than a rewrite — hence `n/a` for Fix Strategy rather than a penalty. Matched through the override chain to the declaring framework type, so `ManualResetEvent.WaitOne` resolves to `WaitHandle.WaitOne`. Provably zero timeouts excluded (probe, not wait); `SemaphoreSlim.Wait` deliberately left to CC026, which can offer a real fix. **v1.39.1 (FN):** `ReaderWriterLockSlim.Enter*Lock` and `TryEnter*Lock` — not a `WaitHandle`, so the previous map missed them; they still park a pool thread with no async counterpart. `TryEnterWriteLock(Timeout.Infinite)` is an unbounded enter. Zero-timeout `TryEnter` probes, look-alikes, and sync methods stay quiet. **v1.39.2 (FN):** `Barrier.SignalAndWait` — same gap: not a `WaitHandle`, no async counterpart, parks every participant. Zero-timeout overloads still report (last arriver runs the post-phase action). **v1.39.3 (FN):** `ReaderWriterLock.AcquireReaderLock`/`AcquireWriterLock` — the pre-Slim lock, same gap; zero-timeout try-acquires stay quiet. **v1.39.4 (FN):** `UpgradeToWriterLock` — the upgrade path was still silent after v1.39.3; zero-timeout upgrades still report (restore uses `Timeout.Infinite`). |
| CC032 | Async call not awaited in non-async code | Usage | Warning | 4 | 4 | n/a | 4 | 4 | 4 | Low | **v1.33.0 (new):** flags a `Task`/`ValueTask`-returning call discarded as a bare expression statement in non-async code, or as the body of a void-returning expression-bodied lambda. Fills a genuine compiler gap: CS4014 fires only *inside* async methods, leaving constructors, sync methods, and non-async lambdas unreported — and CC032 stays quiet where CS4014 already reports, so they never double up. Assigned, returned, argument-passed, and `_ =`-discarded tasks are excluded; `_ =` is the documented opt-in, and flagging it would make the rule unsatisfiable. Analyzer-only by design (resolution depends on intent). |
| CC033 | `CancellationTokenSource` field never disposed | Usage | Warning | 4 | 4 | n/a | 4 | 4 | 4 | Low | **v1.34.0 (new):** flags a CTS field the declaring type creates and never disposes — the field counterpart of CC014, which owns the local case and can offer a `using` fix. Analyzer-only **by design**: a field's lifetime is the object's, so the resolution is implementing `IDisposable`, a design change rather than a rewrite. Ownership-gated (only a source the type *creates*; injected sources are someone else's to dispose), with disposal, escape, and `static` all exonerating. Analyzes the whole type symbol, so creation and disposal in different members — or different files of a partial type — resolve correctly. |
| CC034 | `ParallelOptions` missing a `CancellationToken` | Usage | Warning | 4 | 4 | 4 | 4 | 4 | 4 | Low | **v1.36.0 (new):** flags a `ParallelOptions` created with no token while one is in scope; fixer adds `CancellationToken = token` to the initializer (creating it if absent, appending if present so existing settings survive). Closes a gap CC002 **structurally** cannot reach: CC002 matches calls with token-accepting overloads, but here the token is an object-initializer property and `Parallel.ForEach` has no token overload at all — verified empirically against the shipped analyzer before the rule was written. In-scope-token gated (same walk as CC002/CC012) and quiet when the token is assigned afterwards. |
| CC035 | Cancellation silently swallowed by an empty catch | Usage | Info | 4 | 4 | n/a | 4 | 4 | 3 | Low | **v1.37.0 (new):** reports an *empty* `catch (OperationCanceledException)` (subclasses included). Complements CC019, which covers only broad `catch`/`catch (Exception)` clauses — verified empirically that the named clause was uncovered. Scoped to the empty body: statements, `when` filters, rethrows, and even an explanatory comment all count as deliberation, which is what keeps the idiomatic wait-until-cancelled handler clean (a clean-code guard caught that FP during development). Info because a deliberate silent stop is unusual but legitimate; analyzer-only because the right resolution depends on what the caller needs to know. |
| CC036 | Blocking `Socket` calls in async code | Usage | Warning | 4 | 4 | n/a | 4 | 4 | 4 | Low | **v1.38.0 (new):** flags blocking `Socket` operations in async code. Deliberately *not* folded into CC028: that rule can offer a fix only because it demands a signature-compatible counterpart, and Socket's async APIs are not shaped that way (`Receive(byte[])` → `ReceiveAsync(Memory<byte>, CancellationToken)`), so extending it would trade fix safety for reach. `NetworkStream` stays with CC028 via its `Stream` coverage. Socket resolved from the compilation and matched by symbol through the override chain; async-context gated. Analyzer-only — no mechanical rewrite exists. |
| CC037 | Blocking `TcpClient.Connect` in async code | Usage | Warning | 4 | 4 | 4 | 4 | 4 | 4 | Low | **v1.40.0 (new):** flags `TcpClient.Connect` in async code. CC036 is Socket-only; application code almost always uses the wrapper, which produced **zero** diagnostics from every shipped rule (empirical all-analyzers test). Symbol-gated to `System.Net.Sockets.TcpClient`; quiet unless `ConnectAsync` exists; look-alikes and sync methods stay quiet. `thisClient.Client.Blocking = false` silences only IP/endpoint overloads on a simple local/parameter/field; hostname `Connect` still reports (sync DNS), property/method receivers are not exempt, an unrelated `Socket.Blocking = false` does not exempt, and a later write to the client invalidates the exemption. **v1.52.6:** fixer rewrites a safe `Connect` to `await ConnectAsync`, flowing an in-scope token when the rewritten call binds; tokenless fallback; named `remoteEP:` keeps the token named; named `hostname:` is reported without a rewrite (`ConnectAsync` uses `host`); no fix for null-conditional, await-illegal, or this/base/this-alias inside `ConnectAsync`. **v1.52.27:** null-conditional `Connect(...)` statements now fix too — hoisted via the shared `NullConditionalHoist` with a speculative TcpClient ConnectAsync rebind. |
| CC038 | Blocking `TcpListener` accept in async code | Usage | Warning | 4 | 4 | 4 | 4 | 4 | 4 | Low | **v1.41.0 (new):** flags `TcpListener.AcceptTcpClient` / `AcceptSocket` in async code. CC036 is Socket-only and CC037 is `TcpClient.Connect`; the listener produced **zero** diagnostics from every shipped rule (empirical all-analyzers test). Symbol-gated to `System.Net.Sockets.TcpListener`; quiet unless the matching `Accept*Async` exists; look-alikes, sync methods, a *positive* `Pending()` guard, and `Server.Blocking = false` stay quiet. **v1.41.1 (FN):** `if (!Pending()) Accept` is the blocking path and now reports; inverted early-exit (`if (!Pending()) continue;` then accept) and `while (flag && Pending())` stay quiet. **v1.52.7:** fixer rewrites a safe accept to `await AcceptTcpClientAsync` / `await AcceptSocketAsync`, flowing an in-scope token when the rewritten call binds; tokenless fallback; no fix for null-conditional, await-illegal, or this/base/this-alias inside the matching `Accept*Async`. Unusable TAP hiders stay quiet. **v1.52.28:** null-conditional `AcceptTcpClient()` / `AcceptSocket()` statements now fix too — hoisted via the shared `NullConditionalHoist` with a speculative Accept*Async rebind. **v1.52.29:** null-conditional accept statements now fix too — hoisted via the shared `NullConditionalHoist` with a speculative Accept*Async rebind. |
| CC039 | Blocking `UdpClient.Receive` in async code | Usage | Warning | 4 | 4 | 4 | 4 | 4 | 4 | Low | **v1.42.0 (new):** flags `UdpClient.Receive` in async code. CC036 is Socket-only, CC037 is `TcpClient.Connect`, and CC038 is `TcpListener` accept; the UDP wrapper produced **zero** diagnostics from every shipped rule (empirical all-analyzers test). Symbol-gated to `System.Net.Sockets.UdpClient`; quiet unless `ReceiveAsync` exists; look-alikes, sync methods, `Available > 0` / inverted `Available == 0` continue, and `Client.Blocking = false` stay quiet. **v1.52.8:** fixer rewrites a discarded `Receive(ref endpoint)` statement to `var received = await ReceiveAsync(...)` plus `endpoint = received.RemoteEndPoint`, flowing an in-scope token when the rewritten call binds; tokenless fallback. The TAP returns `UdpReceiveResult` and does not take the `ref` endpoint, so a value-use of the `byte[]` is reported without a rewrite. No fix for null-conditional, await-illegal, or this/base/this-alias inside `ReceiveAsync`. Unusable TAP hiders stay quiet. |
| CC040 | Blocking `HttpListener.GetContext` in async code | Usage | Warning | 4 | 4 | 4 | 4 | 4 | 4 | Low | **v1.43.0 (new):** flags `HttpListener.GetContext` in async code. CC036–CC039 are Socket/TcpClient/TcpListener/UdpClient; the HTTP listener produced **zero** diagnostics from every shipped rule (empirical all-analyzers test). Symbol-gated to `System.Net.HttpListener`; quiet unless `GetContextAsync` exists; look-alikes and sync methods stay quiet. **v1.52.9:** fixer rewrites a safe `GetContext()` to `await GetContextAsync()`. The TAP is tokenless, so the rewrite never invents a token. No fix for null-conditional or await-illegal positions. `HttpListener` is sealed. **v1.52.30:** null-conditional `GetContext()` statements now fix too — hoisted via the shared `NullConditionalHoist` with a speculative GetContextAsync rebind. |
| CC041 | Blocking `NamedPipeServerStream.WaitForConnection` in async code | Usage | Warning | 4 | 4 | 4 | 4 | 4 | 4 | Low | **v1.44.0 (new); fixer v1.52.10:** flags `NamedPipeServerStream.WaitForConnection` in async code. CC028 maps File/Stream Read/Write/CopyTo/Flush; CC036–CC040 are Socket/Tcp/Udp/HttpListener; the named-pipe server produced **zero** diagnostics from every shipped rule (empirical all-analyzers test). Symbol-gated to `System.IO.Pipes.NamedPipeServerStream`; quiet unless `WaitForConnectionAsync` exists; look-alikes and sync methods stay quiet. **v1.52.10:** fixer rewrites a safe `WaitForConnection()` to `await WaitForConnectionAsync`, flowing an in-scope token when the rewritten call binds; tokenless fallback; no fix for null-conditional or await-illegal positions. `NamedPipeServerStream` is sealed. Token-taking `WaitForConnectionAsync` is modern .NET. **v1.52.34:** null-conditional `WaitForConnection()` statements now fix too — hoisted via the shared `NullConditionalHoist` with a speculative WaitForConnectionAsync rebind. |
| CC042 | Blocking `NamedPipeClientStream.Connect` in async code | Usage | Warning | 4 | 4 | 4 | 4 | 4 | 4 | Low | **v1.45.0 (new); fixer v1.52.11:** flags `NamedPipeClientStream.Connect` in async code (parameterless, `int`, `TimeSpan`). CC041 is the server accept wait; the client produced **zero** diagnostics from every shipped rule (empirical all-analyzers test). Symbol-gated to `System.IO.Pipes.NamedPipeClientStream`; quiet unless `ConnectAsync` exists; look-alikes and sync methods stay quiet. **v1.52.11:** fixer rewrites a safe `Connect` to `await ConnectAsync`, preserving the timeout and flowing an in-scope token when the rewritten call binds; tokenless fallback for `()` and `int`; no rewrite for `TimeSpan` without a token (no tokenless TAP), null-conditional, or await-illegal positions. `NamedPipeClientStream` is sealed. Token-taking `ConnectAsync` is modern .NET. **v1.52.35:** null-conditional `Connect(...)` statements now fix too — hoisted via the shared `NullConditionalHoist` with a speculative ConnectAsync rebind. |
| CC043 | Blocking `Dns.GetHostAddresses` in async code | Usage | Warning | 4 | 4 | 4 | 4 | 4 | 4 | Low | **v1.46.0 (new); fixer v1.52.12:** flags `Dns.GetHostAddresses` in async code (string and `AddressFamily`, including `using static`). CC002 cannot see it (no token overload of this method). CC036–CC042 are Socket/Tcp/Udp/HttpListener/named-pipe; DNS produced **zero** diagnostics from every shipped rule (empirical all-analyzers test). Symbol-gated to `System.Net.Dns`; quiet unless `GetHostAddressesAsync` exists; look-alikes and sync methods stay quiet. **v1.46.1 (FP):** a compile-time constant IP (`"127.0.0.1"`, `"::1"`, `const` local) is a parse, not a query, and stays quiet; `"localhost"` and non-const locals still report. **v1.52.12:** fixer rewrites a safe `GetHostAddresses` to `await GetHostAddressesAsync`, flowing an in-scope token when the rewritten call binds; tokenless fallback (string TAP, or AddressFamily TAP with optional token); no fix for await-illegal positions. `Dns` is static. |
| CC044 | Blocking `Dns.GetHostEntry` in async code | Usage | Warning | 4 | 4 | 4 | 4 | 4 | 4 | Low | **v1.47.0 (new); fixer v1.52.13:** flags `Dns.GetHostEntry` in async code (string, `AddressFamily`, `IPAddress`, including `using static`). CC043 is GetHostAddresses only; GetHostEntry produced **zero** diagnostics from every shipped rule (empirical all-analyzers test). A numeric IP still reports — reverse DNS, not a parse. Quiet unless `GetHostEntryAsync` exists; look-alikes and sync methods stay quiet. **v1.52.13:** fixer rewrites a safe `GetHostEntry` to `await GetHostEntryAsync`, flowing an in-scope token when the rewritten call binds to `System.Net.Dns`; tokenless fallback for string TAP; never invents a token on the tokenless `IPAddress` TAP; AddressFamily TAP has an optional token. Identifier `using static` rewrites that would bind a same-named helper are withheld. No fix for await-illegal positions. |
| CC045 | Blocking `DbConnection.Open` in async code | Usage | Warning | 4 | 4 | 4 | 4 | 4 | 4 | Low | **v1.48.0 (new); fixer v1.52.1:** flags `DbConnection.Open` in async code. CC003 is EF Core; ADO.NET Open produced **zero** diagnostics from every shipped rule (empirical all-analyzers test). Symbol-gated to `System.Data.Common.DbConnection`; concrete providers match through the override chain; quiet unless `OpenAsync` exists; look-alikes and sync methods stay quiet. **v1.52.1:** fixer rewrites a safe `Open()` to `await OpenAsync`, flowing an in-scope token when the rewritten call binds; parameterless fallback when it does not; no fix for null-conditional or await-illegal positions. `OpenAsync` has accepted a token since .NET Framework 4.5. `DbCommand.ExecuteReader` is CC046. **v1.52.26:** null-conditional `Open()` statements now fix too — hoisted via the shared `NullConditionalHoist` with speculative OpenAsync rebinding. |
| CC046 | Blocking `DbCommand.ExecuteReader` in async code | Usage | Warning | 4 | 4 | 4 | 4 | 4 | 4 | Low | **v1.49.0 (new); fixer v1.52.2:** flags `DbCommand.ExecuteReader` in async code (parameterless and `CommandBehavior`). CC003 is EF Core; CC045 is Open; ExecuteReader produced **zero** diagnostics from every shipped rule (empirical all-analyzers test). Symbol-gated to `System.Data.Common.DbCommand`; `ExecuteReader` is not virtual so providers' `new` hiders match by inheritance + framework shape (instance, arity 0, `DbDataReader` return, 0 args or one `CommandBehavior`); custom helpers, generic helpers, and statics stay quiet; quiet unless a TAP `ExecuteReaderAsync` is reachable; look-alikes, `IDbCommand`, and sync methods stay quiet. **v1.52.2:** fixer rewrites a safe `ExecuteReader` to `await ExecuteReaderAsync`, preserving `CommandBehavior` and flowing an in-scope token when the rewritten call binds; parameterless/behavior-only fallback when it does not; no fix for null-conditional or await-illegal positions. **v1.52.14:** parenthesize the await when the call is a receiver or is followed by `!`. Provider `new` TAP hiders still match (`ExecuteReaderAsync()` / token form are not virtual). Stay quiet when every reachable async shape is an unusable hider. `ExecuteReaderAsync` has accepted a token since .NET Framework 4.5. `ExecuteNonQuery` is CC047. `ExecuteScalar` is CC048. **v1.52.18:** rewrite when ExecuteReader is an argument inside an unrelated `?.`; `host?.Command.ExecuteReader()` is still NoFix. **v1.52.31:** null-conditional `ExecuteReader()` statements now fix too — hoisted via the shared `NullConditionalHoist` with a speculative ExecuteReaderAsync rebind. |
| CC047 | Blocking `DbCommand.ExecuteNonQuery` in async code | Usage | Warning | 4 | 4 | 4 | 4 | 4 | 4 | Low | **v1.50.0 (new); fixer v1.52.3:** flags `DbCommand.ExecuteNonQuery` in async code. CC046 is ExecuteReader only; ExecuteNonQuery produced **zero** diagnostics from every shipped rule (empirical all-analyzers test). Symbol-gated to `System.Data.Common.DbCommand`; overrides and `new` hiders match by inheritance + framework shape (instance, arity 0, `int` return, no parameters); custom helpers, generic helpers, and statics stay quiet; quiet unless a `Task<int>` TAP `ExecuteNonQueryAsync` is reachable; look-alikes, `IDbCommand`, and sync methods stay quiet. **v1.52.3:** fixer rewrites a safe `ExecuteNonQuery()` to `await ExecuteNonQueryAsync`, flowing an in-scope token when the rewritten call binds; parameterless fallback when it does not; no fix for null-conditional, await-illegal positions, or token-required-only counterparts with no token in scope. Provider overrides still match. `ExecuteNonQueryAsync` has accepted a token since .NET Framework 4.5. `ExecuteScalar` is CC048. **v1.52.32:** null-conditional `ExecuteNonQuery()` statements now fix too — hoisted via the shared `NullConditionalHoist` with a speculative ExecuteNonQueryAsync rebind. |
| CC048 | Blocking `DbCommand.ExecuteScalar` in async code | Usage | Warning | 4 | 4 | 4 | 4 | 4 | 4 | Low | **v1.51.0 (new); fixer v1.52.4:** flags `DbCommand.ExecuteScalar` in async code. CC046/CC047 are ExecuteReader/NonQuery; ExecuteScalar produced **zero** diagnostics from every shipped rule (empirical all-analyzers test). Symbol-gated to `System.Data.Common.DbCommand`; overrides and `new` hiders match by inheritance + framework shape (instance, arity 0, non-`void` return, no parameters), including a more-derived return such as `string`; custom helpers, generic helpers, statics, `void` hiders, and `Task`/`ValueTask` hiders stay quiet; quiet unless a TAP `ExecuteScalarAsync` (`Task<T>`/`ValueTask<T>` with a reference-type `T`, 0 args or one token) is reachable. **v1.52.4:** fixer rewrites a safe `ExecuteScalar()` to `await ExecuteScalarAsync`, flowing an in-scope token when the rewritten call binds; parameterless fallback; no fix for null-conditional, await-illegal, token-required-only, this/base, or a this-alias (local, field, or property) inside `ExecuteScalarAsync`. Covariant `Task<string>` hiders still match. `ExecuteScalarAsync` has accepted a token since .NET Framework 4.5. **v1.52.33:** null-conditional `ExecuteScalar()` statements now fix too — hoisted via the shared `NullConditionalHoist` with a speculative ExecuteScalarAsync rebind. |
| CC049 | Blocking `SmtpClient.Send` in async code | Usage | Warning | 4 | 4 | 4 | 4 | 4 | 4 | Low | **v1.52.0 (new); fixer v1.52.5:** flags `SmtpClient.Send` in async code (`MailMessage` and four-string). CC004 is HttpClient; Send produced **zero** diagnostics from every shipped rule (empirical all-analyzers test). Symbol-gated to `System.Net.Mail.SmtpClient`; `Send` is not virtual so `new` hiders match by inheritance + framework shape (void, one `MailMessage` or four strings); custom helpers, generic helpers, statics, and the event-based `SendAsync` stay quiet; quiet unless `SendMailAsync` exists. **v1.52.5:** fixer rewrites a safe `Send` to `await SendMailAsync`, flowing an in-scope token when the rewritten call binds; tokenless fallback; no fix for null-conditional, await-illegal, or this/base/this-alias inside `SendMailAsync`. Token-taking `SendMailAsync` is .NET 5+; Framework has the tokenless form. **v1.52.25:** null-conditional `Send(...)` statements now fix too — hoisted via the shared `NullConditionalHoist` with in-scope token flow and a speculative SendMailAsync rebind. |
| CC050 | Blocking `Ping.Send` in async code | Usage | Warning | 4 | 4 | 4 | 4 | 4 | 4 | Low | **v1.52.38 (new):** flags `Ping.Send` in async code (all sync overloads, parameterless through the full TimeSpan/`byte[]`/`PingOptions` arity). CC036–CC044 are Socket/Tcp/Udp/HttpListener/named-pipe/DNS; Ping produced **zero** diagnostics from every shipped rule (empirical all-analyzers test). Symbol-gated to `System.Net.NetworkInformation.Ping`; quiet unless `SendPingAsync` exists — the event-based EAP `SendAsync` is deliberately not treated as the counterpart; look-alikes and sync methods stay quiet. The token-taking `SendPingAsync` overloads are modern .NET only and exist solely on the `TimeSpan` arity, so a bare `Send(host)` rewrites tokenless while the full-arity call flows an in-scope token. Null-conditional statements hoist to an `is not null` guard; await-forbidden contexts (lock bodies, unsafe) and a bare implicit-this `Send` inside a `SendPingAsync` member are reported without a fix. |
| CC028 | Blocking `System.IO` read/write/append in async code | Usage | Warning | 4 | 4 | 4 | 4 | 4 | 4 | Low | **v1.24.0 (new); fixes v1.25.0/v1.27.198/v1.27.202:** flags a blocking synchronous `System.IO` helper inside async code (method/local function/lambda/anonymous method) when a signature-compatible `<name>Async` counterpart exists on the type — `File` read/write/append (`ReadAllText`/`ReadAllBytes`/`ReadAllLines`, `WriteAll*`, `AppendAll*`), `StreamReader.ReadToEnd`/`ReadLine`, and (v1.27.0) `StreamWriter.Write`/`WriteLine`/`Flush` (generalised from `System.IO.File` to `System.IO` in v1.26.0). Qualified and `using static` File calls are supported. Null-conditional instance calls are diagnosed but intentionally receive no fix because preserving null semantics is context-dependent. **v1.27.0** replaced the name-only counterpart lookup with a parameter-signature match (overload equals the call's params, optionally + a trailing token), so the rewrite always compiles (`StreamWriter.Write(bool)` has no async form → quiet) and the token is only flowed when the matched overload accepts one (`Write(string)`→`await WriteAsync(text)` tokenless; `Flush()`→`await FlushAsync(token)`). Fixer rewrites safe direct-access shapes to `await …Async(…[, token])`, flowing the in-scope token via `FindEnclosingCancellationTokenParameter`. Symbol-resolved + namespace-gated to `System.IO` (look-alikes ignored); only in async context via the shared `IsInAsyncFunction`. Fix-All batches across the type→method map. Rounds out the blocking-in-async family (CC013/CC015/CC026). **v1.29.0 (FN):** the `Stream` primitives themselves — `Read`/`Write`/`CopyTo`/`Flush` — are now covered. The curated map keys on the exact declaring type name, so blocking stream calls were a silent false negative despite every one having a token-taking async counterpart. Stream types match by **inheritance** (concrete framework streams and user subclasses outside `System.IO` included) and the counterpart lookup walks base types, because a concrete stream overrides the sync member but inherits the async one. `MemoryStream` is excluded as in-memory (non-blocking); the exclusion tests the receiver's own type, since `MemoryStream` does not override `Flush`. Review hardening: an async counterpart must be `public` and awaitable, and the rule verifies that a call with the arity the fix will emit actually *binds* to such a method — overload resolution stops at the most-derived type declaring an applicable member, so a subclass's `new int ReadAsync(...)` would otherwise capture the rewrite (CS1061). Calls whose named arguments do not line up with the counterpart's parameter names (renamed override) are reported without a fix rather than rewritten into CS1739; the fix carries the counterpart's own token parameter name so a renamed one does not produce CS1739 either. Stream membership is decided by the invoked member's *original definition* being on `Stream` (a subclass's own `Write(string)` convenience overload is not a blocking primitive), and static/instance binding is part of counterpart usability (a hiding `static ReadAsync` cannot be called through an instance receiver, CS0176). Final gate: the analyzer speculatively binds the exact call the fix would emit and reports only when it resolves to the intended counterpart, so implicit-conversion overloads that signature comparison cannot see are handled by real overload resolution. Named arguments must sit at the same ordinal on both methods, so an override that reuses base names in a different order cannot yield a silently argument-swapping rewrite. **v1.52.16:** parenthesize the await when the call is followed by `!` (`ReadAllText(p)!.Trim()`). The fixer now uses the shared `AwaitNeedsParentheses` helper. **v1.52.17:** `holder?.Reader.ReadLine()` reports without AD0001 and without a rewrite; argument-inside-unrelated-`?.` still fixes. **v1.52.36:** null-conditional statement spines (`reader?.ReadToEnd();`, `writer?.WriteLine(text);`, chained `holder?.Reader.ReadLine();`) now hoist to an `is not null` guard via `NullConditionalHoist` instead of being withheld — the fixer resolves the member binding on the `?.` spine, and an in-scope token flows into the awaited call. |
| CC027 | Returned task uses a disposed `using` resource | Usage | Warning | 4 | 4 | n/a | 4 | 3 | 4 | Low | **v1.23.0 (new); fixes v1.27.184/v1.27.206:** flags a non-async `Task`-returning method/local function whose `return` is a call on a local disposed by a `using` declaration, declaration-form using statement, or expression-form `using (resource)` — the resource is disposed before the returned task completes (premature disposal). Expression-form analysis is scoped to returns inside that exact using body. Receiver walking unwraps interface/base casts, which do not change the using local's lifetime. High confidence: only the receiver case is flagged (a synchronous read into a completed task like `Task.FromResult(resource.Value)` is not). Analyzer-only (fix = make async + await). |
| CC026 | `SemaphoreSlim.Wait()` in async code | Usage | Warning | 4 | 4 | 4 | 4 | 3 | 4 | Low | **v1.22.0 (new); fixes v1.27.185/v1.27.191/v1.27.193/v1.27.196:** flags potentially blocking `SemaphoreSlim.Wait()` overloads (parameterless, timeout, token), including null-conditional calls, inside async code — a classic deadlock source; fixer → `await gate.WaitAsync(…)` for safe direct-access shapes, carrying the original arguments through and injecting the in-scope token when `Wait()` was parameterless (v1.22.2). Provably zero integer and framework `TimeSpan` timeout forms (zero field, defaults, zero-argument construction) are excluded because they are immediate try-enter probes. Symbol-resolved to `System.Threading.SemaphoreSlim`. **v1.52.15:** parenthesize the await when a `bool`-returning `Wait` is a receiver or is followed by `!`. Parameterless `Wait()` and `Wait(CancellationToken)` return `void`. Chained `holder?.Gate.Wait` is reported without a rewrite. **v1.52.23:** null-conditional `Wait()` statements now fix too — hoisted via the shared `NullConditionalHoist` with arguments carried verbatim and the in-scope token flowing for parameterless `Wait()`. |
| CC025 | Prefer `await using` for `IAsyncDisposable` | Usage | Info | 4 | 4 | 4 | 4 | 3 | 3 | Low | **v1.21.0 (new); fix v1.27.187:** flags a `using` statement/declaration (no `await`) over an `IAsyncDisposable` resource in async code; fixer prepends `await`. Both the declaration (`using var x = …`) and statement (`using (…)`) forms, expression and variable receivers. Top-level programs are covered when their synthesized entry point contains `await`; purely synchronous top-level code stays quiet. Info. |
| CC024 | `async` lambda converted to a void-returning delegate | Usage | Warning | 4 | 4 | n/a | 4 | 3 | 4 | Low | **v1.20.0 (new); fix v1.27.186:** the lambda counterpart of CC023. Flags an `async` lambda whose converted delegate returns `void`, including custom delegate types (binds as async void). Catches the `Parallel.ForEach(..., async x => …)` trap. Task-returning delegates and the sanctioned `(object, EventArgs-derived)` event-handler shape are excluded. Analyzer-only (the right delegate depends on the consuming API). |
| CC023 | `async void` (non-event-handler) | Usage | Warning | 4 | 4 | 4 | 4 | 3 | 4 | Low | **v1.19.0 (new):** flags an `async void` method that is not an event handler (`(object, EventArgs)` shape, EventArgs subclasses included) and not externally-controlled; fixer changes `void`→`Task` + adds the Tasks import. Classic async anti-pattern (cf. VSTHRD100) — `async void` can't be awaited or cancelled and crashes on unhandled exceptions. |
| CC022 | Prefer `CancelAsync()` over `Cancel()` in async | Usage | Info | 4 | 4 | 4 | 4 | 3 | 3 | Low | **v1.18.0 (new); fixes v1.27.187/v1.27.215/v1.27.222:** flags a parameterless `CancellationTokenSource.Cancel()` inside async code, including null-conditional calls, only when the target framework exposes the public parameterless `CancelAsync()` API; .NET 6/7 stay quiet instead of receiving a non-compiling suggestion. Fixer rewrites safe direct member access to `await cts.CancelAsync()`. **v1.52.21:** null-conditional `Cancel()` statements now fix too — hoisted to `if (x is not null) { await x…CancelAsync(); }` with the operation spliced back into the awaited chain. Withheld when the rewrite would change behavior or not compile: a surviving nested `?.` (`holder?.Cts?.Cancel()`), receivers that are not locals, parameters, or `this` (`Engine?.Cancel()`, `Create()?.Cancel()` — evaluated twice), nullable-struct receivers, element-access/`!` spines (`holder?.Sources[0].Cancel()`), and unbraced `if` bodies with a parent `else`. **v1.52.20:** `holder?.Cts.Cancel()` is reported without a rewrite; `Cancel()` nested inside an async lambda argument of an unrelated `?.` still fixes. Top-level programs are covered when their synthesized entry point contains `await`; purely synchronous top-level code stays quiet. Info (`Cancel()` is still valid). The `Cancel(bool)` overload and sync contexts are excluded. Modern .NET 8 guidance — `Cancel()` runs callbacks synchronously on the caller. |
| CC021 | `HttpContext.RequestAborted` not observed | Usage | Info | 4 | 3 | n/a | 4 | 3 | 3 | Low | **v1.16.0 (new); fixes v1.27.181/v1.27.200/v1.27.216–v1.27.218:** the HttpContext parallel of CC020. Flags a method with a `Microsoft.AspNetCore.Http.HttpContext` parameter that does async work but never reads `context.RequestAborted` and never passes the context on. Direct and null-conditional reads count, including continued member use such as `context?.RequestAborted.IsCancellationRequested`; compile-time-only `nameof(context.RequestAborted)` does not. Passing the context as a direct argument or as a reduced extension-method receiver, including null-conditionally, counts as handing it off; ordinary instance calls do not. Info because HttpContext is often taken for non-cancellation reasons (hence FP score 3). Shares `AccessesMember`/`ParameterEscapesAsArgument` with CC020. |
| CC020 | gRPC method ignores `ServerCallContext.CancellationToken` | Usage | Warning | 4 | 4 | n/a | 4 | 3 | 3 | Low | **v1.15.0 (new); fixes v1.27.181/v1.27.200/v1.27.216–v1.27.218:** flags a method with a `Grpc.Core.ServerCallContext` parameter that does async work but never reads `context.CancellationToken` and never passes the context on. Direct and null-conditional reads count, including continued calls such as `context?.CancellationToken.ThrowIfCancellationRequested()`; compile-time-only `nameof(context.CancellationToken)` does not. Passing the context as a direct argument or as a reduced extension-method receiver, including null-conditionally, counts as handing it off; ordinary instance calls do not. Fills a genuine gap — the token is a property, not a parameter, so CC002 can't see it (cf. CC017 for BackgroundService). Analyzer-only; gated by parameter type name+namespace (tests use a stub). |
| CC019 | Broad catch swallows `OperationCanceledException` | Usage | Info | 4 | 3 | 4 | 4 | 3 | 3 | Low | **v1.14.0 (new); fixes v1.17.0/v1.27.174/v1.27.175/v1.27.213/v1.27.223:** flags a catch-all/`catch (Exception)` with no `when` filter, over a `try` containing awaited work in the current function scope, whose body does not propagate cancellation. Covers explicit `await`, `await foreach`, and both `await using` forms; awaits inside nested local functions/lambdas are ignored because that deferred work does not execute in the `try` itself. Conditional direct type-pattern rethrows are polarity- and hierarchy-aware: a positive unrelated type or a negated type overlapping any cancellation subtype does not suppress the diagnostic, while a positive cancellation guard or a negated disjoint class does. Semantic identity keeps same-named custom exceptions separate; unsealed cancellation subtypes that may implement a tested interface remain protected. Info because boundary handlers are sometimes intended. Conservative (filter/rethrow/specific-type/no-await all suppress). The fix inserts `if (ex is OperationCanceledException) throw;` (typed catches only). |
| CC018 | SignalR hub method missing `CancellationToken` | Usage | Warning | 4 | 4 | 4 | 4 | 3 | 4 | Low | **v1.13.0 (new):** SignalR analogue of CC005B. Flags a public non-static async method on a `Microsoft.AspNetCore.SignalR.Hub`/`Hub<T>` subclass without a token; excludes lifecycle overrides + externally-controlled signatures. Reuses the shared add-token-parameter fixer. Base-type gated by name+namespace (tests use a faithful Hub stub, no package). |
| CC017 | `BackgroundService.ExecuteAsync` ignores stopping token | Usage | Warning | 4 | 4 | n/a | 4 | 3 | 4 | Low | **v1.12.0 (new); fixes v1.27.177/v1.27.209/v1.27.211:** flags an `override` of `ExecuteAsync(CancellationToken)` on a `Microsoft.Extensions.Hosting.BackgroundService` subclass whose body never observes the incoming stopping token at runtime — the override case CC016 skips. Compile-time-only `nameof(stoppingToken)`, a write-only assignment, and passing the parameter as `out` do not count as observation. Analyzer-only; token passed by value, `ref`, or `in`, or observed in a loop counts as used. Framework-gated to BackgroundService by base-type walk. |
| CC016 | Unused `CancellationToken` parameter | Usage | Info | 4 | 4 | n/a | 4 | 3 | 3 | Low | **v1.11.0 (new); fixes v1.27.11/v1.27.177/v1.27.178/v1.27.209/v1.27.211:** flags a method/local function that does async work (has `await` in its current function scope) but never observes its incoming `CancellationToken` parameter at runtime; excludes externally-controlled signatures and sync bodies. Await eligibility stops at nested lambdas/local functions, while token-reference analysis still descends into them because captures are real usage. Compile-time-only `nameof(token)`, a write-only simple assignment, and passing the parameter as `out` do not count; right-hand-side reads and `ref`/`in` forwarding still do. A token marked `[EnumeratorCancellation]` is excluded because the async-iterator infrastructure observes it (cf. CC011). Analyzer-only by design. |
| CC015 | Blocking on async code (sync-over-async) | Usage | Warning | 4 | 4 | 4 | 4 | 3 | 4 | Low | **v1.10.0 (new); fixes v1.27.188–v1.27.190/v1.27.192/v1.27.194/v1.27.195/v1.27.219:** flags `.Result` and potentially blocking `.Wait(...)` (including null-conditional access), plus `.GetAwaiter().GetResult()`, on a `Task`/`Task<T>`/`ValueTask` inside an `async` function; static `Task.WaitAll`/`WaitAny` joins are covered when qualified or imported with `using static`. Fixer rewrites safe direct-access shapes to `await`. Provably zero integer and exact framework `TimeSpan` timeout forms (zero field, defaults, zero-argument construction) are excluded because they are immediate completion probes; this applies to instance `Wait` and static `WaitAll`/`WaitAny`. Symbol-resolved (lookalikes ignored). Shares `IsInAsyncFunction` with CC013. **v1.52.19:** `host?.Work.Result` is reported without a rewrite; `.Result` as an argument inside an unrelated `?.` still fixes. **v1.52.22:** null-conditional blocking statements (`host?.Work.Result;`, `task?.Wait();`) now hoist to an `is not null` check awaiting the task via the shared `NullConditionalHoist`; eligibility identical to the CC022 hoist plus a speculative same-task-type check |
| CC014 | `CancellationTokenSource` never disposed | Usage | Warning | 4 | 4 | 4 | 4 | 3 | 4 | Low | **v1.9.0 (new); fixes v1.27.179/v1.27.197/v1.27.203/v1.27.205/v1.27.207:** flags a local `new CancellationTokenSource(...)`/`CreateLinkedTokenSource(...)` that is not a `using` decl, never disposed, and never escapes (return/out-assign/argument/nested-capture); fixer converts to a `using` declaration. Top-level-program locals use the compilation unit as their synthesized function boundary and receive the same safe `using var` fix. Compile-time-only `nameof(cts.Dispose)` does not count as disposal. Parentheses and null-forgiving operators are compile-time-only and are unwrapped before disposal/escape shape checks. An actual parameterless `System.IDisposable.Dispose()` invocation through an exact non-user-defined interface cast also counts as disposal; arbitrary casted calls do not. Conservative escape analysis — any disposal-elsewhere path suppresses it (like a scoped CA2000 for CTS). |
| CC013 | `Thread.Sleep` in async code | Usage | Warning | 4 | 4 | 4 | 4 | 3 | 4 | Low | **v1.8.0 (new); fixes v1.27.201/v1.27.204:** flags `System.Threading.Thread.Sleep` lexically inside an `async` method/local function/lambda/anonymous method; fixer rewrites to `await Task.Delay(delay, token)` (token flowed when in scope). Provably zero integer and exact framework `TimeSpan` duration forms (zero field, defaults, zero-argument construction) are excluded because `Thread.Sleep(0)` is a scheduler yield rather than a timed wait and `Task.Delay(0)` completes synchronously; positive and runtime-determined sleeps still report. Async-context check stops at the first function boundary, so a synchronous lambda inside an async method is quiet. Symbol-resolved (no name-only match). |
| CC012 | Explicit `CancellationToken.None`/`default` when a token is in scope | Usage | Info | 4 | 4 | 4 | 4 | 3 | 3 | Low | **v1.7.0 (new); fixes v1.27.176/v1.27.208/v1.27.210:** flags the actual `System.Threading.CancellationToken.None` property or `default`/`default(CancellationToken)` bound to a `CancellationToken` parameter when an in-scope token exists, including repeatedly parenthesized forms and `None` imported with `using static`; fixer replaces the whole argument expression with the token. The framework property is symbol-resolved, so a custom token-valued property merely named `None` stays quiet. Info severity because best-effort cleanup legitimately opts out. Uses the shared scope walk + converted-type gate (a bare `default` only counts in token context). |
| CC011 | Async-iterator token missing `[EnumeratorCancellation]` | Usage | Warning | 4 | 4 | 4 | 4 | 3 | 4 | Low | **v1.6.0 (new):** producer-side complement to CC010. Flags an `async IAsyncEnumerable<T>` iterator (method or local function with `yield`) whose `CancellationToken` parameter lacks `[EnumeratorCancellation]`, so a token passed via `.WithCancellation` would be silently dropped. Fixer adds the attribute + `System.Runtime.CompilerServices` import. Conservative: non-iterators returning the type, tokenless iterators, and already-marked params are quiet. Yield detection stops at nested local functions/lambdas. |

## Planning Shortlist

| Priority | Rules | Work |
| --- | --- | --- |
| High | None | No rule has a correctness defect severe enough to block a release. |
| Medium | None | No P0/P1 items open; remaining P2 items are opportunistic — see backlog. |
| Low | All 50 rules | Mature and FP-clean. Every rule is covered by a clean-code FP guard (`AllAnalyzersCleanCodeTests`) spanning core, framework (controllers/MediatR/SignalR/Minimal API/BackgroundService/gRPC), nested-scope, exotic-syntax, modern-C#-shape, async-File-I/O, and non-async `using` cases. Improve opportunistically. |

The rule set has grown from the original 9 (CC001–CC006, CC009) to 50 (adding CC010–CC050 across the
async-stream, blocking, lifecycle, async-hygiene, property-token, and linked-timeout families). Recent
hardening loops have shifted from new rules to FP/FN edge cases found by reviewing each rule against
representative code — three real false positives (CC009 loop condition, CC014 `cts?.Dispose()`,
CC001 `async Main`) and several false negatives (CC023 local functions, CC024 anonymous methods,
CC027 `using` statement) were fixed this way.

## Prioritized Fix Backlog

Grading: **P0** = release-blocking; **P1** = next hardening loop; **P2** = opportunistic; **P3** = directional.

### P0 — Release-blocking
- _None._

### P1 — Next hardening loop
- _None._ The backlog is down to P2/P3 items; the next loop should re-audit rule health rather
  than work a pre-named item.

### P2 — Opportunistic

- **Await-insertion guard, applied (v1.31.0).** Every fix that inserts an `await` now consults
  `CancellationTokenHelpers.AwaitInsertionIsUnsafe` and is withheld in a `lock` body, exception
  filter, unsafe context, disallowed query clause, or across a ref-like lifetime. CC013/CC015/CC022/
  CC025/CC026 had no such guard and could turn compiling code into a build error; CC028 had only the
  syntactic half. Any future await-inserting rule should call the same helper.
- **Dedupe the add-token-to-declaration recipe.** The CC005C method-group fix and the CC001 fix
  both build `CancellationToken cancellationToken = default` and insert it via
  `InsertTokenParameter`; the method-group symbol resolution (symbol-or-single-candidate) is also
  duplicated between `MinimalApiAnalyzer` and `MinimalApiCodeFixProvider`. Shared helpers would
  keep analyzer and fixer matching in lockstep.
- ~~**`DbCommand.ExecuteScalar` still silent.**~~ Shipped as CC048 in v1.51.0.
- ~~**`DbCommand.ExecuteNonQuery` still silent.**~~ Shipped as CC047 in v1.50.0.
- ~~**`DbCommand.ExecuteReader` still silent.**~~ Shipped as CC046 in v1.49.0.
- **`SslStream.AuthenticateAsClient`,
  `WebRequest.GetResponse` still silent.** Empirical 0-diag probes in the v1.48.0
  re-audit. Deferred — one target per iteration.
- ~~**`Ping.Send` still silent.**~~ Shipped as CC050 in v1.52.38.
- ~~**`SmtpClient.Send` still silent.**~~ Shipped as CC049 in v1.52.0.
- ~~**`Dns.GetHostEntry` still silent.**~~ Shipped as CC044 in v1.47.0.
- ~~**CC043 reports constant IP arguments.**~~ Fixed in v1.46.1 — compile-time
  constant IPs stay quiet; `"localhost"` and non-const locals still report.
- ~~**`Dns.GetHostAddresses` still silent.**~~ Shipped as CC043 in v1.46.0.
- ~~**`NamedPipeClientStream.Connect` still silent.**~~ Shipped as CC042 in v1.45.0.

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
  `FindEnclosingCancellationToken` scope walk (CC002/003/004/009/010/012/013/026/028/029/030/034; parameters first, then `HttpContext.RequestAborted` / `ServerCallContext.CancellationToken`; implementation in `InScopeTokenWalk`, focused Stryker.NET job at ≥80%),
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

- v1.52.38: 1518 tests, green locally. **CC050 (new)** —
  flags `Ping.Send` in async code; fixer rewrites to
  `await SendPingAsync`, preserving arguments and flowing an
  in-scope token when the rewritten call still binds. The
  token-taking `SendPingAsync` overloads are modern .NET only
  and exist solely on the `TimeSpan` arity, so a bare
  `Send(host)` rewrites tokenless while the full-arity call
  flows the token; null-conditional statements hoist to an
  `is not null` guard, lock bodies/unsafe contexts and a bare
  implicit-this `Send` inside `SendPingAsync` stay fix-free.
  EAP `SendAsync` is never treated as the counterpart.

- v1.52.37: 1508 tests, green locally. **CC013 fixer** —
  real bug fixed: a named `Thread.Sleep(millisecondsTimeout:)`
  argument was copied verbatim into `Task.Delay`, whose first
  parameter is named `millisecondsDelay`/`delay`, emitting CS1739.
  The rewrite now strips name colons and binds positionally.
  3 new tests pin TimeSpan, named-argument, and
  Timeout.InfiniteTimeSpan shapes with token flow.
- v1.52.36: 1505 tests, green locally. **CC028 fixer** —
  null-conditional statement spines (direct `reader?.ReadLine();`,
  chained `holder?.Reader.ReadLine();`, plus ReadToEnd and
  StreamWriter.WriteLine) hoist to an `is not null` guard with
  token flow; the fixer previously returned early because a `?.`
  spine surfaces as a MemberBindingExpression, which the name
  switch did not handle. NoFix precedence reordered so
  named-argument-mismatch and await-unsafe contexts (lock body,
  unsafe) stay final even on a `?.` spine — the hoist lands in the
  same context and copies arguments verbatim; a withheld rewrite
  that fails to hoist now stays withheld instead of falling through.
  7 new fixer tests (4 hoists incl. token flow + chained spine;
  3 NoFix regressions: non-statement conditional access, lock body,
  renamed ReadAsync overload).
- v1.52.35: 1499 tests, green locally. **CC042 fixer** —
  null-conditional `Connect(...)` statements hoist to an
  `is not null` check via `NullConditionalHoist.TryPrepareHoistedCall`
  with candidate ordering (token first, tokenless fallback) and
  per-candidate speculative rebind requiring the framework's
  ConnectAsync on NamedPipeClientStream; non-token parameters must
  mirror the original Connect arguments. 2 new fixer tests; 1 NoFix
  test converted to hoist expectation (token flows).
- v1.52.34: 1497 tests, green locally. **CC041 fixer** —
  null-conditional `WaitForConnection()` statements hoist to an
  `is not null` check via `NullConditionalHoist.TryPrepareHoistedCall`
  with in-scope token re-resolution; speculative rebind requires the
  framework's WaitForConnectionAsync on NamedPipeServerStream.
  2 new fixer tests; 1 NoFix test converted to hoist expectation.
- v1.52.33: 1495 tests, green locally. **CC048 fixer** —
  null-conditional `ExecuteScalar()` statements hoist to an
  `is not null` check via `NullConditionalHoist.TryPrepareHoistedCall`
  with in-scope token re-resolution; speculative rebind requires the
  framework's ExecuteScalarAsync on DbCommand. Returned-value and
  other expression positions keep reporting without a rewrite.
  3 new fixer tests; 2 NoFix tests converted to hoist expectations.
- v1.52.32: 1494 tests, green locally. **CC047 fixer** —
  null-conditional `ExecuteNonQuery()` statements hoist to an
  `is not null` check via `NullConditionalHoist.TryPrepareHoistedCall`
  with token flow and a speculative rebind requiring the framework's
  ExecuteNonQueryAsync on DbCommand. 3 new fixer tests (direct,
  chained, self-recursion this-spine + parenthesized + other-receiver
  variants) and 2 NoFix tests converted to hoist expectations.
- v1.52.31: 1489 tests, green locally. **CC046 fixer** —
  null-conditional `ExecuteReader()` statements hoist to an
  `is not null` check via `NullConditionalHoist.TryPrepareHoistedCall`
  (`command?.ExecuteReader();` →
  `if (command is not null) { await command.ExecuteReaderAsync(ct); }`,
  chained spines splice the operation). Speculative rebind requires
  the framework's Task-returning ExecuteReaderAsync on DbCommand;
  hidden members withhold. 3 fixer tests updated/added.
- v1.52.30: 1489 tests, green locally. **CC040 fixer** —
  null-conditional `GetContext()` statements hoist to an
  `is not null` check via `NullConditionalHoist.TryPrepareHoistedCall`
  (`listener?.GetContext();` →
  `if (listener is not null) { await listener.GetContextAsync(); }`,
  chained spines splice the operation). Speculative rebind requires
  the framework's Task-returning GetContextAsync on HttpListener;
  hidden members withhold. 2 new fixer tests; 1 NoFix test updated.
- v1.52.29: 1487 tests, green locally. **CC038 fixer** —
  null-conditional `AcceptTcpClient()` / `AcceptSocket()`
  statements hoist to an `is not null` check via
  `NullConditionalHoist.TryPrepareHoistedCall`; cancellable form
  preferred with parameterless fallback; speculative rebind
  requires the framework Accept*Async on TcpListener and withholds
  hidden non-awaitable members. Direct-spine member-binding async
  names resolve. Cancellable form preferred; when the cancellable
  overload fails to bind the hoist falls back to the parameterless
  Accept*Async (same fallback semantics as the direct-path fix).
  1 NoFix test updated to expect the hoist.
- v1.52.28: 1487 tests, green locally. **CC038 fixer** —
  null-conditional `AcceptTcpClient()` / `AcceptSocket()`
  statements hoist to an `is not null` check via
  `NullConditionalHoist.TryPrepareHoistedCall`; speculative rebind
  requires the framework Task-returning AcceptTcpClientAsync /
  AcceptSocketAsync on TcpListener; hidden members withhold.
  Direct-spine member-binding async names now resolve. 2 new fixer
  tests; 1 NoFix test updated to expect the hoist.
- v1.52.27: 1487 tests, green locally. **CC037 fixer** —
  null-conditional `Connect(...)` statements hoist to an
  `is not null` check via `NullConditionalHoist.TryPrepareHoistedCall`
  (`client?.Connect(host, port);` →
  `if (client is not null) { await client.ConnectAsync(host, port, ct); }`,
  chained spines splice the operation). Speculative rebind requires
  the framework Task/ValueTask-returning ConnectAsync on TcpClient;
  hidden members withhold. 3 new fixer tests; 1 NoFix test updated.
- v1.52.26: 1484 tests, green locally. **CC045 fixer** —
  null-conditional `Open()` statements hoist to an `is not null`
  check via `NullConditionalHoist.TryPrepareHoistedCall`
  (`connection?.Open();` →
  `if (connection is not null) { await connection.OpenAsync(ct); }`,
  chained spines splice the operation). Speculative rebind requires
  the framework's Task-returning `OpenAsync`. 2 NoFix tests updated
  to expect the hoist; 2 new tests; 1 invalid negative removed.
  Hoist machinery deduplicated into `NullConditionalHoist`.
- v1.52.25: 1482 tests, green locally. **CC049 fixer** —
  null-conditional `Send(...)` statements hoist to an
  `is not null` check via the shared `NullConditionalHoist`
  (`client?.Send(message);` →
  `if (client is not null) { await client.SendMailAsync(message, ct); }`,
  chained spines splice the client). The in-scope token is
  re-resolved by the fixer (the analyzer drops it for spine
  shapes) and a speculative rebind requires the framework's
  Task-returning `SendMailAsync`, resolved accessibly across base
types. 4 new fixer tests; 1 NoFix test updated to expect the hoist.
- v1.52.24: 1478 tests, green locally. **CC030 fixer** —
  null-conditional `WaitForExit()` statements hoist to an
  `is not null` check via the shared `NullConditionalHoist`
  (`process?.WaitForExit();` →
  `if (process is not null) { await process.WaitForExitAsync(ct); }`,
  chained spines splice the operation). Speculative rebind requires
  the framework's Task-returning `WaitForExitAsync`; the hidden-
  member fallback to the parameterless form carries over. 2 NoFix
  tests updated to expect the hoist; 1 hidden-subclass test updated.
- v1.52.23: 1478 tests, green locally. **CC026 fixer** —
  null-conditional `Wait()` statements hoist to an `is not null`
  check via the shared `NullConditionalHoist`
  (`gate?.Wait();` →
  `if (gate is not null) { await gate.WaitAsync(ct); }`,
  timeouts carried through). Argument lists carried verbatim;
  terminality and spine-exactness enforced, plus a speculative
  rebind so hidden non-awaitable `WaitAsync` members withhold the
  rewrite on direct and null-conditional paths alike. 3 new fixer tests, 1 updated.
- v1.52.22: 1475 tests, green locally. **CC015 fixer** —
  null-conditional blocking statements (`task?.Wait();`,
  `holder?.Work.GetAwaiter().GetResult();`)
  hoist to `if (x is not null) { await x.Work; }` via the shared
  `NullConditionalHoist`; speculative type check confirms the
  spliced task expression still binds to the original task, and
  the blocking operation must be the terminal expression of the
  statement (`.GetResult().Dispose()` stays unfixed), and the
  awaiter must be a parameterless `GetAwaiter()`. The spine
  is bound syntactically to the diagnosed access, so argument-
  position nested conditionals never splice an outer operation.
  Hoist extracted from v1.52.21's CC022 fixer into shared
  `NullConditionalHoist`. 6 new fixer tests, 1 updated; codex
  review found the trailing-work, wrong-spine, and direct-spine
  GetAwaiter gaps this hardening closes.
- v1.52.21: 1468 tests, green locally. **CC022 fixer** —
  null-conditional `Cancel()` statements hoist to an
  `is not null` check with the awaited call (`cts?.Cancel()` →
  `if (cts is not null) { await cts.CancelAsync(); }`,
  including chained spines like `holder?.Cts.Cancel()`).
  Withheld for surviving nested `?.`, non-local/parameter/this
  receivers (double evaluation), nullable-struct receivers,
  element-access/`!` spines, dangling-else shapes (including
  through embedded-statement chains), language versions below
  C# 9, and hidden `CancelAsync()` members — verified by a
  speculative rebind on both direct and conditional paths.
  13 new/updated fixer tests; codex review found the double
  evaluation, splice-shape, nullable-struct, dangling-else,
  language-version, and hidden-member gaps this hardening closes.
- v1.52.20: 1455 tests, green locally. **CC022 fixer** —
  `holder?.Cts.Cancel()` reports without a rewrite (would
  become `holder? await.Cts.CancelAsync()`). Direct
  `cts?.Cancel()` stays NoFix. Nested inside an async lambda
  argument of an unrelated `?.` still rewrites. 3 new fixer tests.
- v1.52.19: 1452 tests, green locally. **CC015 fixer** —
  `host?.Work.Result` / `.Wait()` / `GetResult()` on a `?.`
  spine report without a rewrite. 5 new fixer tests.
- v1.52.18: 1447 tests, green locally. **CC046 fixer** —
  rewrite ExecuteReader used as an argument inside an
  unrelated `?.`. 1 new fixer test.
- v1.52.17: 1446 tests, green locally. **CC028** —
  `holder?.Reader.ReadLine()` reports without AD0001 and
  without a rewrite; TAP hiders through `?.` stay quiet.
  4 new tests.
- v1.52.16: 1442 tests, green locally. **CC028 fixer** —
  parenthesize `await …Async` when the original call is
  followed by `!`. Uses shared `AwaitNeedsParentheses`. 2 new
  fixer tests.
- v1.52.15: 1440 tests, green locally. **CC026 fixer** —
  parenthesize `await WaitAsync` when a `bool`-returning `Wait`
  is a receiver or followed by `!`; withhold when Wait sits on
  the left spine of a `?.` WhenNotNull. 7 new fixer tests.
- v1.52.14: 1433 tests, green locally. **CC046 fixer** —
  parenthesize `await ExecuteReaderAsync` when used as a receiver
  or followed by `!`. 2 new fixer tests.
- v1.52.13: 1431 tests, green locally. **CC044 fixer** —
  `GetHostEntry` → `await GetHostEntryAsync`, with token flow on
  string TAP. `IPAddress` TAP is tokenless. AddressFamily TAP has
  an optional token. Identifier helper-shadow withheld. 13 new
  fixer tests. `Dns` is a static type.
- v1.52.12: 1418 tests, green locally. **CC043 fixer** —
  `GetHostAddresses` → `await GetHostAddressesAsync`, with token
  flow. Tokenless fallback for the string TAP; AddressFamily TAP
  has an optional token so tokenless still compiles. Identifier
  `using static` rewrites that would bind a same-named helper are
  withheld. 11 new fixer tests. `Dns` is a static type.
- v1.52.11: 1407 tests, green locally. **CC042 fixer** —
  `Connect()` / `Connect(int)` / `Connect(TimeSpan)` →
  `await ConnectAsync`, with token flow. Tokenless fallback for
  parameterless and `int`. `Connect(TimeSpan)` without a token is
  withheld (no tokenless TAP). 11 new fixer tests.
  `NamedPipeClientStream` is sealed so subclass TAP hiders cannot
  compile.
- v1.52.10: 1396 tests, green locally. **CC041 fixer** —
  `WaitForConnection()` → `await WaitForConnectionAsync`, with
  token flow and tokenless fallback. 6 new fixer tests.
  `NamedPipeServerStream` is sealed so subclass TAP hiders cannot
  compile.
- v1.52.9: 1390 tests, green locally. **CC040 fixer** —
  `GetContext()` → `await GetContextAsync()`. Tokenless TAP; the
  fixer does not invent a token. 8 new fixer tests. `HttpListener`
  is sealed so subclass TAP hiders cannot compile.
- v1.52.8: 1382 tests, green locally. **CC039 fixer** —
  discarded `Receive(ref endpoint)` → `var received = await
  ReceiveAsync(...)` plus `endpoint = received.RemoteEndPoint`,
  with token flow and tokenless fallback. Value-use of the `byte[]`
  is withheld because the TAP returns `UdpReceiveResult`. Withheld
  for await-illegal, null-conditional, and this/base/this-alias
  (including a cast of `this`) inside `ReceiveAsync`. 14 new
  fixer tests. Unusable TAP hiders stay quiet. A composed UdpClient
  field outside `ReceiveAsync` still rewrites.
- v1.52.7: 1368 tests, green locally. **CC038 fixer** —
  `AcceptTcpClient` / `AcceptSocket` → `await Accept*Async` with
  token flow, tokenless fallback, withheld for await-illegal,
  null-conditional, and this/base/this-alias (including any
  instance field/property on the enclosing type, plus a cast or
  `as` of `this`) inside the matching `Accept*Async`. 17 new
  fixer tests. Unusable TAP hiders stay quiet. An optional-int TAP
  leftover does not steal a usable inherited counterpart.
- v1.52.6: 1351 tests, green locally. **CC037 fixer** —
  `Connect` → `await ConnectAsync` with token flow, tokenless
  fallback, named-argument token name when names still bind,
  withheld for `hostname:` (TAP uses `host`), await-illegal,
  null-conditional, and this/base/this-alias (including any
  instance field/property on the enclosing type) inside
  `ConnectAsync`. 12 new fixer tests. Unusable TAP hiders stay quiet.
- v1.52.5: 1339 tests, green locally. **CC049 fixer** —
  `Send` → `await SendMailAsync` (`MailMessage` and four-string) with
  token flow, tokenless fallback, named-argument token name, withheld
  where `await` cannot compile or the receiver is `this`/`base`/a
  this-alias inside `SendMailAsync`. Unusable `new` TAP hiders stay
  quiet. 16 new fixer tests. A composed SmtpClient field still
  rewrites; a field assigned `this` does not. Optional-int TAP
  hiders do not steal the rewrite.
- v1.52.4: 1323 tests, green locally. **CC048 fixer** —
  `ExecuteScalar()` → `await ExecuteScalarAsync` with token flow,
  parameterless fallback, withheld where `await` cannot compile,
  only a token-taking TAP is reachable with no token in scope, or
  the receiver is `this`/`base`/a this-alias (local, field,
  property, or an outer-scope capture in a nested function) inside
  `ExecuteScalarAsync` (so the rewrite cannot re-enter the override);
  covariant `Task<string>` hiders still report; unusable hiders stay
  quiet. 32 fixer tests. Covariant TAP hiders still rewrite as
  statements; a value use of a same-signature narrower TAP falls back
  to the parameterless form so outer overloads cannot retarget.
- v1.52.3: 1291 tests, green locally. **CC047 fixer** —
  `ExecuteNonQuery()` → `await ExecuteNonQueryAsync` with token flow,
  parameterless fallback, withheld where `await` cannot compile or only
  a token-taking TAP is reachable with no token in scope; quiet when no
  reachable `Task<int>` TAP; provider overrides still report. 15 new
  fixer tests.
- v1.52.2: 1276 tests, green locally. **CC046 fixer** —
  `ExecuteReader()` / `ExecuteReader(CommandBehavior)` →
  `await ExecuteReaderAsync` with token flow, behavior preserved,
  named `behavior:` kept legal by naming the token argument,
  parameterless fallback, withheld where `await` cannot compile or
  only a token-taking TAP is reachable with no token in scope;
  quiet when no reachable reader-returning TAP `ExecuteReaderAsync`;
  provider `new` TAP hiders still report; `Task<int>` hiders stay
  quiet. 23 new fixer tests.
- v1.52.1: 1253 tests, green locally. **CC045 fixer** — `Open()` →
  `await OpenAsync` with token flow, parameterless fallback, withheld
  where `await` cannot compile; quiet when no reachable `OpenAsync`;
  provider `OpenAsync` overrides still report. 14 new fixer tests.
- v1.52.0: 1239 tests, green locally. **new rule CC049** — blocking
  `SmtpClient.Send` in async code (`MailMessage` and four-string).
  Empirically 0 diagnostics from every shipped rule. TAP counterpart is
  `SendMailAsync`, not event-based `SendAsync`. Token-taking
  `SendMailAsync` is .NET 5+.
- v1.51.0: 1225 tests, green locally. **new rule CC048** — blocking
  `DbCommand.ExecuteScalar` in async code. Empirically 0 diagnostics
  from every shipped rule. Overrides and `new` hiders match by
  inheritance + framework shape (instance, arity 0, non-`void` return),
  including a more-derived `string` return. `ExecuteScalarAsync` has
  accepted a token since .NET Framework 4.5.
- v1.50.0: 1208 tests, green locally. **new rule CC047** — blocking
  `DbCommand.ExecuteNonQuery` in async code. Empirically 0 diagnostics
  from every shipped rule. Overrides and `new` hiders match by
  inheritance + framework shape (instance, arity 0, `int` return).
  `ExecuteNonQueryAsync` has accepted a token since .NET Framework 4.5.
  `ExecuteScalar` deferred.
- v1.49.0: 1193 tests, green locally. **new rule CC046** — blocking
  `DbCommand.ExecuteReader` in async code (parameterless and
  `CommandBehavior`). Empirically 0 diagnostics from every shipped rule.
  `ExecuteReader` is not virtual; provider `new` hiders still report via
  inheritance + framework shape (arity 0). Custom helpers, generic
  helpers, and statics stay quiet.
  `ExecuteReaderAsync` has accepted a token since .NET Framework 4.5.
  `ExecuteNonQuery` / `ExecuteScalar` deferred.
- v1.48.0: 1177 tests, green locally. **new rule CC045** — blocking `DbConnection.Open`
  in async code. Empirically 0 diagnostics from every shipped rule. `OpenAsync`
  has accepted a token since .NET Framework 4.5. `DbCommand.Execute*` deferred.
- v1.47.0: 1169 tests, green locally. **new rule CC044** — blocking `Dns.GetHostEntry`
  in async code. Empirically 0 diagnostics from every shipped rule. Numeric
  IPs still report (reverse DNS). Token-taking string `GetHostEntryAsync` is
  modern .NET.
- v1.46.1: 1160 tests, green locally. **CC043 FP fix:** compile-time constant
  IP arguments stay quiet (`"127.0.0.1"`, `"::1"`, `const` local);
  `"localhost"`, leading-zero IPv4 (`"010.0.0.1"`), and non-const locals still report.
- v1.46.0: 1151 tests, green locally. **new rule CC043** — blocking `Dns.GetHostAddresses`
  in async code (including `AddressFamily` and `using static`). Empirically 0
  diagnostics from every shipped rule. Token-taking `GetHostAddressesAsync` is
  modern .NET; Framework has the tokenless form. `GetHostEntry` deferred.
- v1.45.0: 1144 tests, green locally. **new rule CC042** — blocking `NamedPipeClientStream.Connect`
  in async code (including `Connect(int)` / `Connect(TimeSpan)`). Empirically 0 diagnostics
  from every shipped rule. Token-taking `ConnectAsync` is modern .NET.
- v1.44.0: 1136 tests, green locally. **new rule CC041** — blocking `NamedPipeServerStream.WaitForConnection`
  in async code. Empirically 0 diagnostics from every shipped rule. Token-taking
  `WaitForConnectionAsync` is modern .NET.
- v1.43.0: 1130 tests, green locally. **new rule CC040** — blocking `HttpListener.GetContext`
  in async code. Empirically 0 diagnostics from every shipped rule.
- v1.42.0: 1124 tests, green locally. **new rule CC039** — blocking `UdpClient.Receive`
  in async code. Empirically 0 diagnostics from every shipped rule. `Available > 0`
  and `Client.Blocking = false` stay quiet.
- v1.41.1: 1103 tests, green locally. **CC038 FN fix:** a negated `Pending()`
  guard no longer silences `AcceptTcpClient` / `AcceptSocket` — that path still
  parks. A positive `if (Pending())`, inverted early-exit poll, and
  `while (flag && Pending())` stay quiet.
- v1.41.0: 1091 tests, green locally. **new rule CC038** — blocking `TcpListener.AcceptTcpClient`
  / `AcceptSocket` in async code. Empirically uncovered by all 37 previous rules.
- v1.40.0: 1076 tests, green locally. **new rule CC037** — blocking `TcpClient.Connect`
  in async code. Empirically uncovered by all 36 previous rules. Hostname `Connect`
  still reports after `Client.Blocking = false` (sync DNS); IP/endpoint overloads stay
  quiet only when *this* client's `Client.Blocking` is set false.
- v1.39.4: 1051 tests, green locally. **CC031 FN fix:** `ReaderWriterLock.UpgradeToWriterLock`
  in async code is diagnosed, including zero-timeout upgrades (restore can park);
  `Acquire*Lock(0)` stays quiet.
- v1.39.3: 1049 tests, green locally. **CC031 FN fix:** `ReaderWriterLock.Acquire*Lock`
  in async code is diagnosed; zero-timeout try-acquires, look-alikes, and sync methods
  stay quiet.
- v1.39.2: 1044 tests, green locally. **CC031 FN fix:** `Barrier.SignalAndWait` in async
  code is diagnosed, including zero-timeout overloads (post-phase still runs);
  look-alikes and synchronous waits stay quiet.
- v1.39.1: 1038 tests, green locally. **CC031 FN fix:** `ReaderWriterLockSlim.Enter*Lock`
  and `TryEnter*Lock` in async code are diagnosed; zero-timeout probes, look-alikes, and
  synchronous enters stay quiet.
- v1.39.0: 1030 tests, green locally and in CI. **Framework property tokens** join
  the shared in-scope walk (`HttpContext.RequestAborted`, `ServerCallContext.CancellationToken`);
  CC001 skips convention middleware `Invoke`/`InvokeAsync(HttpContext)`. Focused Stryker.NET
  on the walk at 96% mutation score.
- v1.28.0: 750 tests, green locally. **new rule CC029** — timeout
  `CancellationTokenSource` not linked to in-scope parent token. Focused analyzer +
  code-fix coverage; clean-code linked-timeout idiom remains quiet.
- v1.27.223: 732 tests, green locally. **CC019 FN fix:** direct negated type-pattern
  rethrows are classified by polarity and cancellation-hierarchy overlap.
- v1.27.222: 726 tests, green locally. **CC022 FP fix:** `Cancel()` is reported only
  when the analyzed target framework provides the `CancelAsync()` API.
- v1.27.221: 725 tests, green locally. **CC005B FN fix:** MVC `[AcceptVerbs(...)]`
  actions are analyzed and fixed while same-named non-MVC attributes remain quiet.
- v1.27.220: 722 tests, green locally. **CC005C FN fix:** positional unreduced
  framework Map calls analyze/fix lambda and method-group handlers while semantic guards remain.
- v1.27.219: 717 tests, green locally. **CC015 FN fix:** statically imported
  `Task.WaitAll`/`WaitAny` calls are diagnosed while semantic guards remain intact.
- v1.27.218: 712 tests, green locally. **CC020/CC021 FP fix:** null-conditional reduced
  extension calls count as context handoff while ordinary instance calls do not.
- v1.27.217: 709 tests, green locally. **CC020/CC021 FP fix:** chained
  null-conditional token use counts as observation while downstream-object members do not.
- v1.27.216: 706 tests, green locally. **CC020/CC021 FP fix:** direct
  null-conditional context-token reads count as observation while downstream members do not.
- v1.27.215: 703 tests, green locally. **CC022 FN fix:** null-conditional framework
  `Cancel()` calls are diagnosed in async code while overload, lookalike, and sync guards stay quiet.
- v1.27.214: 701 tests, green locally. **CC002 FN fix:** explicit arguments are
  classified by converted type; boxed tokens no longer hide missing propagation and contextual
  token defaults remain clean across CC002/CC003/CC004.
- v1.27.213: 697 tests, green locally. **CC019 FN fix:** conditional rethrows restricted
  to unrelated exception types no longer mask swallowed cancellation; cancellation guards remain quiet.
- v1.27.212: 695 tests, green locally. **CC009 FN fix:** checks deferred to nested
  functions no longer satisfy an enclosing loop while direct body/condition checks still do.
- v1.27.211: 693 tests, green locally. **CC016/CC017 FN fix:** bound `out` arguments
  no longer count as incoming-token observation while `ref` and `in` forwarding still do.
- v1.27.210: 690 tests, green locally. **CC012 FN/fix:** the exact framework `None`
  property imported with `using static` is diagnosed and fixed while custom imports remain quiet.
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
