# Feature Specification: In-memory live-drain EnqueueSchedulerWork delivery (Immediate mode)

**Feature Branch**: `worktree-agent-a456304a41c9336d5`
**Created**: 2026-07-19
**Program**: Runtime Execution Seam (ADR 0031, being ratified in parallel)
**Work unit**: WU-2 (follows WU-1: work-item ack folded into the checkpoint commit UoW)
**Input**: The ratified architecture plan for the Runtime Execution Seam, ADR 0031 decisions (a) `IWorkflowSchedulerWorkQueue.EnqueueAsync` is idempotent by work-item identity and (b) single-writer-per-execution is the parallelism ceiling, and the coalescing overlay precedent (E3-6 / RT-10).

## Why (measured)

In Immediate mode (the spec-095-bounded default cost model) each activity hop currently costs a durable
outbox round-trip *on top of* the checkpoint commit. The checkpoint commit atomically appends an
`EnqueueSchedulerWork` intent to the post-commit outbox (a good crash backstop). Then
`RuntimePostCommitOutboxProcessor.ProcessAsync` **claims** the intent (Pending -> Delivering, one durable
write), **enqueues** the next scheduler work item (one durable write), and **records** the intent Delivered
(Delivering -> Delivered, one durable write). The live drain loop consumes that enqueued work item
immediately, in the same in-process loop, so the durable *claim* coordination round-trip buys nothing: it
exists to fence concurrent competing deliverers, and while a live drain owns the execution there are none
(single-writer ceiling, decision (b)).

Coalesced mode already avoids this via the `RuntimeCoalescingSession` overlay. Immediate mode should get the
same fast path with the durable outbox kept purely as a crash backstop.

## User Story 1 - Cheaper straight-line hops (Priority: P1)

As a runtime operator, I need each Immediate-mode activity hop to cost fewer durable writes without losing
crash recovery, so that straight-line workflows drain faster while remaining exactly-once.

**Independent Test**: Drive a multi-hop Immediate-mode drain and assert every continuation intent ends
`Delivered` with its outbox fencing token unchanged (proving the durable claim round-trip was skipped), the
next work item is enqueued, and the drain quiesces with `RuntimeSchedulerDrainStopReason.Quiesced`.

## User Story 2 - Unchanged crash recovery (Priority: P1)

As a runtime operator, I need a crash anywhere in the fast path to converge on exactly one execution of each
hop, so that the fast path never trades durability for speed.

**Independent Test**: Simulate a crash after the durable enqueue but before the Delivered mark (intent still
`Pending`); run the resumption sweep and assert it re-drives the intent through the durable claim path, the
idempotent enqueue dedupes to a no-op, and the item converges to `Delivered` with no duplicate work item.

## User Story 3 - Coalescing stays authoritative (Priority: P1)

As a runtime maintainer, I need the Immediate fast path to never engage while a coalescing session is active,
so that the two persistence policies compose without double delivery.

**Independent Test**: With a coalescing session active for the execution, process the outbox and assert the
durable claim path ran (fencing token advanced), not the in-memory fast path.

## Functional Requirements

- **FR-001**: While a live drain owns an execution's intent delivery (a drain-scoped marker on
  `IRuntimeLiveDrainDeliveryAccessor`, mirroring `IRuntimeCoalescingSessionAccessor`),
  `RuntimePostCommitOutboxProcessor.ProcessAsync` MUST deliver `EnqueueSchedulerWork` intents for that exact
  execution in-memory: enqueue the continuation through the queue's idempotent `EnqueueAsync` and mark the
  durable outbox item `Delivered` through the existing recording contract (`EffectiveStatus=Delivered`,
  `DeliveryAttemptCount` saturating-incremented), WITHOUT the durable claim round-trip.
- **FR-002**: The marker MUST be pushed only by `WorkflowDrainOrchestrator.DrainCoreAsync` on the Immediate
  (no coalescing scope factory) branch, bounded by the drain's RT-2 single-writer lease, and popped when the
  drain returns.
- **FR-003**: The fast path MUST NOT engage when a coalescing session is active for the execution; the
  coalescing overlay is authoritative. The orchestrator MUST NOT push the marker on the coalescing branch, and
  the processor MUST additionally guard against an active coalescing session.
- **FR-004**: Intent kinds other than `EnqueueSchedulerWork` MUST stay on the durable claim path unchanged,
  even under an active live-drain marker.
- **FR-005**: A live-drain marker for a *different* execution MUST NOT divert the current request's delivery to
  the fast path.
