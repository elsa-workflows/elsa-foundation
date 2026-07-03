# W11 — Hot-path fixes: allocation / behaviour evidence

Work unit: **W11 — Hot-path fixes** (findings **IN-2, IN-3, IN-5, IN-10, DS-9, DS-10**; each item
independently shippable). Program-goal bucket:
[`elsa-4-review-remediation`](../../program-goals/elsa-4-review-remediation.md). Brief:
[roadmap.md §W11](roadmap.md#w11-hot-path-fixes).

Measured on the Phase-2 tree branched from `origin/main` `b6a29b15`, .NET 10. This repo carries no
BenchmarkDotNet harness, so — as with W9 — the win for each item is pinned by xUnit allocation/behaviour
tests plus the source-level cost that is removed. New suites: **Elsa.Events.Tests** (IN-2/IN-5/IN-10) and
**Elsa.Expressions.JavaScript.Jint.Tests** (DS-9/DS-10); IN-3 extends **Elsa.Serialization.Tests**.

---

## IN-3 — cache `JsonPayloadSerializer` serializer options

**Before.** `JsonPayloadSerializer.GetOptions()` allocated a fresh `JsonSerializerOptions`, re-added every
converter, and re-registered the type-info resolver on **every** serialize/deserialize call. Because STJ
keys its compiled `JsonTypeInfo` metadata cache off the `JsonSerializerOptions` *instance*, a per-call
instance defeats that cache — every payload round-trip re-derived reflection metadata from scratch.

**After.** Options are built once and cached, guarded by a revision counter on the converter registry
(`JsonPayloadConverterRegistry.Revision`, bumped via `Interlocked` on `Register`/`RegisterAll`).
`GetOptions()` does a volatile revision read + double-checked rebuild: same registry state ⇒ the **same**
`JsonSerializerOptions` instance, so STJ's `JsonTypeInfo` cache is retained across calls; a registry mutation
bumps the revision and forces exactly one rebuild.

**Evidence** — `Elsa.Serialization.Tests` `JsonPayloadSerializerOptionsCacheTests` (Serialization **24 → 28**):
- `GetOptions_CalledRepeatedly_ReturnsSameInstance` — reference-equal options across calls (the
  STJ-cache-defeating per-call allocation is gone).
- `GetOptions_AfterRegistryRevisionChanges_RebuildsOptions` — a registry change yields a new instance (cache
  is correctly invalidated, not stale).
- `Register_And_RegisterAll_BumpRevision` — the invalidation signal fires on mutation.
- `GetOptions_StableRegistry_DoesNotRebuildAcrossManyCalls` — many calls, one instance (no per-call churn).

---

## IN-2 — stop linking the enqueue-time caller token into fire-and-forget dispatch

**Before.** `BackgroundEventPublisher` linked the enqueue-time caller token (captured on the queued context,
e.g. an HTTP request scope) into background dispatch. By the time a fire-and-forget event is dequeued its
originating scope is often already gone, so a since-cancelled token would abort — and then **misreport as an
error** — a dispatch that should run to completion.

**After.** Dispatch runs under the host/tenant lifetime token only; the queued context's own
`CancellationToken` is never linked. `IEventContext.CancellationToken` is retained on the contract (it still
carries the caller token for non-background strategies).

**Evidence** — `BackgroundEventPublisherTokenTests`:
- `AlreadyCancelledEnqueueTokenDoesNotBlockDispatch` — an event queued with an **already-cancelled** token is
  still dispatched exactly once and logs no error.
- `DispatchUsesHostTokenNotEnqueueToken` — the token observed by the publisher is the host token, not the
  dead enqueue-time token.

---

## IN-5 — the dead `StopAsync` becomes a real graceful-shutdown hook, wired into the host

**Before.** `BackgroundEventPublisher.StopAsync` existed but was never called; `TaskStateManager` cancelled
the lifetime token and disposed running tasks without ever signalling background tasks to stop, so queued
in-flight events were cut off at shutdown. (`TaskStateManager` stopped `RecurringTask`s gracefully but not
`IBackgroundTask`s.)

**After.** `StopAsync` completes the channel writer (`ChannelWriter.TryComplete`), so the read loop drains
everything already queued and then exits cleanly. `TaskStateManager.Stop()` now mirrors the recurring-task
pattern: it calls `StopAsync` on each registered `IBackgroundTask` **before** cancelling the lifetime token,
then awaits the running loops. Completing the writer makes the drain **bounded** — no further enqueue is
possible, so shutdown can never hang chasing a still-filling channel; the token cancel remains the hard-stop
safety net. `TaskManager.RunBackgroundTasks` registers each instance so the host can reach it. (Confined to
`TaskStateManager.cs`/`TaskManager.cs`; `TaskExecutor.cs` — W15/IN-4 — untouched.)

**Evidence** — `BackgroundEventPublisherShutdownTests`:
- `StopAsyncDrainsAllQueuedEventsThenExitsCleanly` — N=25 queued, `StopAsync`, then the loop drains all 25,
  returns `IsCompletedSuccessfully`, and the writer is completed (no post-stop enqueue).
- `HostShutdownDispatchesAllQueuedEventsAndExitsCleanly` — **full public host path**: `TaskManager` runs the
  publisher as a registered `IBackgroundTask`; `DisposeAsync` (host shutdown) drives `TaskStateManager.Stop()`
  and all 25 queued events are dispatched. Were `StopAsync` still a dead hook, the writer would never complete
  and the token cancel would cut the drain short — so "all N dispatched, clean exit" is the wiring proof.

---

## IN-10 — cache the per-event-type handler resolution on the publish path

**Before.** Each `Publish` re-derived `typeof(IEventHandler<>).MakeGenericType(eventType)` + `GetMethod`, then
dispatched through `MethodBase.Invoke` — per-call reflection plus a boxed `object[]` args allocation on a hot
path, and `TargetInvocationException` unwrapping on every throw.

**After.** `EventHandlerHelper` compiles a `Func<IEventHandler, IEvent, CancellationToken, Task>` once per
event type into a `ConcurrentDictionary`. The delegate closes over nothing, casts to the closed generic
handler/event types and calls `Handle` directly — no per-dispatch reflection, no args-array allocation, and
the handler's own exception surfaces **unwrapped** (behaviour-equivalent to the old explicit unwrap). The
strategies (`Sequential`/`Parallel`) call `handler.Invoke(context.EventContext)`. Kept out of
`EventHandlerInvokerMiddleware.cs` per the W10 boundary.

**Evidence** — `EventHandlerInvokerCacheTests`:
- `GetInvokerReturnsSameCachedDelegatePerEventType` — reference-equal delegate per event type (built once,
  cached), distinct per type (no per-call `MakeGenericType`/`GetMethod`/`Invoke`).
- `InvokeDispatchesToTypedHandler` / `SequentialStrategyDispatchesToAllHandlersInOrder` — correct single- and
  multi-handler dispatch, in order.
- `HandlerExceptionSurfacesUnwrapped` — a throwing handler surfaces `InvalidOperationException` directly, not a
  reflection `TargetInvocationException`.

---

## DS-10 — assemble the Jint engine setup once per scope; fresh engine per evaluation

**Before.** `JintEngineFactory.Create` ran the full `IJintEngineOptionsConfigurator` pipeline and rebuilt the
assembled `Jint.Options` on **every** evaluation.

**After.** The configurator pass + sandbox constraints are assembled **once per factory scope** and cached
(`_cachedOptions`); only the `Engine` itself is constructed fresh per call. Engines are **not** pooled — Jint
engines retain mutable global state between evaluations, so per-evaluation state isolation requires a fresh
`Engine`; the win is on the setup, which is stateless. `JintEngineFactory` is `AddScoped`, so each
workflow-execution scope owns its own cached options.

> **Cache-location contract.** The shipped configurators do not read `evaluatorOptions`, so caching
> independent of it is correct. A future context-sensitive configurator must **not** rely on this cache — the
> guard test below pins "configurators run once per scope" so such a change trips a loud test rather than
> silently reading stale first-call options. Documented at the cache site in `JintEngineFactory`.

**Evidence** — `JintEngineOptionsCacheTests`:
- `ConfiguratorsRunOncePerScopeAcrossManyCreates` — **mandatory guard**: a counting configurator runs exactly
  **once** across 10 `Create` calls in a scope (setup assembled once, reused).
- `EngineStateIsIsolatedBetweenEvaluations` — a global set on one engine is `undefined` on the next `Create`
  (fresh engine; no state bleed — why pooling was rejected).
- `SeparateScopesDoNotShareOptions` — each DI scope assembles its own options once (per-scope isolation).

---

## DS-9 — Jint sandboxing: statement / recursion / timeout limits + honoured `CancellationToken`

**Before.** No execution limits; the `CancellationToken` passed to the evaluator was discarded — a
pathological script (infinite loop, unbounded recursion) could hang the executing thread indefinitely.

**After.** `ApplySandboxConstraints` wires Jint's `TimeoutInterval`, `MaxStatements` and `LimitRecursion`, and
registers a cancellation constraint that each `Create` rebinds to the caller's real token (a cancelable
placeholder token registers the constraint on the cached options; `Create` calls `Reset(token)` per
evaluation). Each limit is **individually overridable per shell** via `JintFeature` ManifestSettings →
`FeatureOptions`, and each is independently disabled by a null/non-positive value. Defaults are generous
(timeout **5 s**, **10,000,000** statements, recursion depth **300**) and were tuned against the full Runtime
and Activities-Runtime suites — both stay green (no legitimate script regressed).

