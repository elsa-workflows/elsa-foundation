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
**detectable** instead of silently lost. `WorkflowExecutionDrainCoordinator` now acquires a
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
