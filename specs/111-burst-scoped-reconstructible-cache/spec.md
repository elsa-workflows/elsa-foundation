# Feature Specification: Burst-Scoped Reconstructible Cache + Executable-Artifact Read Cache

**Feature Branch**: `worktree-agent-a2dc84c4c4aa08812`

**Created**: 2026-07-20

**Status**: Implemented

**Input**: Follow-up item (b) of [ADR 0031](../../docs/adr/0031-runtime-burst-execution-sticky-single-writer-drain-with-in-process-fast-path.md) (accepted 2026-07-19) — the burst-scoped reconstructible cache — plus its first consumer, the executable-artifact read cache. Under the [Runtime Execution Seam](../../docs/program-goals/runtime-execution-seam.md) bucket. This is the last unimplemented piece of the ratified burst architecture; the sibling item (a) in-process-hop fast path (spec 109) and the durable round-trip characterization (spec 110) are the motivating measurements. Those two specs (PRs #884/#886) are NOT present in this base; this unit is designed against their described seams and notes where.

## Context

Under the Coalesced cadence dial ([ADR 0032](../../docs/adr/0032-runtime-checkpoint-cadence-is-policy-driven-per-workflow.md), spec 108) the durable commit/queue storms of a hot loop collapse to `1 commit + 2 queue ops` per activity. What remains uncollapsed is `IWorkflowExecutableStore.FindAsync`: every drain-path handler re-reads the SAME immutable, content-addressed pinned artifact (ADR 0038 — artifact id + behavioral hash, immutable by construction) once per hop. The spec-110 characterization measures ~5 executable reads per activity — 46 durable reads per 10-activity hot loop, 10 per 2-node run — every one resolving an object that cannot change within a drain.

ADR 0031 item (b) ratified the fix: a **burst-scoped reconstructible cache** for heavy in-memory objects, keyed to the workflow execution and disposed at burst (drain) end. Its load-bearing rule: *memory is cache, never a correctness dependency* — every entry is reconstructible from durable state, so a lost cache (crash, eviction, burst never forming) reproduces byte-identical results via the durable path. The executable read is the ideal first consumer: the artifact is immutable within a drain, so caching it needs no invalidation.

## Scope-mechanism decision (which ambient seam spans the drain)

ADR 0031 names `IWorkflowExecutionAmbientServicesAccessor` as the cache's natural home, but that accessor does not exist under that name in this base. The base has three drain-adjacent ambient seams:

| Seam | Spans | Verdict |
|---|---|---|
| `RuntimeSchedulerDrainRequest.AmbientServices` (`IServiceProvider?`) threaded to the drainer/pipeline | one `DrainAsync`, but handlers may open their own DI scope (`WorkflowInvokeActivitySchedulerWorkHandler` opens a fresh scope when ambient services are absent) so a DI-scoped service is not reliably shared | rejected — not reliably shared across a hop |
| `IRuntimeLiveDrainDeliveryAccessor` (spec 106) | the **Immediate** drain only — deliberately NOT pushed on the coalescing path where the overlay session is authoritative | rejected — cadence-specific; reusing it would change WU-2 delivery semantics |
| `IRuntimeCoalescingSessionAccessor` | the **Coalesced** drain only | rejected — cadence-specific |
| `ScopedWorkflowExecutionCommandExecutor` DI scope | one command envelope = one full drain-to-quiescence; but handler self-scoping (above) defeats a DI-scoped cache | rejected as the cache carrier for the same reason |

No single existing accessor spans BOTH cadences, and a DI-scoped cache is defeated by handler self-scoping. The drain-spanning boundary that is cadence-agnostic AND survives handler self-scoping is `WorkflowSchedulerCommandRouter.ProcessAsync` wrapping `IWorkflowDrainOrchestrator.DrainAsync` (one command → one burst → all cycles → quiescence, across both cadence branches). The cache therefore uses **one new AsyncLocal push/pop accessor** (`IWorkflowBurstScopeAccessor`) established at that boundary, following the identical established pattern of the two existing accessors (`AsyncLocalRuntimeLiveDrainDeliveryAccessor` / `AsyncLocalRuntimeCoalescingSessionAccessor`). This is not a second scope *mechanism*: it reuses the AsyncLocal-accessor pattern and the router/orchestrator drain boundary, generalizing what the ADR called the ambient-services accessor rather than inventing a parallel lifetime. AsyncLocal (not DI-scope) is required precisely because a handler may run in a fresh DI scope while the drain's async flow — and therefore the ambient burst scope — still surrounds it.

## Call-site enumeration (cached vs excluded)

Cached (routed through the burst cache, keyed by the immutable `ArtifactId`) — every drain-path pinned-executable read:

| # | Call site | Read |
|---|---|---|
| 1 | `WorkflowStartSchedulerWorkHandler` | `RequestedArtifactId` |
| 2 | `WorkflowScheduleActivitySchedulerWorkHandler` | `PinnedExecutable.ArtifactId` |
| 3 | `WorkflowStartActivitySchedulerWorkHandler` | `PinnedExecutable.ArtifactId` |
| 4 | `WorkflowCompleteActivitySchedulerWorkHandler` | continuation `PinnedExecutable.ArtifactId` |
| 5 | `WorkflowCreateBookmarkSchedulerWorkHandler` | `PinnedExecutable.ArtifactId` |
| 6 | `WorkflowRetryActivityBoundarySchedulerWorkHandler` | `PinnedExecutable.ArtifactId` |
| 7 | `WorkflowCheckpointSchedulerWorkHandler` | `PinnedExecutable.ArtifactId` (WorkflowStarted only) |
| 8 | `RuntimeCheckpointCadenceResolver` | authored-cadence artifact (coalescing host, once/drain) |
| 9 | `WorkflowInvokeActivitySchedulerWorkHandler` | `PinnedExecutable.ArtifactId` |
| 10 | `WorkflowParentActivityCompletionSchedulerWorkHandler` | `PinnedExecutable.ArtifactId` |
| 11 | `WorkflowResumeBookmarkSchedulerWorkHandler` | `PinnedExecutable.ArtifactId` |

Excluded (unchanged), respecting spec 110's boundary:

| Call site | Why excluded |
|---|---|
| `WorkflowStartDispatcher` (x2), `BookmarkResumeDispatcher` | activation-entry reads, run in a scope BEFORE the drain/burst is established (pre-enqueue) — no ambient scope, would pass through anyway |
| `WorkflowExecutableReferenceGarbageCollector`, `WorkflowExecutableDependencyGraph.ListAllAsync` | reference-GC closure over live state (`ListAllAsync`, not `FindAsync`) — intentionally re-reads live state |
| `WorkflowExecutableRootWriteLeaseManager` | lease/recovery — intentionally live |
| `ExecuteWorkflowRequestHandler`, `WorkflowExecutableInspector`, `ActivityExecutionLayoutReader` | API/inspection, not the drain path |

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Executable reads collapse under an active burst (Priority: P1)

A hot loop of N activities reads the pinned executable once (first cache miss populates the burst scope); all subsequent per-hop reads are served from memory. Durable executable reads per run drop from ~46 (10-activity hot loop) to ≤ 2-3.

**Why this priority**: this is the measured cost the unit exists to remove.

### User Story 2 - Memory is never a correctness dependency (Priority: P1)

A run with the cache DISABLED (kill switch off, or burst never forming) commits **byte-identical** durable checkpoint state to a run with it ENABLED. Because the cached artifact is immutable/content-addressed, memoizing it changes only read count, never committed state.

**Why this priority**: the ADR-mandated guardrail; the cache can never silently become a source of truth.

### User Story 3 - A lost cache reconstructs from durable state (Priority: P1)

Clearing the burst cache mid-drain (test seam) causes the next read to miss and re-read durably; the run completes identically. Entries do not survive across two sequential drains of the same execution — the second drain re-reads the artifact durably at least once.

**Why this priority**: reconstructible-only + evict-at-boundary is the ADR contract that makes the optimization safe.

### User Story 4 - Deterministic disposal at burst end (Priority: P2)

At drain quiescence the burst scope is disposed: disposable entries (heavy clients, parsed documents a future consumer publishes) are disposed and the entry table is cleared, so nothing leaks across drains or grows unbounded.

## Requirements *(mandatory)*

- **FR-001**: A burst-scoped cache contract exposes a typed get-or-add with an async factory (`GetOrAddAsync<T>(key, factory, ct)`), keyed by string, single-flight within the scope (trivial under single-writer-per-execution). XML docs state the reconstructible-only + must-survive-⇒-durable-value rule.
- **FR-002**: The burst scope is established once per drain by `WorkflowSchedulerCommandRouter` around `IWorkflowDrainOrchestrator.DrainAsync`, spanning both cadence branches, via an AsyncLocal push/pop accessor. When no scope is active (no router, or kill switch off) reads pass straight through to the durable store.
- **FR-003**: The executable read cache (`IWorkflowExecutableReader`) routes the 11 enumerated drain-path `FindAsync` reads through the burst scope keyed by `ArtifactId`; the excluded reads are untouched.
- **FR-004**: A kill switch option `RuntimeBurstCacheOptions.Enabled` (default `true`), following the `WorkflowDrainOrchestratorOptions` idiom (no `RuntimeInProcessHopFastPathOptions` exists in this base). Disabled ⇒ no scope pushed ⇒ every read is durable.
- **FR-005**: All entries are evicted (and disposed) at scope end; a `Clear()` test seam evicts mid-drain. Entries never survive across two sequential drains of one execution.
- **FR-006**: Guardrail — committed durable checkpoint state is byte-identical cache-on vs cache-off (masked non-deterministic ids, fixed clock).
- **FR-007**: A debug-only assertion seam (`#if DEBUG`) rejects caching a value that must survive the run (`DurableValueState` / `DurableValueExternalReference`), catching the misuse the ADR guardrail warns about, at zero release cost.

### Key Entities

- **`WorkflowBurstScope`**: the reconstructible cache for one drain of one execution — `GetOrAddAsync<T>`, `Clear()` (test seam), diagnostics counters (`ReadCount`/`MissCount`), `IAsyncDisposable`.
- **`IWorkflowBurstScopeAccessor`** / `AsyncLocalWorkflowBurstScopeAccessor`: AsyncLocal push/pop accessor (mirrors the live-drain / coalescing accessors).
- **`IWorkflowExecutableReader`** / `BurstCachedWorkflowExecutableReader`: the first consumer — burst-cached pinned-executable reads over `IWorkflowExecutableStore`.
- **`RuntimeBurstCacheOptions`**: `Enabled` kill switch (default on).

## Success Criteria *(mandatory)*

- **SC-001**: Executable durable reads per run: hot loop 46 → ≤ 3 (cache on); 2-node 10 → ≤ 3.
- **SC-002**: Byte-identical committed durable state cache-on vs cache-off (both cadences).
- **SC-003**: Mid-drain clear ⇒ run completes identically; two sequential drains ⇒ ≥ 1 durable re-read on the second.
- **SC-004**: Full projects green: `Elsa.Workflows.Runtime.Tests`, `Elsa.Activities.Runtime.Tests`, `Elsa.Persistence.Groundwork.Tests`; architecture guard if shape changed.
