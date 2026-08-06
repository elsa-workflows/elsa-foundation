# Spec 131: Coalesced inspection-store read memo (candidate A, re-aimed from spec 130)

## Status

Implemented. Store-read-coalescing unit in the spec-110 family (characterize → refute premise → fix the store read),
executing the re-aim finding of [spec 130](../130-runtime-envelope-build-cpu/research.md): under the Coalesced cadence
the per-hop inspection-projection build paid a durable `IActivityExecutionInspectionStore.FindAsync` on every hop —
44 reads per hot-loop×10 run, 44/44 returning null (the durable inspection store is only written at the segment
flush), with 75% of the 44 builds being intermediate projections the last-write-wins fold discards.

## What was built

A coalescing overlay for the inspection store that memoizes the **durable baseline** per activity execution:

- `CoalescingActivityExecutionInspectionStore` (`src/Elsa/Workflows/Runtime/Services/Coalescing/CoalescingRuntimeStateStores.cs`):
  while a coalescing session owns the workflow execution, `FindAsync` serves a per-drain memo of what the durable
  store returned (a cached null records a durable miss); the first read per activity execution passes through and is
  memoized. `ListSummariesPageAsync` and all no-session/out-of-drain reads are byte-for-byte pass-through.
- `RuntimeCoalescingSession.TryGetInspectionBaseline / CacheInspectionBaseline / InvalidateInspectionBaselines`:
  the memo lives on the session (drain-confined, single-writer).
- `CoalescingRuntimeCheckpointCommitStore` invalidates the memo immediately after **every** durable inner commit under
  an active session (folded flush, quiescence flush, pass-through boundary), so post-flush reads observe the freshly
  flushed rows exactly like a durable read would.
- Toggle: `CoalescingRuntimeCheckpointPersistenceOptions.CoalesceInspectionReads` (default **on**; **off** is the
  byte-identical control path — per-hop durable reads, the pre-unit behavior).

## Why memoize the durable baseline instead of serving buffered overlay state

The brief's alternative — an overlay that lets mid-segment reads see the session's buffered (unflushed) projections —
was **rejected on the byte-identical guardrail**: `ActivityExecutionInspectionProjection.FromState` and `.Merge`
differ materially (`FirstCheckpointId` reset vs preserved, `OutcomeNames` reset vs retained, bookmark/incident/value
snapshot unions). Serving buffered state would flip the accumulator's `FromState`/`Merge` branch on every intermediate
hop and change the flushed projection documents relative to the control. The durable-baseline memo returns exactly
what a fresh durable read would return at every point in the drain, so flushed bytes are provably unchanged.

Memo validity rests on the single-writer invariant: inspection rows are mutated **only** by checkpoint commits
(`IActivityExecutionInspectionStore` is a read-only contract; `IActivityExecutionInspectionWriter` is consumed only by
the two checkpoint writers; the Groundwork writer validates inspections are Upsert-only and activity-scope cleanups do
not touch inspection documents), commits for the session's workflow execution are fenced by the ownership lease, and
every durable commit under the session invalidates the memo.

## Hard precondition — consumer audit (2026-07-22)

Every consumer of `IActivityExecutionInspectionStore` and `IRuntimeActivityExecutionInspectionAccumulator` was
enumerated; no in-burst reader depends on an intermediate projection existing durably pre-flush:

| Consumer | Kind | Verdict |
|---|---|---|
| `RuntimeActivityExecutionInspectionAccumulator` | in-drain, sole in-drain `FindAsync` caller | reads only to pick `FromState` vs `Merge`; memo returns durable-identical values → unchanged |
| `GetActivityExecutionRequestHandler`, `GetWorkflowInstanceRequestHandler`, `ActivityExecutionValuePayloadReader` | HTTP/API, out-of-drain | no ambient session in their async context → pass-through, unchanged (and a deactivated session never serves the memo) |
| 19 `BuildProjectionAsync` call sites (start/schedule/checkpoint/cancel/cancel-scope handlers, `BlockingIncidentWorkflowFaultObserver`, `BookmarkConsumptionCheckpointService`, `ActivityFaultIncidentRecorder`, `ActivityCancellationCheckpointService`, `WorkflowIntrinsicExecutor`, `StructuralParentEvaluationSupport`, invoke/notify-parent/parent-completion/resume handlers) | in-drain, via accumulator | all build a projection solely to embed as an Upsert in a checkpoint change-set; all tolerate both null (`FromState`) and found (`Merge`) |
| `InMemoryActivityExecutionInspectionStore`, `GroundworkActivityExecutionInspectionStore` | implementations | write path is `IActivityExecutionInspectionWriter`, called only inside checkpoint commits → invalidation-on-commit is complete |

