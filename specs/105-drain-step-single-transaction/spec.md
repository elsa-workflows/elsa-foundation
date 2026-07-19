# Feature Specification: Single Durable Transaction per Drain Step (fold work-item ack into the checkpoint commit)

**Feature Branch**: `worktree-agent-a2a8fa83acb2bbf9a`

**Created**: 2026-07-19

**Status**: Draft

**Input**: WU-1 of the runtime engine-performance effort under the [Runtime Execution Seam](../../docs/program-goals/runtime-execution-seam.md) bucket. Fold the scheduler work-item acknowledgement (post-dispatch `CompleteClaimAsync`) into the same durable unit-of-work as the checkpoint commit, so a drain step performs one durable transaction instead of two. Implementable now under ratified [ADR 0020](../../docs/adr/0020-runtime-checkpoint-commit-post-commit-work.md).

## Context

A drain step today performs at least two independent durable writes: the checkpoint commit (already multi-store atomic through `GroundworkRuntimeCheckpointWriter.ApplyAtomicallyAsync`, one commit-ledger unit-of-work spanning ~15 document kinds under `TransactionBoundary.CrossUnitAtomic`), and then a separate work-item acknowledgement — the pre-dispatch `ClaimAsync` visibility fence plus the post-dispatch `CompleteClaimAsync` (`WorkflowSchedulerDrainer.AckAsync`) that permanently removes the item. A measured 2-node workflow costs ~40–60 durable SQLite transactions largely because each drain step commits its checkpoint and acks its work item separately.

The checkpoint commit already carries multiple state kinds atomically (workflow execution, scheduler, activity execution, post-commit outbox, execution-liveness fence, commit marker, and — via `ActivityScopeCleanupRequest` — targeted scheduler work-item deletes). This unit adds the **claimed** work item's fence-checked delete to that same unit-of-work, then skips the now-redundant separate ack.

The `SchedulerWorkItemDocumentKind` is already inside the checkpoint commit scope (`GroundworkRuntimeCheckpointWriter.RuntimeCheckpointCommitScope`), so no new document kind and no storage-manifest change is required.

## Scope boundary

- **In scope**: the atomic fold for the two claim-capable durable providers (`InMemoryWorkflowSchedulerWorkQueue`, `GroundworkWorkflowSchedulerWorkQueue`); the drainer skipping the separate ack on the atomic path; claim-lost detection when a stale claimant commits.
- **Out of scope (preserved unchanged)**: the legacy single-writer dequeue path for non-claim-capable providers; the handler-fault ack-on-fault path (`#412` item 3); the coalesced-mode per-segment durable queue advance (`RuntimeCoalescingSession.AdvanceInnerQueueAsync`), which already batches queue advance and is kept authoritative in that mode.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A drain step commits its checkpoint and acks its work item in one durable transaction (Priority: P1)

When the drainer dispatches a claimed work item on a claim-capable provider and the handler commits a checkpoint, the committed work item removal happens inside the checkpoint commit's unit-of-work, and no second durable transaction is issued for the ack.

**Why this priority**: This is the entire point of the unit — it halves the durable transaction count for the dominant single-commit drain steps.

**Independent Test**: Drive a real drain step through a claim-capable provider whose checkpoint store records committed transactions; assert exactly one durable commit and that the work item is gone.

**Acceptance Scenarios**:

1. **Given** a claimed work item and a handler that commits one checkpoint, **When** the drainer dispatches it, **Then** the work item is deleted inside the checkpoint commit and the separate `CompleteClaimAsync` is not invoked.
2. **Given** the checkpoint commit rolls back (crash before commit), **When** the drain step is retried, **Then** the work item is still claimed and is redelivered idempotently (strengthened `#412` redrive-safety: the committed-but-unacked window is removed).

### User Story 2 - A stale claimant's commit fails claim-lost instead of deleting successor-owned work (Priority: P1)

If a work item was reclaimed by a successor drain (its claim fencing token advanced), a stale claimant that tries to commit — and thereby consume the item — fails with a claim-lost signal and persists nothing, rather than deleting work the successor now owns.

**Why this priority**: Correctness — the fold must not weaken single-writer/claim fencing. It must reproduce the existing claim-lost detection that `CompleteClaimAsync` provides.

**Independent Test**: Claim an item, let a successor reclaim it (advancing the fencing token), then commit from the stale claimant; assert the commit fails claim-lost and the successor-owned item survives.

**Acceptance Scenarios**:

