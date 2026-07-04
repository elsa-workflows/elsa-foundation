# Durable resumption of workflow execution (PS-2 / RT-3)

> **Audience:** engineers and architects working in `elsa-foundation`.
> **Purpose:** make the difference between *durable storage* and *durable resumption* explicit, and
> document exactly which crash windows the runtime recovers from today, which one it does not, and
> why. This is the worked reference behind roadmap unit **W2** of the Elsa 4 review remediation
> program (findings **PS-2** and **RT-3**).
> **Knowledge role:** worked reference. Canonical short definitions live in
> [`docs/glossary/elsa.md`](glossary/elsa.md); the extension-point contracts live in
> [`src/Elsa/Workflows/Runtime/Core/EXTENSION_POINTS.md`](../src/Elsa/Workflows/Runtime/Core/EXTENSION_POINTS.md)
> and [`src/Elsa/Workflows/Runtime/Resumption/EXTENSION_POINTS.md`](../src/Elsa/Workflows/Runtime/Resumption/EXTENSION_POINTS.md).

## Durable storage is not durable resumption

A workflow makes progress through a repeating cycle:

1. An execution agent accepts a command and **commits a checkpoint** — the durability boundary. The
   checkpoint records new state *and* the follow-up work the commit implies as **post-commit outbox**
   items (intent kind `EnqueueSchedulerWork`).
2. The post-commit outbox is **delivered**: each `EnqueueSchedulerWork` item enqueues a
   `RuntimeSchedulerWorkItem` into the **scheduler work queue**.
3. The scheduler **drains** the queued work — running activities, scheduling children, and producing
   the next checkpoint — and the cycle repeats.

Before W2, every store that backs step 1 could be Groundwork-backed and survive a crash, yet the
runtime still lost work: `AddGroundworkRuntimeStores` swapped eleven state contracts but **not**
`IWorkflowSchedulerWorkQueue`, so delivered work landed in a process-local in-memory queue that died
with the process (**PS-2**). And nothing ever *ran* a recovery pass: the system-wide outbox sweep and
`IRuntimeRecoveryScanner` were registered but never invoked, so an item stranded between commit and
delivery — or a `FailedRetryable` outbox item with a future `AvailableAt` — waited forever for an
unrelated command to arrive (**RT-3**).

**Durable storage** means the state survives the crash. **Durable resumption** means something
*re-drives* that state to completion after the crash. W2 adds the second half.

## What W2 adds

- **A durable scheduler work queue** — `GroundworkWorkflowSchedulerWorkQueue`, an `IDocumentStore`-backed
  bridge (document kind `schedulerWorkItem`) swapped in by `AddGroundworkRuntimeStores`. Enqueue is
  idempotent by `(WorkflowExecutionId, WorkItemId)`; listing/dequeue are FIFO by
  `(RecordedAt, Sequence, WorkItemId)`; dequeue is load-first-then-delete.
- **Backlog discovery** — an additive contract method
  `IWorkflowSchedulerWorkQueue.ListPendingWorkflowExecutionIdsAsync(int limit)` returns the distinct
  execution ids that still have queued work. After a restart, nothing else knows which executions were
  interrupted; this is how the sweep finds them. Both the in-memory and Groundwork queues implement it.
- **A resumption sweep service** — `IRuntimeResumptionService` (`RuntimeResumptionService`). One
  `SweepAsync` pass:
  1. **Re-delivers** stranded post-commit outbox items **system-wide**
     (`ProcessAsync(workflowExecutionId: null, intentKind: EnqueueSchedulerWork)`), including due
     `FailedRetryable` retries — this closes RT-3.
  2. **Discovers** the interrupted executions: the union of the durable queue backlog
     (`ListPendingWorkflowExecutionIdsAsync`) and `IRuntimeRecoveryScanner` candidates.
  3. **Re-drives** each execution by enqueueing a `RunSchedulerWork` command envelope **through the
     agent mailbox** — *not* by draining from the sweep. Re-driving through the mailbox preserves the
     single-writer discipline (RT-2): the agent remains the only writer for its execution.
