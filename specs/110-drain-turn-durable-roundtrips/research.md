# Research — per-drain-turn durable round-trips under Coalesced cadence

This is the **STEP 1 characterization** for the "collapse per-drain-turn durable commit round-trips"
work unit. It determines empirically what is durable in each per-turn commit under the shipped
Coalesced cadence, using an instrumented run of the real in-process dispatch/drain path over
on-disk WAL SQLite (the same harness shape as `benchmarks/.../EngineExecutionBenchmarks`).

## Headline finding (the premise is refuted; the unit is re-aimed)

The briefing's premise — *"12 `elsa.runtime.checkpoint.commit` spans per run totaling ~137ms, and
each `CommitAsync` still executes a durable round-trip (~11ms each, fsync-scale), even though the
coalescing layer folds documents"* — **does not hold under Coalesced cadence.** Under Coalesced:

- **Buffered (deferred) per-turn checkpoints do not touch the durable store at all** — they are
  overlay-only, no fsync. `CoalescingRuntimeCheckpointCommitStore.CommitAsync` returns after
  `session.BufferDeferred(commit)` without ever calling the inner Groundwork store
  (`src/Elsa/Workflows/Runtime/Services/Coalescing/CoalescingRuntimeCheckpointCommitStore.cs`).
  The `elsa.runtime.checkpoint.commit` OTel span (started in `RuntimeCheckpointCommitter.CommitAsync`)
  wraps the whole committer call, so a *buffered* span exists but carries no durable I/O; only the
  *flushed* spans round-trip. The "12 spans" are committer calls, not 12 durable round-trips.
- **The per-turn durable-commit collapse the unit set out to build already ships** (ADR 0032 / specs
  105 + 107 + 108). Coalescing folds the whole burst's checkpoints, work-queue transitions, and
  outbox into one atomic commit at the flush boundary.

The task explicitly anticipated this branch: *"If it turns out the 12 round-trips are actually
overlay-only (no fsync) and the 137ms is elsewhere, SAY SO and re-aim the unit at what the evidence
shows."* The evidence says exactly that. The unit is re-aimed at the **only** per-drain-turn durable
round-trip coalescing does not remove: the redundant **executable-artifact reads**.

## Method

A counting decorator was inserted over `IWorkflowExecutableStore` (lease + `FindAsync`) and over the
durable `IWorkflowSchedulerWorkQueue` (before the coalescing decorator captures it as the inner
queue), and the durable `checkpointCommit` marker documents were counted after each run (one marker
is written per real durable checkpoint flush, so `documents/run` == durable checkpoint transactions
per run). The permanent form of this instrument lives at
`benchmarks/.../DurableRoundTripDiagnostics.cs`. Two graphs were driven end-to-end (dispatch →
activate actor → enqueue Start → drain to completion):

- **2-node**: `Flowchart` root → one `WriteLine` leaf (unmarked ⇒ `SideEffectProfile.External`).
- **hot-loop×10**: `Flowchart` root → straight-line chain of 10 pure `NoOpStep` leaves
  (`[ActivitySideEffectProfile(ReplaySafe)]`), the ADR 0032 target shape.

Each was run under **Immediate** (runtime default) and **Coalesced** (opt-in;
`MaxSegmentCheckpoints = 256` for the hot loop so the cap never trips mid-burst).

## Characterization table — durable round-trips per run

| Scenario | checkpoint commits (fsync writes) | durable scheduler-queue ops | root-write-lease writes | executable reads (`FindAsync`) |
| --- | --- | --- | --- | --- |
| 2-node · **Immediate** | 12 | enqueue 13 + claim 21 + complete 7 = **41** | acquire 2 + release 2 = 4 | 10 |
| 2-node · **Coalesced** | **2** | enqueue 2 + dequeue 2 = **4** | acquire 2 + release 2 = 4 | 10 |
| hot-loop×10 · **Immediate** | 66 | enqueue 58 + claim 102 + complete 34 = **194** | acquire 2 + release 2 = 4 | 46 |
| hot-loop×10 · **Coalesced** | **1** | enqueue 1 + dequeue 1 = **2** | acquire 1 + release 1 = 2 | 46 |

(`claim`/`complete`/`enqueue` are the durable per-hop scheduler-queue transitions the Immediate path
pays; under Coalesced they are served from the in-memory overlay queue and never reach the durable
store — the durable queue is advanced once per segment by `RuntimeCoalescingSession.AdvanceInnerQueueAsync`.)

### What each durable round-trip is, per the suspects in the brief

- **(a) The execution-liveness fence validate-and-touch** (`GroundworkRuntimeCheckpointWriter.
  ValidateAndTouchExpectedFenceAsync`). **Not a per-turn cost.** It runs *inside*
  `ApplyAtomicallyAsync`, i.e. only on a real flush, and its load + touch happen inside the same
  commit-ledger unit-of-work as the state changes — one fsync, not a separate round-trip. A buffered
  turn never reaches it. Verified: buffered turns issue zero durable ops.