1. **Given** an item reclaimed by a successor (fencing token advanced), **When** the stale claimant commits with a consume-change for that item, **Then** the commit fails claim-lost and nothing in the change set is persisted.
2. **Given** the claim was merely renewed (fencing token and owner unchanged, only visibility/revision advanced), **When** the same claimant commits, **Then** the consume succeeds — renewal must not be mistaken for reclaim.

### User Story 3 - Handler faults, coalesced mode, and legacy providers keep working (Priority: P1)

**Why this priority**: The fold must not regress the fault/poison path, the coalesced batching mode, or non-claim-capable providers.

**Independent Test**: Run the existing poison-drain, coalescing, and legacy-provider suites plus new targeted tests.

**Acceptance Scenarios**:

1. **Given** a handler that faults before committing any checkpoint, **When** the drainer handles the fault, **Then** the item is ack-deleted through the legacy fault path and poisoned exactly once (bounded delivery preserved).
2. **Given** an active coalescing session, **When** deferred checkpoints are buffered and flushed, **Then** the durable scheduler-queue advance stays owned by the session's overlay/`AdvanceInnerQueueAsync` and the fold utility unions the per-hop consumed work items without loss or duplication.
3. **Given** a non-claim-capable (legacy) provider, **When** a drain step runs, **Then** the drainer uses the original list/dispatch/dequeue path with the RT-2 TOCTOU tripwire, unchanged.

## Requirements *(mandatory)*

- **FR-001**: A `ConsumedSchedulerWorkItem` record (workflow execution id, work-item id, claim owner id, claim fencing token) MUST carry the claimed item's fence identity into the checkpoint commit change set via `RuntimeCheckpointStateChangeSet.WithConsumedSchedulerWorkItems(...)`, mirroring the post-commit-outbox / activity-scope-cleanup folding patterns.
- **FR-002**: The `RuntimeCheckpointCommitter` MUST attach the consume-change from the ambient claim when a checkpoint commits during that dispatch, and MUST assert the store durably consumed exactly the folded set (the acknowledgement-count guard analog of the post-commit-outbox guard).
- **FR-003**: The claim identity MUST reach the committer through the drain's explicitly-threaded ambient services (a scoped accessor shared by the drainer and the committer), not a new AsyncLocal service locator (RT-7).
- **FR-004**: Both claim-capable durable providers MUST perform the consumed-work-item delete inside the existing checkpoint unit-of-work. The delete MUST be fence-checked against the claim owner id and fencing token (renewal-stable), so a stale claimant's commit fails claim-lost rather than deleting successor-owned work; a renewed claim (same owner/token) MUST still succeed.
- **FR-005**: The coalescing decorator/session MUST stay consistent: while an active coalescing session owns the workflow execution, the committer MUST NOT fold the consume-change (the session's overlay queue + `AdvanceInnerQueueAsync` remain the authority on durable queue advance), and `RuntimeCheckpointFold` MUST union `ConsumedSchedulerWorkItems` across a folded segment without loss or duplication.
- **FR-006**: On the atomic path (claim-capable provider AND the commit durably carried the consume-change) the drainer MUST skip the separate `CompleteClaimAsync`. On every other path (non-atomic/legacy provider, a dispatch that produced no commit, the handler-fault path) it MUST retain the legacy ack and the RT-2 TOCTOU tripwire.
- **FR-007**: A stale-claimant commit that fails the consume fence MUST surface as a claim-lost outcome the drainer treats like `RuntimeSchedulerWorkClaimLostException`: neither acknowledge nor poison the item (a successor owns it).
- **FR-008**: Replay of an already-committed drain step (redelivery) MUST remain idempotent: the durable replay marker records the consumed work-item ids and returns them, so the acknowledgement-count guard passes on replay without re-deleting.

## Invariants that MUST survive

- **#412 redrive-safety**: crash before commit leaves the item claimed → visibility timeout → idempotent redelivery. Folding removes the committed-but-unacked window (strengthens the invariant).
- **W5 terminal-status guard and RT-2 single-writer fencing**: unchanged; the ownership fence is still validated inside the commit unit-of-work.
- **Handler-fault path (#412 item 3)**: a faulted handler has no successful commit, so the fault path still ack-deletes through the legacy path — bounded poison delivery keeps working.

## Success Criteria *(mandatory)*

- **SC-001**: A single-commit drain step on a claim-capable provider issues exactly one durable checkpoint transaction (down from two) and leaves the work item consumed.
- **SC-002**: A stale-claimant commit fails claim-lost and persists nothing; the successor-owned item survives.
- **SC-003**: Handler faults poison exactly once; coalesced mode converges to an empty durable queue; legacy providers are byte-for-byte unchanged.
- **SC-004**: The full runtime and Groundwork persistence test projects pass.