- **A feature-gated pump** — `Elsa.Workflows.Runtime.Resumption` is a separate package whose
  `WorkflowsRuntimeResumptionFeature` registers the service and a `RuntimeResumptionPumpTask`
  (`IRecurringTask`, scheduled by the Tasks domain). The Groundwork persistence features declare
  `DependsOn = ["WorkflowsRuntimeResumption"]`, so **selecting durable stores pulls the pump into the
  shell** — the "durable stores ⇒ pump available" invariant is machine-visible in the feature catalog.
  The runtime API feature is deliberately untouched (the pump is opt-in with durable storage).

## The idempotency / durability contract — read this carefully

The durability guarantee is **asymmetric**, and the asymmetry is the whole story:

- **Enqueue side — at-least-once.** The post-commit outbox can be redelivered (by the drain
  coordinator during normal execution, or by the resumption sweep after a crash). Redelivery can
  enqueue the same work twice, so the queue **absorbs duplicates**: enqueue is idempotent by
  `(WorkflowExecutionId, WorkItemId)`, and re-drive envelopes carry a fresh per-sweep idempotency key
  while the underlying work items stay single-instance. An execution whose backlog remains (e.g. a
  dispatch that raced a crash) is simply re-driven on the next sweep. This is why re-running the sweep
  is always safe.
- **Dequeue side — at-most-once.** Dequeue is *load-first-then-delete*: the item is removed from the
  durable queue **before** the handler that consumes it commits its own checkpoint. A crash in that
  gap loses the item, because nothing re-delivers a dequeued item.

That asymmetry — at-least-once on enqueue, at-most-once on dequeue — is what creates the one crash
window W2 does **not** close.

## Crash windows

Two recoverable windows and one residual window. The recoverable windows are covered by
`GroundworkDurableResumptionCrashTests`, which runs two provider generations over a shared
`IDocumentStore` and asserts the crashed execution converges to the same terminal state as a
crash-free control run.

### Window A — after checkpoint commit, before outbox delivery *(recovered)*

The checkpoint is durable; the outbox row is durable and `Pending`; the scheduler work was never
enqueued. On restart, the sweep's **outbox re-delivery** step delivers the row, enqueues the work, and
re-drives. ✅

### Window B — after outbox delivery, before drain *(recovered)*

The outbox row is `Delivered` and the scheduler work is durably queued, but it was never drained. On
restart, the sweep's **backlog discovery** (`ListPendingWorkflowExecutionIdsAsync`) finds the
execution and re-drives, draining the queue. ✅

### Window C — after dequeue-delete, before handler checkpoint commit *(detectable via W5; item-level replay still needs dequeue-ack)*

A work item is dequeued (removed from the durable queue) and its handler begins, but the process
crashes **before** the handler commits the checkpoint that would record the resulting progress. The
item is already gone from the queue and nothing re-delivers a dequeued item. This is the direct
consequence of the at-most-once dequeue described above.

**What W5 changes.** W5 (single-writer ownership fencing, RT-2) makes the interrupted execution
**detectable** instead of silently lost. `WorkflowDrainOrchestrator` now acquires a
`RuntimeExecutionLease` at the start of a drain (writing `ExecutionLease` + `Heartbeat` to operational
state), pushes it onto the ambient ownership scope, and releases it only in a `finally`. A crash mid-drain
therefore never runs the release, so the lease/heartbeat **persist**. Once the lease's timeout elapses,
`IRuntimeRecoveryScanner` yields the execution as a `LeaseLost`/`HeartbeatExpired` candidate — this is the
operational-state data W2 said the scanner needs — and the sweep's discovery step (§What W2 adds) unions
it with the durable backlog and **re-drives the execution through the agent mailbox** with a
`RunSchedulerWork` recovery envelope. So the execution is no longer invisible: the crash surfaces, the
single-writer discipline is preserved (re-drive goes through the mailbox), and a clean drain releases the
lease so a *completed* execution is never mistaken for an interrupted one. This is covered by
`RuntimeResumptionServiceTests.SweepAsync_DiscoversWindowCExecution_FromOwnershipLeaseLeftByCrash`
(real scanner + real ownership lease, empty backlog) plus lease-detectability/no-false-positive tests in
`RuntimeExecutionOwnershipTests`.

