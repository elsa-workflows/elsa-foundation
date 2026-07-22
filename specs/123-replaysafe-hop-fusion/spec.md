# Feature Specification: ReplaySafe hop fusion — fused schedule→start→invoke + inline single-predecessor completion (ADR 0047 D1+D2)

**Feature Branch**: `worktree-agent-a9886080ad6b0451b`
**Created**: 2026-07-22
**Program**: Runtime Execution Seam
**ADR**: [ADR 0047](../../docs/adr/0047-replaysafe-activities-execute-as-fused-hops-with-precomputed-routing.md) — **Decisions D1 + D2** (D3 shipped as [spec 119](../119-publish-time-routing-tables/spec.md); D4 stays deferred)
**Extends**: [ADR 0031](../../docs/adr/0031-runtime-burst-execution-sticky-single-writer-drain-with-in-process-fast-path.md) (burst/locality), [ADR 0032](../../docs/adr/0032-runtime-checkpoint-cadence-is-policy-driven-per-workflow.md) (R2 ReplaySafe claim relaxation, cadence), [ADR 0020](../../docs/adr/0020-runtime-checkpoint-commit-post-commit-work.md) (post-commit intents release only after durable commit)
**Input**: ADR 0047 D1+D2 and the three ratification resolutions; the discrete handler chain, coalescing overlay, and fusion seam analysis in [research.md](./research.md); the spec-119 routing memo + inbound index; the spec-109 byte-identical guardrail + toggle pattern.

> **Status 2026-07-22: D1 + D2 SHIPPED (increments A0→E green; D2 driver landed by the follow-up session).**
> Shipped: stage-core extraction (schedule/start/invoke-completion), `RuntimeReplaySafeFusionOptions` (default ON;
> `FuseCompletionCascade` dial for the D1-only A/B column), `RuntimeSchedulerDispatchDiagnostics` (dispatch,
> fused-span, inline-cascade, and join-fallback counters), the D1 `ReplaySafeFusionDriver`, and the **D2 inline
> completion pump**: `IReplaySafeSuccessorRoutingProbe` (Runtime.Core) with Flowchart/Sequence implementations
> resolves the cross-assembly single-predecessor check off the spec-119 memo, and the driver pumps the completion
> cascade inline in drain-loop FIFO order via the coalescing session's peek/consume pair — join edges (resolution #1),
> child-fault evaluations, non-ReplaySafe parents, and the workflow tail fall back to the discrete cascade. Guardrails
> cover straight-line, suspend, External, D1-only, multi-outcome branch, and fan-in join (join fallback proven via
> counters) byte-identical ON vs OFF; the Groundwork crash suite covers 8 kill points including the inline completion
> pass and the D2→D1 recursion boundary. A/B (hot-loop×10, coalesced): dispatches 58 (none) → 36 (D1) → 5 (D1+D2).

> **Ratification resolutions this unit implements verbatim** (ADR 0047 Follow-up, resolved 2026-07-20):
> 1. **D2 fuses single-predecessor edges only** in the first iteration; every fan-in/join edge falls back to the discrete cascade.
> 2. **D3 routing tables are recomputed on materialization** (already shipped by spec 119; this unit consumes the memo, adds no schema/hash change).
> 3. **Toggle default is ON at ship.**

## Why

After ADR 0031 (burst + in-process fast path) and ADR 0032 (coalesced cadence + R2 claim relaxation), the
spec-109 Step-0 profile shows **dispatch machinery is ~55% of a durable hop and ~78% of an in-memory hop**;
JSON round-trips are ~0.4–2% (already cut), scope construction is ~0 (ambient burst scope). The cost that
remains is **the number of hops**. Per ordinary `ReplaySafe` leaf edge the runtime still pays ~7
dispatches (schedule → start → invoke → completed → parent-evaluation → continuation-scheduling →
checkpoint), each paying the drain loop's claim/pause/dispatch/ack machinery. For a `ReplaySafe` activity
ADR 0032 R2 already ratified that re-running the whole span is harmless *by declaration* and its attempt
claim is already `Deferred`, and coalescing already folds every stage checkpoint into one segment flush —
so **after coalescing, the per-stage hops buy nothing for `ReplaySafe` activities except CPU** (a
mid-segment crash replays the whole segment regardless of hop count). This unit removes those hops.

## What this changes (and what it must not)