## Guardrails and evidence

- `tests/Elsa/Workflows/Runtime/Tests/CoalescingInspectionReadTests.cs`: scripted two-segment drain (intermediate
  builds, a prior-drain durable row exercising the cross-drain `Merge` path, a mid-drain attempt-boundary flush, a
  terminal flush) run twice — memo **on** vs **off** (control). Asserts byte-identical (JSON) sequences for every
  projection built, every checkpoint commit reaching the durable store, and the final durable inspection documents;
  durable read count drops 7 → 4 (one read per distinct activity per coalesced window).
- Existing suites green: `RuntimeCheckpointFoldTests`, `RuntimeCheckpointCoalescingTests` (29),
  `GroundworkCoalescingCrashConvergenceTests` + `GroundworkRuntimeCheckpointWriterTests` (27).
- `benchmarks/.../EnvelopeBuildStageDiagnostics.cs` (spec 130 instrument, extended with an off-control row and
  cadence-aware deterministic assertions), hot-loop×10, deterministic counters:

| Row | builds | durable `FindAsync` | null misses |
|---|---|---|---|
| Immediate | 44 | 44 | 11/44 |
| Coalesced(cap256), memo **off** (control) | 44 | 44 | 44/44 |
| Coalesced(cap256), memo **on** | 44 | **11** | 11/11 |

The 33 absorbed reads are exactly the intermediate builds the fold discards (4.00× builds:activities); Immediate and
the off-control preserve the pre-unit shape byte-for-byte.

## Non-goals

- The Coalesced-vs-Immediate projection fidelity divergence that pre-dates this unit (under Coalesced, all-null reads
  make every build take `FromState`, so the flushed doc's `FirstCheckpointId`/merge history differs from Immediate's
  `Merge` chain) is **out of scope**: the pinned control is the pre-unit Coalesced behavior, not Immediate. If that
  divergence is ever judged a defect, fixing it is a separate behavioral unit.
- No skip/deferral of the projection *builds* themselves: spec 130 measured pure construction CPU at 0.03–0.70% of
  in-memory hot-loop CPU; only the store read was worth removing.

## Limited supersession — logical inspection composition (2026-08-06)

This section records a later composition contract; it does **not** rewrite the 2026-07-22 evidence above. The
original rejection in [Why memoize the durable baseline instead of serving buffered overlay state](#why-memoize-the-durable-baseline-instead-of-serving-buffered-overlay-state) and the first non-goal are superseded **only** where an equivalent logical checkpoint sequence crosses a physical persistence boundary differently. The triggering case is an intermediate checkpoint that is physically immediate because it has generic post-commit work while the equivalent fused path remains deferred because it elides only a transport continuation.

For that case, the runtime-owned activity-execution inspection projection is a composition over the ordered
**logical** checkpoint sequence, not over accidental immediate/deferred transport boundaries. Equivalent sequences
must retain the same logical `FirstCheckpointId` and the defined cumulative outcome, bookmark, incident, value, and
metadata semantics, and must produce byte-identical checkpoint and inspection-state documents. A successful durable
checkpoint becomes the authoritative baseline for subsequent logical composition; no later composition may use a
stale earlier durable baseline.

The visibility and recovery boundaries remain strict. Readers outside the active coalesced session see durable
inspection truth only. If a session ends or crashes before later logical accumulation becomes durable, recovery starts
from durable truth and deterministically reconstructs the later logical accumulation through normal replay; volatile
session state is neither exposed as durable truth nor retained as a recovery authority.

This is a generic Runtime contract. It does not branch on fusion, Execution Evidence, provider, source authority, or
checkpoint name, and it does not change inspection fields into evidence capture. Coalescing-read/memo and ReplaySafe
fusion controls may change locality or transient transport bookkeeping but cannot change checkpoint/state documents.
Spec 123's byte-identity guardrails therefore remain strict: the sole permitted comparison exclusion identified by
spec-123 research §8.2 is transient post-commit intents and outbox rows, not any durable inspection or other state family.