**What is still open.** W5 delivers the *visibility* half of the closure W2 described (lease/heartbeat +
scanner detection + re-drive). It deliberately does **not** change the dequeue itself, which remains
load-first-then-delete on `IWorkflowSchedulerWorkQueue` (owned by W2). Guaranteed *item-level* replay — so
the exact dequeued-but-uncommitted continuation is re-run rather than merely surfaced for re-drive — still
requires **drainer acknowledgement semantics**: the item must stay durably owned (not deleted) until the
consuming handler's checkpoint commits, at which point ownership is released. W5 supplies the
lease/heartbeat ownership primitive that makes such an ack-based dequeue implementable without forking
ownership semantics; wiring the dequeue to hold-until-commit is the remaining increment on W2's durable
queue. Until then, window C is **detected and re-driven** rather than **replayed at item granularity**.

## Bounding the pump

So a single restart with a large backlog — or one poisoned execution — cannot overwhelm or starve the
sweep, the pump is bounded on two axes:

- **Per tick:** `RuntimeResumptionSweepRequest.MaxExecutionsPerSweep` (default 100) caps how many
  executions one sweep re-drives.
- **Per execution:** the pump applies a geometric backoff to individual executions whose re-drive
  fails, passing them as `ExcludedWorkflowExecutionIds` so they are skipped until their backoff
  elapses. A separate whole-sweep geometric backoff (bounded by `MaxBackoffInterval`, default 5m)
  throttles after consecutive sweep failures. One poisoned execution therefore cannot monopolise the
  sweep or block healthy executions.

## Coalescing checkpoint persistence — the deferred-flush window (E3-6 / RT-10)

Everything above describes the **default** `ImmediateRuntimeCheckpointPersistencePolicy`: every named
checkpoint flushes to the durable store the moment it is decided. `AddCoalescingRuntimeCheckpointPersistence`
swaps in `CoalescingRuntimeCheckpointPersistencePolicy` — an **opt-in** durability/throughput trade,
selectable exactly like Elsa 3's commit strategies. It folds a *drain segment* of non-suspending intra-drain
checkpoints into **one atomic flush commit at quiescence**, matching Elsa 3's one-write-per-burst behaviour.
The default runtime keeps Immediate, so nothing below applies unless coalescing is explicitly enabled.

**Governing invariant — the durable scheduler queue never advances past the last flushed state.** Within a
coalesced segment, intra-drain checkpoints are buffered in an ambient in-memory working set (an overlay over
the real state stores, scheduler queue, and outbox). The segment-entry work item is **only** dequeued from the
durable queue as part of the atomic flush commit — never durably dequeued before the flush lands. So at every
instant the durable queue's frontier equals the last flushed checkpoint. A crash mid-segment discards the
in-memory buffer and leaves the segment-entry item exactly where the last flush left it, so the standard Window
B recovery (backlog discovery → re-drive) replays the entire segment from the last durable state.

**What is buffered.** Workflow-execution, activity-execution, durable-value and scheduler state writes, plus
continuation intents (`EnqueueSchedulerWork`) which are consumed **in-segment** from the overlay so a folded
segment does not durably re-record its own continuation. **What is never buffered** (condition E): W5's
lease/heartbeat/ownership operational writes and `EnsureOwnershipAsync` fencing go straight to the operational
store as today, and the single folded flush still goes through `RuntimeCheckpointCommitter.CommitAsync`, so
ownership fencing gates the coalesced flush exactly as it gates an immediate commit — a stale writer is rejected
before anything persists.

**Flush boundaries — mandatory checkpoints are never coalesced away.** The policy forces an immediate flush at
every durability-critical boundary: `WorkflowSuspended`, `WorkflowCompleted`, `WorkflowFaulted`,
`WorkflowCancelled`, `IncidentRecorded`, `ActivitySuspended`, `ActivityCancelled`, and `BookmarkCreated`. A
bookmark-suspend (including `Delay`/timer-style suspensions) flushes so an external stimulus always finds a
**durable** bookmark and can never race an in-memory-only one; a decided fault is durable at the moment it is
decided. End-of-drain quiescence also flushes even when the workflow is still `Running`/waiting. A
boundary-forced flush is **complete** (condition C): it atomically persists the folded state + the boundary
checkpoint itself + any remaining unconsumed in-memory queue items (durably re-enqueued) + undelivered outbox
intents. Nothing buffered is lost at a boundary.

