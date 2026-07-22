# Feature Specification: Per-hop envelope-building CPU — measure-first characterization

**Feature Branch**: `worktree-agent-a53fdd2b03c8c08c0`

**Created**: 2026-07-22

**Status**: Complete — **KILL verdict** (envelope-building-CPU hypothesis refuted; instrument landed as permanent tripwire; store-read follow-up re-aimed). See [research.md](./research.md).

**Input**: Engine-performance work unit under the [Runtime Execution Seam](../../docs/program-goals/runtime-execution-seam.md) bucket (phase 4, track 3): *"per-hop envelope-building CPU."* Spec 112's diagnostics, sharpened by planning at origin/main, flagged that every activity still pays **three** stage-core envelope builds per hop in **both** the discrete and the spec-123 fused paths (`BuildScheduledCommitAsync`, `BuildStartedCommitAsync`, `BuildCompletedCommitAsync`). Spec 123 removed *dispatches*, not *construction*. These per-hop CPU costs are upstream of the committer and therefore invisible to `BufferedCommitStageDiagnostics`, whose timers only start at `CommitAsync`.

## The lead (verified in source at origin/main)

Per hop, per stage core, upstream of the committer:

1. **Metadata snapshots** — a 9-entry metadata dict built and passed through `RuntimeModelMetadata.Snapshot` (a `ToDictionary` + `ReadOnlyDictionary` wrap) ×3 per activity, plus further copies inside `ActivityExecutionInspectionProjection.FromState`/`Merge` (each does its own `ToDictionary` merge then re-`Snapshot`).
2. **Inspection projection build = a per-hop async STORE READ.** `RuntimeActivityExecutionInspectionAccumulator.BuildProjectionAsync` calls `store.FindAsync(...)` on **every** hop. Confirmed in source: `CoalescingRuntimeStateStores` overlays only the WorkflowExecution / ActivityExecution / DurableValue / Scheduler stores — there is **no coalescing overlay** over `IActivityExecutionInspectionStore`. So mid-burst reads bypass the overlay and hit the durable baseline (SQLite `QueryAsync`).
3. **Continuation work-item payload serialize** — `JsonSerializer.SerializeToElement(payload)` per hop (a genuine serialize, not deferred to flush).
4. **`RuntimeCheckpointStateChangeSet` ctor** — eight `ValidateStateIdMatches` LINQ `.Any` validation passes + null-coalescing collection allocations.

NOT paid per hop: heavy state serialization (POCOs ride to flush — spec 112 proved `GroundworkRuntimeCheckpointWriter` serializes only at flush). Fold headroom: `RuntimeCheckpointFold`/MergeBuffer is last-write-wins by `StateId`, so the intermediate Scheduled/Started projections are built fully then discarded at fold.

## Stage 1 — the instrument (no production code)

`benchmarks/Elsa/Workflows/Runtime/Benchmarks/EnvelopeBuildStageDiagnostics.cs`, modeled on `BufferedCommitStageDiagnostics` (WorkflowExecutionHarness, SQLite document store, hotloop10, `[InlineData]` rows for Immediate + Coalesced(cap256)) and the counter style of `RuntimeSchedulerDispatchDiagnostics`. It measures, per hop and per cadence mode:

- **(a)** `BuildProjectionAsync` wall time + call count, via a benchmark-local timing decorator on `IRuntimeActivityExecutionInspectionAccumulator` (registered last so both discrete and fused paths resolve it), plus a decorator on `IActivityExecutionInspectionStore.FindAsync` to isolate the store-read share of the build.
- **(b)** total non-committer stage time — the inter-commit gap trick: the wall between the end of one committer→store call and the start of the next, attributed to the same drain = an upper bound on per-hop envelope build.
- **(c)** allocations via process-wide `GC.GetTotalAllocatedBytes()` bracketing the run (robust under async thread-hops) plus per-hop `GC.GetAllocatedBytesForCurrentThread()` bracketed inside the accumulator decorator (thread-affine, best-effort), plus GC collection counts.

Denominator and kill/proceed criteria are pinned in [research.md](./research.md) **before** the run. Machine discipline: `uptime` before/after; parallel fleet sessions share this box, so counters and per-hop ratios are the evidence, and wall-sensitive comparisons are run order-swapped. Because numerator and denominator are captured **in the same run** under the same load, their ratio is load-invariant to first order.

## The kill/proceed gate (verdict recorded in research.md either way)

- **KILL** if envelope-build < 5% of in-memory hot-loop CPU AND allocations < ~2 KB/hop → land the instrument as a permanent tripwire (like `DurableRoundTripDiagnostics`), write the verdict, add a one-line entry to `docs/program-goals/runtime-execution-seam.md`, STOP. That is a complete unit.
- **PROCEED to Stage 2** only if inspection-projection build alone ≥ ~10% of in-memory hot-loop CPU, or total envelope build ≥ 15%.
- **RE-AIM** if item 3 (payload serialize) dominates → Stage 2 targets candidate C, not A.

## Scope boundary

- **In scope (Stage 1)**: the permanent envelope-build diagnostic + the pinned verdict. No production code.
- **In scope (Stage 2, only if the gate says proceed)**: strictly the evidenced candidate (A fold-aware skip / B single-build snapshots / C payload-serialize cache), behind a toggle whose OFF path is byte-identical.
- **Explicitly NOT in scope**: any change to the checkpoint-commit / coalescing / fence / queue path (already characterized by specs 110/115); heavy-state serialization (rides to flush); Immediate-mode behavior change.

## User Scenarios & Testing *(mandatory)*

### User Story 1 — The per-hop envelope-build CPU of a drain is visible and attributable (Priority: P1)

As an engine-performance maintainer, I can run one diagnostic that reports, per cadence mode, the per-hop wall and allocations spent building checkpoint envelopes upstream of the committer, so I can decide whether that construction is worth optimizing — and keep the number honest over time.

**Acceptance**: `EnvelopeBuildStageDiagnostics` emits per-hop µs for `BuildProjectionAsync` (and its `FindAsync` store-read share), the inter-commit envelope-build upper bound, bytes/hop, and the envelope-build CPU share vs the pinned in-memory hot-loop denominator, for both Immediate and Coalesced(cap256), with `RuntimeSchedulerDispatchDiagnostics` hop/fused-span counts as the deterministic denominators.

## Success Criteria

- **SC-001**: The instrument runs green (workflow completes) under both cadence modes and prints the measured table.
- **SC-002**: The kill/proceed/re-aim verdict is recorded in research.md against the pre-pinned thresholds.
- **SC-003**: If KILL, the instrument lands as a permanent tripwire and the program-goals doc carries a one-line finding. If PROCEED, Stage 2 is scoped to the evidenced candidate only.