- **FR-006**: In-memory-delivered intents MUST be counted in `RuntimePostCommitOutboxProcessResult.DeliveredCount`
  so the orchestrator's cycle loop continues while continuations remain and exits on quiescence; the
  `RuntimeSchedulerDrainStopReason` contract (`Quiesced` / `OutboxDeliveryFailed` / `Faulted` / `Paused`) MUST
  be preserved.
- **FR-007**: Callers with no live-drain marker (notably the `RuntimeResumptionService` recovery sweep and any
  third-party v1 store) MUST take their existing paths unchanged.

## Crash-safety invariants (pinned by tests)

- **INV-1 (enqueue lost)**: Crash after the checkpoint commit but before in-memory delivery -> the intent is
  durable in the outbox (`Pending`). The `RuntimeResumptionPumpTask` sweep re-drives it through the claim path;
  idempotent enqueue-by-identity plus the invoke handler's terminal-status short-circuit make redelivery a
  no-op.
- **INV-2 (mark lost)**: Crash after the durable enqueue succeeded but before the `Delivered` mark -> the item
  is still `Pending`. The sweep re-enqueues (dedupe no-op) and records `Delivered`; no duplicate work item, no
  re-execution.
- **INV-3 (single-writer)**: The marker is only ever ambient while the drain holds the execution's ownership
  lease, so no competing deliverer races the claim-free write for the same execution (decision (b)).

## Durable-transaction-count delta per hop

Per straight-line hop that produces one `EnqueueSchedulerWork` continuation (the checkpoint commit is
unchanged and counted separately):

| Post-commit delivery step        | Before (claim path) | After (in-memory fast path) |
|----------------------------------|--------------------:|----------------------------:|
| Claim (Pending -> Delivering)    | 1                   | 0                           |
| Enqueue next work item (durable) | 1                   | 1                           |
| Record Delivered                 | 1 (Delivering->Delivered) | 1 (Pending->Delivered) |
| **Delivery writes / hop**        | **3**               | **2**                       |

Delta: **-1 durable write per hop (-33% of the delivery overhead)**. Over an N-hop straight-line run the
delivery cost falls from `3N` to `2N`. The durable enqueue is retained deliberately (ADR 0031 decision (a)):
it is the actual next work and its idempotency-by-identity is what makes crash redelivery a no-op.

## Success Criteria

- **SC-001**: A 2-hop Immediate drain delivers its continuation in-memory (outbox item ends `Delivered`,
  fencing token 0), enqueues the next work item, counts the delivery, and quiesces with stop reason `Quiesced`.
- **SC-002**: A recovery sweep after successful in-memory delivery is a no-op (nothing deliverable, no
  duplicate work item).
- **SC-003**: The INV-2 crash window converges idempotently to `Delivered` with a single work item.
- **SC-004**: An active coalescing session keeps the durable claim path (fencing token advances).
- **SC-005**: Non-`EnqueueSchedulerWork` intents and different-execution markers keep the durable claim path.
- **SC-006**: Full `Elsa.Workflows.Runtime.Tests`, `Elsa.Persistence.Groundwork.Tests`, and
  `Elsa.Activities.Runtime.Tests` pass.

## Edge Cases

- **A v1 store with no claim capability** already delivers claim-free; the fast path reuses that exact
  mechanism, so behavior is identical whether the fast path is forced by the marker or by the store's lack of a
  claim capability.
- **A failed dispatch during in-memory delivery** records the failure through the same non-claim recording
  contract (`FailedRetryable` / `FailedFinal` with correct `DeliveryAttemptCount`) and is retried by a later
  sweep; the fast path changes only the success path's claim avoidance.

## Deviations from the ratified plan

- **`RuntimeSchedulerPostCommitIntentDispatcher` was not modified.** The plan suggested "factor the enqueue
  target." The in-memory ack path reuses the existing `IRuntimePostCommitIntentDispatcher.DispatchAsync` seam,
  which already enqueues through the shared idempotent `IWorkflowSchedulerWorkQueue`. No separate enqueue
  target is needed, so the dispatcher is unchanged.
- **The acknowledgement path is the processor's existing claim-free branch, forced by the marker.** Rather than
  adding a parallel delivery method, `ProcessAsync` forces its existing `GetDeliverableAsync` +
  `ProcessItemAsync(claim: null)` branch (which already enqueues and records `Delivered` via the authoritative
  non-claim recording contract) when `DeliversInMemory(request)` is true. This keeps idempotency bookkeeping in
  one place and is strictly DRY.