**Segment cap.** `CoalescingRuntimeCheckpointPersistenceOptions.MaxSegmentCheckpoints` (default 50) bounds a
segment: once the buffered checkpoint count reaches the cap, an intermediate flush is forced. This bounds both
replay cost (a crash re-runs at most one segment) and memory (the working set holds at most one segment). See
the benchmark results doc for the replay-cost trade.

**The crash-replay window and at-least-once semantics.** A crash mid-segment loses the buffered-but-unflushed
checkpoints, but they are **replayable from the last flushed commit + durable queue redelivery** — the honest
recovery generation re-drives the segment-entry item and re-executes the whole buffered segment. This means
**in-segment activity re-execution after a crash is expected**: the crashed generation persisted no checkpoint,
so activities in the lost segment run again on recovery. This is the same at-least-once-after-persist guarantee
the Immediate path already gives on the enqueue side (§The idempotency / durability contract); coalescing simply
widens the replay window from one checkpoint to one segment. External-facing outbox intents are still delivered
**only post-flush**, so an activity's external effect is never delivered before its durable commit. Convergence
and the absence of duplicate *terminal* effects are proven by
`GroundworkCoalescingCrashConvergenceTests.Coalescing_CrashMidSegment_QueueRetainsSegmentEntry_ThenHonestSweepConvergesWithoutDuplicateEffects`
(two generations over a shared store: gen-1 crashes mid-segment with the queue still holding the segment entry;
gen-2's honest sweep converges to the crash-free control snapshot) and the queue-retention half by
`RuntimeCheckpointCoalescingTests.CrashMidSegment_DurableQueueStillHoldsSegmentEntry_AndNoPartialCheckpointPersisted`.
That a bookmark-suspend flushes its bookmark **durably** at the boundary — so W8's durable Delay/timer pump (which
reads the durable bookmark store) can never race an in-memory-only bookmark — is proven by
`RuntimeCheckpointCoalescingTests.Coalescing_BookmarkSuspend_FlushesDurableBookmarkImmediately`, and that
coalescing never wraps W8's `IDurableTimerStore` or the `IBookmarkStateStore` (so a `Delay` suspension's timer
*and* bookmark both persist directly, never through the buffer) by
`RuntimeCheckpointCoalescingTests.Coalescing_DoesNotDecorateDurableTimerOrBookmarkStores_SoDelaySuspensionStaysDurable`.

## Runtime composition root and lifetimes (RT-4)

The hosting-agnostic runtime execution spine is registered by
`RuntimeCoreServiceCollectionExtensions.AddWorkflowRuntimeCore(this IServiceCollection)` in
`Elsa.Workflows.Runtime.Core`. The FastEndpoints `WorkflowsRuntimeApiFeature` no longer owns those
registrations — it composes the Core root and then adds only its HTTP request handlers. This makes the
runtime usable from a non-HTTP host (a worker, another module, a test harness) without pulling in the
API feature. The host-agnostic guard is `RuntimeCoreCompositionRootTests`: it composes
`AddWorkflowRuntimeCore` into a bare `ServiceCollection` and drives a real Cancel drain end-to-end with
no API feature present.

**Lifetime story — deliberate, not incidental.** The reference in-memory stores, handlers, pipelines and
the drainer are registered **singleton** (process-global). That matches the reference implementation:
the in-memory stores *are* the durable state for the reference host, so a single shared instance is the
correct model. Two consequences are deliberately in scope, and one is deliberately out:

- **Overridability is preserved.** Every Core registration uses `TryAdd*`, so a durable provider package
  (EF Core, Mongo, etc.) can register its own store *before or after* `AddWorkflowRuntimeCore` and win,
  including choosing its own lifetime for that store. Composition order does not matter for correctness.
- **W9 coalescing decorators still wrap.** The opt-in `AddCoalescingRuntimeCheckpointPersistence`
  decorates the commit store / queue / outbox / state stores registered here; the Core root does not
  change their registration *shape*, so those decorators keep composing unchanged.