- **(b) Consumed-scheduler-work-item deletes (spec 105).** Under Coalesced the committer suppresses
  the spec-105 fold (`RuntimeCheckpointCommitter.ResolveConsumedWorkItems` returns `[]` when a
  session owns the execution); the overlay queue is authoritative and the durable queue is advanced
  once per segment. Measured hot-loop advance = **1 dequeue + 1 enqueue** for the whole 10-activity
  burst (not one per activity) — `AdvanceInnerQueueAsync` computes the net consumed prefix, so the
  advance is bounded by the seeded frontier, not the hop count. Already minimal.
- **(c) The coalescing overlay's own bookkeeping.** In-memory only. `BufferDeferred` mutates the
  overlay working set; nothing durable. Confirmed by zero durable ops on buffered turns.
- **(d) Commit markers / replay fingerprints.** One marker document per real flush, written inside
  the same unit-of-work (`MarkCommittedAsync`). Count == flush count (2-node 2, hot-loop 1). Not a
  separate round-trip.

### The root-write lease is workflow-level, not per-turn

`GroundworkRuntimeCheckpointWriter.ExecuteWithWorkflowExecutionRootWriteLeaseAsync` wraps a commit in
a `TryAcquire`/`Release` lease **only when the commit carries a `WorkflowExecution` state change**.
Per-activity checkpoints (`ActivityAttemptClaimed`, `ActivityCompleted`) carry only
activity/scheduler state and take the early-return path with no lease. So lease acquire/release is
~2/run (start checkpoint + terminal), **independent of activity count** (2 for both the 12-flush
2-node and the 66-flush hot loop). The lease is retention/GC protection for the executable artifact,
orthogonal to the RT-2 execution single-writer fence (which is the liveness fence in (a)). Not a
per-turn target; the earlier "batch the fence-touch per drain" hypothesis does not apply here.

## The residual: redundant durable executable-artifact reads

`IWorkflowExecutableStore.FindAsync` is called **~5×/activity** and survives coalescing entirely
(46 reads for the 10-activity hot loop, 10 for the 2-node). Every call resolves the **same** pinned,
immutable, content-addressed executable artifact (ADR 0038: one artifact per distinct behavior, keyed
by `ArtifactId`, byte-identical for a given id). The call sites are the hot-path handlers and the
dispatcher — `WorkflowStartSchedulerWorkHandler`, `WorkflowInvokeActivitySchedulerWorkHandler`
(via activity/template resolution), `WorkflowCompleteActivitySchedulerWorkHandler`,
`WorkflowCreateBookmarkSchedulerWorkHandler`, `WorkflowStartDispatcher` — each independently loading
the pinned executable from the durable store to construct/resolve activities for its hop.

These are durable **reads** (point loads on WAL SQLite — cheaper than an fsync write but still a store
crossing, and repeated per hop they dominate the per-turn durable-op count once the commit storm is
folded away). They are the last per-drain-turn durable round-trip class that coalescing's
write-batching does not touch, and they are trivially safe to serve from a reconstructible cache
because the artifact is immutable by `ArtifactId` and pinned/retained for the duration of the run.

## Relationship to ADR 0031 (burst) and 0032 (cadence)

- **ADR 0032 (cadence)** removes the per-hop *commit* cost. **Already shipped and measured optimal**
  above (hot-loop 66 → 1 commit; the per-hop claim/complete/enqueue storm 194 → 2).
- **ADR 0031 (burst)** removes the per-hop *JSON serialize/deserialize and fresh-DI-scope* cost and
  adds a **burst-scoped reconstructible cache for heavy user objects**. The executable-artifact read
  is *runtime infrastructure*, not a user object, so a runtime-owned executable read cache is
  complementary to 0031's user-object cache, but it lives in the same drain-locality direction and
  must obey the same invariant: **durable state is truth; the cache is a reconstructible accelerator,
  never a correctness dependency** (a cold cache falls back to `FindAsync` with byte-identical
  results). This unit is sequenced to *not* introduce a second competing ambient-scope mechanism: it
  reuses the drain's DI scope (the same scope ADR 0031's fast path already opens) rather than a new
  `AsyncLocal` accessor (RT-7).

## Verdict

The per-drain-turn durable **commit** collapse the unit was scoped to build is already delivered by
the shipped coalescing policy. Re-aim the actionable work at the redundant per-drain-turn durable
**executable-artifact reads** — a small, safe, non-colliding drain-scoped immutable read cache — and
keep the durable-round-trip diagnostic as the acceptance instrument (durable transactions per drain
turn, not just commit documents per run).
