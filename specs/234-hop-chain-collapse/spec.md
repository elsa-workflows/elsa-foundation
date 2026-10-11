# Feature Specification: Collapse the per-activity hop chain under per-hop persistence

**Feature Branch**: `runtime-throughput/2584-hop-chain-collapse`

**Created**: 2026-10-11

**Status**: Draft

**Program**: Runtime Efficiency ([#2531](https://github.com/elsa-workflows/elsa-foundation/issues/2531)), epic [#2564](https://github.com/elsa-workflows/elsa-foundation/issues/2564) (ADR 0080 D2, P2)

**Issue**: [#2584](https://github.com/elsa-workflows/elsa-foundation/issues/2584); measured input on [#2553](https://github.com/elsa-workflows/elsa-foundation/issues/2553) and the [spec-input comment](https://github.com/elsa-workflows/elsa-foundation/issues/2584#issuecomment-6104358279)

**Input**: User description: "Collapse the per-activity hop chain so a leaf activity under the per-hop (Immediate / future Durable) persistence mode stops paying ~6 queued hops and ~5 checkpoint commits. Durability per hop must be unchanged. Target ≤ 45 runtime commits per request for the 4-activity HTTP workflow (from ~106)."

## Why

Under per-hop persistence (`Immediate` today, `Durable` after ADR 0080 D2), the 4-activity HTTP reference workflow
makes about **106 durable commits per request** on the runtime database (#2553, SQLite, Release, one client):

| Commit kind | Per request | What it is |
|---|---:|---|
| Checkpoint transaction | 22 | One per scheduler boundary (Schedule, Start, AttemptClaimed, Completed, parent resume, …) |
| Work-item enqueue | 22 | Writing the next hop's queue item, outside the checkpoint transaction |
| Work-item claim | 22 | Taking the queue item before running it |
| Work-item delete outside a transaction | 12 | Acknowledging a hop that made no checkpoint (Start, and the three routing hops after each activity) |
| Outbox "Delivered" | 15 | Marking a continuation delivered after its queue item was written |
| Root-write lease | 8 | Out of scope here ([#2586](https://github.com/elsa-workflows/elsa-foundation/issues/2586)) |
| Liveness / placement | ~5 | Ownership acquire, heartbeat, release |

Every commit costs an fsync on SQLite and a WAL flush on PostgreSQL. On a cloud disk, SQLite with per-hop persistence
plateaus at two clients and about one request per second; PostgreSQL with per-hop persistence and diagnostics on
manages 1.33 requests per second at one client against 8.5 with Coalesced. Two thirds of the commits are not
checkpoints at all: they are the queue and outbox plumbing between them, and four of every six hops per activity
exist only to route control to the next one.

ADR 0080 D2 keeps a per-hop mode (`Durable`: "commits after every activity; resumes at the last completed activity").
That mode has to be affordable, and the routing hops in between have no durability of their own to preserve.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A continuation is recorded in the commit that produces it (Priority: P1)

As an operator running per-hop persistence, I want the next hop's work item written inside the same checkpoint
transaction that schedules it, so that each scheduled continuation costs no extra commits beyond its checkpoint.

**Why this priority**: It removes the largest single block of commits (about 30 of 106: the separate enqueue and the
separate "Delivered" mark for 15 outbox continuations) without changing what is durable or when.

**Independent Test**: Run the 4-activity HTTP reference workflow under per-hop persistence on a store whose
scheduler queue shares the checkpoint store's unit of work. Count durable commits per request; compare with the
baseline. Kill the host between a checkpoint commit and the next hop's claim; restart; the run continues from the
committed continuation exactly once.

**Acceptance Scenarios**:

1. **Given** a checkpoint that schedules a continuation and a scheduler queue that shares the checkpoint store's
   unit of work, **When** the checkpoint commits, **Then** the continuation's work item exists durably as of that same
   commit, and no separate enqueue or "Delivered" commit follows for it.
2. **Given** that checkpoint's transaction fails or rolls back, **When** the run is inspected, **Then** neither the
   checkpoint nor the continuation exists.
3. **Given** the host crashes after the commit and before the continuation is claimed, **When** the resumption sweep
   runs, **Then** the continuation is claimed and run exactly once, with the same work-item identity it was committed
   with.
4. **Given** a scheduler queue that does not share the checkpoint store's unit of work, even on the same database
   (or a continuation kind other than scheduler work), **When** the checkpoint commits, **Then** behaviour is
   unchanged: the continuation goes through the post-commit outbox as today.

---

### User Story 2 - Routing hops run in-process without their own queue round trips (Priority: P1)

As an operator running per-hop persistence, I want the routing steps after an activity completes (complete the
activity, evaluate the parent, schedule the continuation) and the Start hop to run inside the drain that owns the
execution, without each one being enqueued, claimed and acknowledged on its own.

**Why this priority**: Those hops make no checkpoint, so each one costs three commits (enqueue, claim, delete) for no
durability. Removing them takes about 38 commits off the reference workflow and also removes their claims.

**Independent Test**: Run the reference workflows under per-hop persistence; the routing hops no longer appear as
queue items, and the commit count drops by the expected amount. Kill the host at every point inside the routing pass
(the EF crash suite `EfHopCollapseCrashConvergenceTests`); after restart every run reaches the same terminal state as an uninterrupted run, with no
duplicated or lost activity execution.

**Acceptance Scenarios**:

1. **Given** an activity completes under per-hop persistence, **When** the drain routes control to the next activity,
   **Then** the routing steps run in the same drain and produce no queue item, claim or acknowledgement of their own.
2. **Given** the host crashes after an activity's completion checkpoint and before the routing pass reaches its next
   durable boundary, **When** the run recovers, **Then** the routing pass is re-derived from durable state and runs
   once to completion; no activity runs twice because of it and no continuation is lost.
3. **Given** a routing step that cannot run in-process (a join with more than one predecessor, a child fault
   evaluation, a parent that is not routable in-process), **When** it is reached, **Then** it falls back to the queued
   hop exactly as today. The workflow tail (the root's `ActivityCompleted`, `ContinuationScheduling` and the tail
   checkpoint) is routable under per-hop persistence and does not fall back (owner decision, 2026-10-11).
4. **Given** the switch of FR-009 is off, **When** any workflow runs, **Then** behaviour, commits and the resulting
   durable state are identical to today, for all three changes (FR-001, FR-004, FR-008).

---

### User Story 3 - Every acknowledgement rides a commit when one exists (Priority: P2)

As an operator, I want a claimed work item whose dispatch makes a checkpoint commit to be deleted inside that commit,
whichever service scope the handler commits from.

**Why this priority**: Smaller saving, but it closes a gap: a handler that commits through a committer resolved from a
different scope (for example the HTTP request's services) may not see the drain's pending acknowledgement, so the
drain acknowledges separately.

**Independent Test**: Trace a synchronous HTTP endpoint run on a claim-capable provider; every claimed item whose
dispatch committed is deleted in that commit; separate acknowledgements remain only for dispatches that made no commit.
On a non-atomic or legacy provider, acknowledgements are unchanged.

**Acceptance Scenarios**:

1. **Given** a claim-capable provider and a dispatch that makes a checkpoint commit through any committer in the
   drain, **When** it commits, **Then** the claimed work item is deleted in the same transaction and the drain does
   not acknowledge it again.
2. **Given** a dispatch that commits twice, an attempt-claim commit and then the outcome commit (`InvokeActivity`,
   parent completion evaluation), **When** it runs, **Then** the claimed work item is deleted in the outcome commit,
   the first commit that is not an attempt claim, so a crash between the two commits recovers exactly as today.
3. **Given** a dispatch that makes no commit, **When** it finishes, **Then** the drain acknowledges it as today.
4. **Given** a non-atomic or legacy provider, **When** a dispatch commits, **Then** the drain acknowledges it
   separately, as today (spec 105 FR-006).

---

### Edge Cases

- A checkpoint schedules several continuations: all are written in the same transaction, in the order they are
  produced today.
- A continuation's work item already exists under the same WorkItemId (redelivery, replay): the existing item is kept
  as-is and nothing is overwritten, exactly as enqueue does today (create-only by identity, ADR 0031 (a)). An in-commit
  write behaves the same way.
- The resumption sweep and the live drain race for a continuation written in the checkpoint: the queue's claim fence
  decides; the loser does nothing.
- The run pauses, is cancelled or faults during an in-process routing pass: the pass stops at the same point the
  queued chain would have stopped, and the durable state is the same.
- The workflow tail is routed in-process: the root's completion, continuation scheduling and the tail checkpoint run in
  one pass whose boundary is the tail checkpoint commit (`WorkflowCompleted` or the root's `ActivityCompleted`), which
  deletes the work item that started the pass.
- A coalesced (or Balanced) run: unaffected; it keeps its existing fusion and buffering paths.
- In-memory runtime stores: behaviour unchanged; the in-memory composition already has no commit cost.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: When the scheduler queue shares the checkpoint store's unit of work, a checkpoint MUST write the work items
  of the scheduler-work continuations it produces in the same transaction as the checkpoint, instead of recording
  them as outbox items for later delivery.
- **FR-002**: A continuation written by FR-001 MUST have the same work-item identity, payload and ordering it would have
  had through the outbox, so redelivery and replay stay idempotent by identity.
- **FR-003**: Continuations of other kinds, and all continuations on a composition whose queue does not share the
  checkpoint store's unit of work, MUST keep the post-commit outbox path unchanged.
- **FR-004**: Under per-hop persistence, the routing steps that follow an activity's completion (activity completion,
  parent completion evaluation, continuation scheduling), the workflow tail (the root's completion, continuation
  scheduling and the tail checkpoint) and the Start hop MUST run inside the owning drain without their own queue item,
  claim or acknowledgement, when the step is routable in-process.
- **FR-005**: A routing step that is not routable in-process (multi-predecessor join, child fault evaluation,
  non-routable parent, or any other case the existing fusion rules exclude) MUST fall back to the queued hop. The
  workflow tail is not a fallback case under per-hop persistence: spec 228 excluded it only because coalesced commits
  were buffered, and under per-hop persistence each tail checkpoint still commits durably (owner decision,
  2026-10-11).
- **FR-006**: An in-process routing pass MUST be re-derivable from durable state: if the host stops anywhere inside it,
  recovery MUST reach the same next durable boundary with no duplicated activity execution and no lost continuation.
  The work item that started the pass MUST stay claimed until the pass reaches a durable boundary. On a claim-capable
  provider it MUST be deleted in that boundary's commit; on any other provider it is acknowledged separately after
  that commit, as today.
- **FR-007**: Durability per activity MUST be unchanged: every checkpoint that commits today under per-hop persistence
  MUST still commit, with the same content, before the work it guards proceeds.
- **FR-008**: On a claim-capable provider (spec 105 FR-004), a claimed work item whose dispatch makes a checkpoint
  commit MUST be deleted inside that commit regardless of which committer instance in the drain performs it; there the
  drain MUST acknowledge separately only dispatches that made no commit. A dispatch that commits twice, an attempt-claim
  commit (`ActivityAttemptClaimed`) and then the outcome commit (`InvokeActivity`, parent completion evaluation),
  MUST delete its claimed work item in the first commit that is not an attempt claim, so recovery after a crash
  mid-attempt is unchanged. Every other path keeps spec 105 FR-006's
  separate acknowledgement and tripwire unchanged: non-atomic and legacy providers (including dispatches that commit),
  dispatches that made no commit, the handler-fault path, and a host that replaces the consume accessor (a replaced
  accessor is never linked; research R6, contract A5).
- **FR-009**: One switch MUST control all three changes: continuations in the checkpoint transaction (FR-001),
  in-process routing (FR-004, FR-006) and acknowledgements inside the dispatch's commit (FR-008). It defaults on.
  Off restores today's behaviour exactly, including today's separate continuation commits and acknowledgements, so
  it is the single rollback lever.
- **FR-010**: The change MUST be observable: the existing scheduler dispatch diagnostics MUST count in-process routing
  steps and fallbacks, as they do for fused spans today.

### Key Entities

- **Checkpoint commit**: the unit of durable progress; gains the continuation work items it schedules (FR-001).
- **Scheduler work item**: one queued hop; routing hops stop producing them (FR-004), continuations are written by the
  checkpoint that schedules them (FR-001).
- **Post-commit outbox item**: unchanged for non-queue continuations and for split compositions (FR-003).
- **Routing pass**: the in-process sequence of routing steps from one activity's completion to the next durable
  boundary (FR-004, FR-006).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: The 4-activity HTTP reference workflow under per-hop persistence makes **at most 52 durable commits per
  request** on the runtime database (baseline about 106) with the root-write lease (#2586) unchanged, and **at most 45**
  once #2586 lands, measured by the deterministic commit counter of the P0 gate (owner decision, 2026-10-11).
- **SC-002**: The deterministic command-count budgets of the P0 gate (#2569) drop for all three reference workflows
  under per-hop persistence, and are lowered in the same change.
- **SC-003**: All eight A1 concurrency-correctness cases (#2532) pass, and the EF crash suite
  `EfHopCollapseCrashConvergenceTests` (the two-generation cut-and-recover harness, kill points K1–K9 inside the routing
  pass, the tail pass and between a checkpoint and its continuation's claim) passes. It replaces the Groundwork crash
  suite, which ADR 0073 retired with Groundwork.
- **SC-004**: With the switch of FR-009 off, which disables FR-001, FR-004 and FR-008 together, the reference
  workflows' durable state and commit counts are identical to the baseline.
- **SC-005**: Coalesced runs' commit counts and behaviour are unchanged.

## Assumptions

- The EF runtime stores (checkpoint, scheduler queue, outbox) share one runtime context per command scope, which the
  runtime context registration already enforces; "shares the unit of work" in FR-001 means exactly that composition.
  Other compositions keep the outbox.
- ADR 0020 ("the commit records post-commit work but never dispatches it inline") permits a durable queue write
  inside the commit: writing the next work item is recording, not dispatching. Nothing runs until a drain claims the
  item, and the item exists if and only if the checkpoint committed. The owner confirmed this reading on 2026-10-11
  ([#2584](https://github.com/elsa-workflows/elsa-foundation/issues/2584#issuecomment-6104682664)), and ADR 0020
  carries a dated clarification that states it. FR-001 is accepted and US1 work may start.
- The spec-228 fusion rules (single-predecessor edges, routing memo) are the starting point for "routable in-process";
  this spec extends their use to per-hop persistence rather than defining new routing rules.
- `Immediate` is the per-hop mode until ADR 0080 D2 renames it to `Durable` (#2585); the requirements apply to
  whichever per-hop mode exists when this lands.
- Out of scope: Balanced/Ephemeral modes (#2585), the root-write lease (#2586), the drain-scoped activity-state cache
  (#2583), diagnostics stores, and the liveness lease writes.

## Dependencies

- #2569 (P0 command-count budget gate, done) is the yardstick.
- A1 correctness suite (#2532) and the EF crash suite `EfHopCollapseCrashConvergenceTests` (ADR 0073) are the safety
  net.
- Spec 105 (drain step single transaction), spec 106 (live-drain delivery), spec 109 (in-process hop fast path) and
  spec 228 (ReplaySafe hop fusion) define the behaviour this extends; the plan must reconcile with each.