- **Scoped/per-request lifetimes are out of scope.** Moving stores to scoped would ripple
  captive-dependency semantics through the singleton drainer and pipelines and is not required to make
  the runtime host-agnostic. If a durable provider needs per-request scoping it overrides the specific
  store via `TryAdd` and owns that lifetime decision locally.

## Drain-path ambient service location removed (RT-7)

The drain path no longer resolves collaborators through ambient service locators. Two AsyncLocal
smugglers were deleted: the `IWorkflowExecutionAmbientServicesAccessor` that the drainer used to reach a
request-scoped `IServiceProvider`/state store, and the pipeline-context accessor that carried the mutable
workspace to handlers. Both now flow **explicitly**: the drainer injects `IWorkflowExecutionStateStore`
directly and passes the drain request's `AmbientServices` into
`IRuntimeExecutionPipelineDispatcher.DispatchAsync`, which stages it on
`RuntimePipelineWorkspace.AmbientServices`; the migrated nested-invoke handlers (`InvokeActivity`,
`ParentActivityCompletion`) read it from that workspace member instead of an AsyncLocal `.Current`.

Two ambients remain **by deliberate design**, and neither is a drain-path service locator:

- `IRuntimeExecutionOwnershipContextAccessor` — a runtime-internal AsyncLocal lease scope (RT-2/W5
  fencing) that carries the active lease from the drain coordinator to the single commit funnel.
- `IRuntimeCoalescingSessionAccessor` (**W9**) — an **opt-in ambient session flag**, not service
  location: it marks that a coalescing session is active so the decorators buffer intra-drain checkpoints
  into the in-memory working set. It is registered only by `AddCoalescingRuntimeCheckpointPersistence`
  and its gating semantics are preserved exactly ("the durable scheduler queue never advances past the
  last flushed state"). It is a documented exception to "no AsyncLocal in the drain path", distinct in
  kind from the removed service locators.

## Slot-invoked handler model (ADR 0029 Move 2)

Scheduler work handlers no longer commit inline from a terminal step. A migrated handler additionally
implements `IRuntimePipelineWorkHandler`; the dispatcher stages it on the workspace, the pipeline's
`Invoke` slot runs it, and the `Checkpoint` slot drains the handler-staged commit **list in order, one
`RuntimeCheckpointCommitter.CommitAsync` call per staged entry** — byte-identical to the previous inline
sequence. The slot never batches or folds staged commits (folding is the W9 coalescing decorators' job;
batching would change W9 boundary detection and W5 fencing granularity). The two nested-invoke handlers
are the deliberate exception: their commits go through a dynamically-resolved provider, so they commit
**inline** in the `Invoke` slot and stage nothing — converting them to staged commits would not be
behavior-preserving.

## Out of scope (owned elsewhere)

- **Ack-based dequeue** that keeps a scheduler work item durably owned until the consuming handler's
  checkpoint commits — the remaining increment for item-level window-C replay, layered on W5's
  lease/heartbeat ownership primitive over W2's durable queue.
- Drainer/handler refactors — **W1**. Runtime-spine decomposition — specs/083. Serializer-policy
  remediation (PS-3) — **W3**. Multi-node outbox delivery-ownership fencing — the Groundwork outbox
  store rejects `OwnerId` filters today, and the sweep passes none.

## Cross-references

- Program-goal bucket: [`docs/program-goals/elsa-4-review-remediation.md`](program-goals/elsa-4-review-remediation.md).
- Roadmap W2 brief: [`docs/reports/elsa-4-architecture-review-2026-07/roadmap.md`](reports/elsa-4-architecture-review-2026-07/roadmap.md).
- Runtime extension points: [`src/Elsa/Workflows/Runtime/Core/EXTENSION_POINTS.md`](../src/Elsa/Workflows/Runtime/Core/EXTENSION_POINTS.md).
- Resumption feature surface: [`src/Elsa/Workflows/Runtime/Resumption/EXTENSION_POINTS.md`](../src/Elsa/Workflows/Runtime/Resumption/EXTENSION_POINTS.md).