Fusion is a **dispatch-level locality optimization inside a live coalescing burst**, available only when
the target's pinned contract declares `SideEffectProfile.ReplaySafe` and the toggle is on. It reuses the
exact stage cores the discrete handlers run — it never re-implements a stage. A run with fusion disabled
(or with no burst) commits **byte-identical** durable state. For `External`/unmarked activities every hop,
flush, and attempt boundary is unchanged. No new command kinds; the durable wire contract is untouched.

---

## User Story 1 — D1: fused schedule→start→invoke pass (Priority: P1)

As a runtime operator running a hot loop of `ReplaySafe` leaf activities, I need the schedule, start, and
invoke stages to execute in one dispatch inside a burst, so the loop pays ~1 dispatch per leaf instead of
~3–7, with byte-identical durable history.

When the drainer dispatches a `ScheduleActivity` work item inside a live burst and the target node's
pinned contract is `ReplaySafe` (and the toggle is on), the fused pass runs the **schedule, start, and
invoke stage cores in one dispatch**: create the `Scheduled` state, transition to `Running` with the
materialized input snapshot, claim the attempt, run the body — staging **all** the same checkpoints
(`ActivityScheduled`, `ActivityStarted`, `ActivityAttemptClaimed`, completion) into the coalescing working
set **in the same order the discrete hops produce them**. The `StartActivity` / `InvokeActivity` work items
for a fused span are **never enqueued**.

**Independent test**: Drive a straight-line hot loop (N `ReplaySafe` leaves) to completion with fusion
ENABLED vs DISABLED over the same in-memory durable substrate; the ordered checkpoint commits and terminal
state are byte-for-byte identical (ownership-plane random ids masked), both complete, and the enabled run's
dispatch counter is materially lower (the fusion is proven to engage).

### Fallback exits (each falls back to the discrete chain, mid-span where needed)

The fused pass stops fusing and lets the already-produced continuation work item flow through the normal
overlay queue whenever it hits any of: activity suspends / creates a bookmark; contract is `External` or
unmarked; a mutating intrinsic (`Finish`/`Correlate`/`SetName`/`SetOutput` — excluded in v1); a fault; a
composite that scheduled children (fork); no live burst; toggle off. A span that exits mid-way MUST leave
state such that the discrete continuation from that point is exactly what the discrete path would have
produced (guaranteed by construction: the fused pass commits the same stage checkpoints via the same
committer, and only elides the enqueue+redispatch of intermediate items).

---

## User Story 2 — D2: inline single-predecessor completion propagation (Priority: P1)

As the same operator, I need a child's completion inside a burst, when its parent is a `ReplaySafe`
routing composite (`Flowchart`/`Sequence`) and the successor edge is **single-predecessor**, to run
parent-completion evaluation, outcome routing, continuation scheduling, and the completion checkpoint in
the same handler pass — emitting the successor's `ScheduleActivity` directly (which, if the successor is
also `ReplaySafe`, feeds straight into another D1 fused pass), so the 4-hop completion cascade collapses.