**Evidence** — `JintSandboxConstraintTests` (all four control-room-required abort paths + non-regression):
- `InfiniteLoopTimesOut` — `while(true){}` under a 200 ms timeout throws `TimeoutException`.
- `StatementLimitAborts` — `while(true){}` under a 1,000-statement cap throws `StatementsCountOverflowException`.
- `RecursionLimitAborts` — unbounded recursion under depth 50 throws `RecursionDepthOverflowException`.
- `CancelledTokenAbortsExecution` — an already-cancelled token aborts a running script with
  `ExecutionCanceledException` (the previously-discarded token is now honoured).
- `LiveTokenDoesNotAbortNormalScript` / `EachCreateRebindsItsOwnToken` — a live token runs a normal script to
  completion, and a cancelled call never poisons a later `Create` (per-call token rebind on the shared
  constraint).

Sandbox defaults are documented on the `JintFeature` ManifestSettings.

---

## Baselines — all green, before and after (unmodified except added tests)

Branched from `origin/main` `b6a29b15`; later merged `origin/main` `5ebcfd91` (second-lander rule) and
re-ran **all** suites. Full `dotnet build Elsa.Server.slnx` reports **0 errors** post-merge. The merge
raised two baselines it owns (Architecture +4 guard tests, Activities Runtime +10 ADR-0030 carrier tests);
both are green with W11 applied.

| Suite | Baseline (b6a29b15) | Post-merge baseline (5ebcfd91) | After W11 |
|---|---|---|---|
| Architecture guards | 37 | 41 | 41 |
| Runtime | 642 | 642 | 642 |
| Groundwork (`Persistence/Groundwork/Tests`) | 150 | 150 | 150 |
| Publishing API | 52 | 52 | 52 |
| Activities Runtime | 145 | 155 | 155 |
| Resumption | 12 | 12 | 12 |
| Scheduling runtime | 19 | 19 | 19 |
| Activities Scheduling | 8 | 8 | 8 |
| Modularity | 104 | 104 | 104 |
| Serialization | 24 | 24 | **28** (+4 IN-3) |
| Events *(new)* | — | — | **8** (IN-2/IN-5/IN-10) |
| Jint *(new)* | — | — | **9** (DS-9/DS-10) |

Every pre-existing baseline stays green **unmodified**: IN-2/IN-5/IN-10 are behaviour-preserving on the
default path, IN-3 preserves serialize/deserialize output while removing per-call allocation, and DS-9's
generous defaults leave legitimate scripts unaffected while bounding runaway ones.