**Ratification resolution #1 binds:** fuse **single-predecessor edges only**. Any fan-in/join edge
(`GetInboundConnections(successor).Count > 1`) falls back to the discrete cascade
(`ActivityCompleted` → `ParentCompletionEvaluation` → `ContinuationScheduling` → `Checkpoint`).
Single-predecessor is detected via the spec-119 routing structure inbound index through
`ExecutableNode.GetOrAddRoutingStructure<T>` — **the fused pass pays no graph walk**. External parents keep
the discrete cascade. The single-writer drain (ADR 0031 res. #2) already serializes the cascade, so
inlining changes cost, not observable order of durable state.

**Independent test**: A multi-outcome straight-line branch (single-predecessor successors) run ENABLED vs
DISABLED commits byte-identical durable state; a shape with a join falls back and is likewise
byte-identical; both complete.

---

## User Story 3 — Byte-identical, crash-convergent, External-untouched (Priority: P1)

As a runtime maintainer, I need fusion to be provably a pure optimization: identical committed state,
convergent crash recovery from kill points *inside* fused spans, and zero change to `External`/unmarked
behavior, so it can never silently become a correctness dependency and rolls back with one config flip.

**Independent test**: the four guardrails below all pass.

---

## Functional Requirements

- **FR-001** (D1 fused pass): Inside a live coalescing burst, when a dispatched `ScheduleActivity` targets
  a node whose `ExecutableNode.ActivityContract.SideEffectProfile == ReplaySafe` and
  `RuntimeReplaySafeFusionOptions.Enabled` is true, the runtime MUST run the schedule, start, and invoke
  stage cores in one dispatch, staging `ActivityScheduled`, `ActivityStarted`, `ActivityAttemptClaimed`,
  and the completion checkpoint into the coalescing working set in the discrete order, and MUST NOT enqueue
  the span's `StartActivity` or `InvokeActivity` work items.

- **FR-002** (stage-core extraction, behavior-preserving): Each of the schedule / start / invoke stage
  cores MUST be callable by BOTH the existing per-kind handler (the durable path, byte-identical, its
  existing test suites unchanged) and the fused pass. Extraction is a refactor with no behavior change;
  the handler test suites pin this.

- **FR-003** (D1 fallback exits): The fused pass MUST fall back to the discrete chain — leaving
  discrete-equivalent state — at every exit in [research.md §4](./research.md): suspend/bookmark, fault,
  child-scheduling fork, mutating intrinsic, non-`ReplaySafe`/unmarked contract, no burst, toggle off.

- **FR-004** (D2 inline completion, single-predecessor only): When a child completes inside a burst and its
  parent is a `ReplaySafe` routing composite AND the successor edge is single-predecessor
  (`GetInboundConnections(successorNodeId).Count == 1` via the spec-119 memo), the runtime MUST run
  parent-completion evaluation, outcome routing, continuation scheduling, and the completion checkpoint in
  one pass and emit the successor `ScheduleActivity` directly. Any fan-in/join edge (`Count > 1`) or
  `External` parent MUST fall back to the discrete cascade.

- **FR-005** (routing via the memo, no walk): Both D2's single-predecessor detection and its outcome
  routing MUST route through `ExecutableNode.GetOrAddRoutingStructure<T>(...From)` (spec 119). The fused
  pass MUST NOT re-walk the graph or add a second cache.

- **FR-006** (crash semantics unchanged): The durable queue MUST hold only the original `ScheduleActivity`
  item for a fused span. Redelivery after a crash MUST resolve through the existing idempotency ladder
  (queue enqueue-by-identity, status-based handler no-ops, fold-forward claims) exactly as a mid-segment
  coalescing crash does today. No new crash-recovery mechanism is introduced.

- **FR-007** (byte-identical toggle): `RuntimeReplaySafeFusionOptions { Enabled = true }` MUST be
  registered by default (resolution #3). Registering `{ Enabled = false }` before the runtime feature
  MUST force the discrete chain everywhere and commit byte-identical durable state (the A/B toggle + host
  kill switch), following the `RuntimeInProcessHopFastPathOptions` pattern.

- **FR-008** (dispatch counter): `DurableRoundTripDiagnostics` MUST gain a per-run **dispatches** counter
  (one increment per `WorkflowSchedulerDrainer.DispatchAsync`) exposed as `DispatchesPerRun`, so the
  hop-count claim (~7 → ~1–2 per leaf edge) is deterministic evidence independent of wall time.

- **FR-009** (External untouched): No change to `External`/unmarked activity behavior — the spec-095
  attempt/poison/attribution suites MUST be bit-for-bit unchanged. `External` ⇒ never fused; the fail-safe
  default keeps the asymmetry pointing at durability.

- **FR-010** (amendments travel with the unit): This unit MUST ship (a) the spec-095 FR amendment noting
  intermediate stage work items are collapsed within a burst without changing the durable delivery contract
  for items that ARE enqueued; (b) the ADR 0047 Follow-up marked D1/D2 implemented → spec 123; (c) the
  ADR 0031 cross-reference note ("see ADR 0047 for the ReplaySafe fusion that removes most of them").

## Guardrails (non-negotiable — all four from ADR 0047)

1. **Byte-identical durable state** (`ReplaySafeFusionGuardrailTests`, reusing the spec-109 harness
   pattern): fused-ON vs fused-OFF end-to-end runs commit byte-identical checkpoint/state documents, for
   **all five shapes** — (a) straight-line hot loop, (b) multi-outcome branch, (c) a shape with a join
   (must fall back), (d) a suspend/resume shape (mid-span exit), (e) an `External`-activity shape. A
   determinism self-check (two OFF runs) proves the fingerprint is a real convergence claim; fusion is
   confirmed to engage in the ON run via the dispatch counter.

2. **Crash convergence** (`ReplaySafeFusionCrashConvergenceTests`, extending the coalescing suites): kill
   points **inside** fused spans — after stage 1 (schedule), after stage 2 (start), mid-body, after body
   pre-flush — for both D1 and D2 shapes, plus the `External`-parent and suspend fallback exits, each
   converges to the discrete-path terminal state on redrive.

3. **External untouched**: the spec-095 attempt/poison/attribution suites prove `External`/unmarked
   behavior is bit-for-bit unchanged.

4. **A/B evidence at the gate**: the benchmark reports (no fusion) / (D1 only) / (D1+D2) —
   dispatches/run, commits/run, executable-reads/run — with dispatch/commit/read counters as the
   deterministic evidence and walls (run-order-swapped, `uptime` reported) as indicative. Expected:
   ~7 dispatches per leaf edge → ~1–2; in-memory hot-loop CPU 2–3×.

## Success Criteria

- **SC-001**: Fusion enabled + `ReplaySafe` leaf in a burst ⇒ one dispatch runs schedule+start+invoke; no
  `StartActivity`/`InvokeActivity` items enqueued.
- **SC-002**: Fusion enabled + single-predecessor `ReplaySafe`-parent completion ⇒ one pass runs
  evaluation+routing+scheduling+checkpoint; join edges fall back.
- **SC-003**: Fusion disabled / no burst / `External` / mutating intrinsic / suspend / fork ⇒ discrete
  chain, byte-identical.
- **SC-004**: All five byte-identical shapes converge ON vs OFF; determinism self-check passes; fusion
  proven to engage.
- **SC-005**: Crash-convergence kill points inside fused spans (D1 + D2) converge to the discrete terminal.
- **SC-006**: `External` suites bit-for-bit unchanged.
- **SC-007**: A/B table shows the dispatch-count collapse deterministically.
- **SC-008**: Full `Elsa.Workflows.Runtime.Tests`, `Elsa.Activities.Runtime.Tests`,
  `Elsa.Activities.Flowchart.Tests`, `Elsa.Activities.Sequence.Tests`, `Elsa.Activities.ControlFlow.Tests`,
  `Elsa.Activities.Bpmn.Tests`, `Elsa.Persistence.Groundwork.Tests`,
  `Elsa.Workflows.Publishing.Api.Tests` pass; full `dotnet build Elsa.Server.slnx` green.

## Out of scope

- **D3** (publish-time routing tables) — shipped by [spec 119](../119-publish-time-routing-tables/spec.md);
  this unit consumes the memo.
- **D4** (completion batching at quiescence) — deferred by the ADR; subsumed by D1+D2 for the burst case.
- **Fan-in/join edge fusion** (resolution #1) — discrete cascade; re-open as its own unit only with
  evidence.
- **Mutating intrinsics** (`Finish`/`Correlate`/`SetName`/`SetOutput`) — first iteration excludes them
  (ADR 0047 D1 "What is NOT fused"). Non-mutating intrinsics may be revisited with a byte-identical proof.
- **BPMN composite fusion beyond single-predecessor Flowchart/Sequence** — the ADR 0032 classification
  pins Flowchart/Sequence as the v1 ReplaySafe routing composites.

## Changed surfaces (for the code increment — see [tasks.md](./tasks.md))

- New: `RuntimeReplaySafeFusionOptions`; the fused-span driver (reuses extracted stage cores); the
  single-predecessor probe on the routing memo.
- Modified: `WorkflowScheduleActivitySchedulerWorkHandler` / `WorkflowStartActivitySchedulerWorkHandler` /
  `WorkflowInvokeActivitySchedulerWorkHandler` / `WorkflowParentActivityCompletionSchedulerWorkHandler`
  (stage-core extraction, behavior-preserving); `WorkflowSchedulerDrainer.DispatchAsync` (dispatch
  counter); `DurableRoundTripDiagnostics` (dispatches/run); `RuntimeCoreServiceCollectionExtensions`
  (register options); benchmark A/B methods.
- Docs: ADR 0047 Follow-up; ADR 0031 cross-reference note; spec-095 FR amendment.
- Tests: `ReplaySafeFusionGuardrailTests`, `ReplaySafeFusionCrashConvergenceTests`, seam tests.
