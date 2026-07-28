# Implementation Plan: ReplaySafe hop fusion (ADR 0047 D1+D2)

**Spec**: [spec.md](./spec.md) · **Grounding**: [research.md](./research.md) · **Tasks**: [tasks.md](./tasks.md)

## Guiding constraints

- **Byte-identical is achieved by reuse, not re-implementation.** The fused pass calls the exact stage
  cores the discrete handlers call. The only thing fusion removes is the enqueue+redispatch of intermediate
  work items. Any code that recomputes a commit rather than reusing the stage core is a bug.
- **Fusion never changes durable truth or its redrive** — it changes dispatch locality inside a burst. The
  crash-recovery ladder (§5 of research) is untouched.
- **Fail-safe direction is durability.** Every ambiguity resolves toward the discrete path.
- **DRY / no back-compat** (pre-release): extract shared stage cores; no shims.

## Commit-by-commit increments (the orchestrator reviews each)

### Increment A — stage-core extraction refactor (behavior-preserving, no fusion yet)
Extract the schedule / start / invoke / parent-completion **stage cores** into services (or `internal`
methods on a shared collaborator) callable by both the per-kind handler and the future driver. Signature:
each core takes the resolved `RuntimeSchedulerWorkItem` + payload + `WorkflowExecutable` + `ExecutableNode`
+ current `ActivityExecutionState` and returns the produced `RuntimeCheckpointCommit?` + the next-stage
work item (or a terminal marker). The existing handlers become thin adapters that call the core then
commit/enqueue exactly as today. **Gate:** the full handler test suites
(`Elsa.Workflows.Runtime.Tests`, `Elsa.Activities.Runtime.Tests`) pass unchanged — this proves the
extraction is behavior-preserving. No new behavior, no toggle read yet. Commit alone.

### Increment B — toggle + dispatch counter (evidence + kill switch, still no fusion behavior)
- Add `RuntimeReplaySafeFusionOptions { bool Enabled = true }` (mirror `RuntimeInProcessHopFastPathOptions`)
  and register it default-ON in `RuntimeCoreServiceCollectionExtensions`; thread it into the seam ctor as
  an optional arg defaulting to `new()`.
- Add the per-run **dispatches** counter to `DurableRoundTripDiagnostics` (one increment per
  `WorkflowSchedulerDrainer.DispatchAsync`), exposed as `DispatchesPerRun`. This is immediately useful:
  it measures the *current* ~7-dispatch path and becomes the deterministic A/B lever once fusion lands.
**Gate:** build + `Elsa.Workflows.Runtime.Tests`. Commit.

### Increment C — D1 fused schedule→start→invoke driver
Add the fused-span driver invoked from the `ScheduleActivity` handler's terminal continuation point (the
least-invasive equivalent seam, justified in research §7), gated by toggle + active coalescing session +
`ExecutableNode.ActivityContract` ReplaySafe. It runs the extracted start + invoke cores inline, staging
their commits through the same committer (buffered into the same segment), and inspects each produced
continuation to decide fuse-vs-fallback per research §4. On any fallback it stops and lets the last
produced work item flow to the overlay queue unchanged. Ship the byte-identical guardrail for shapes
(a) straight-line, (d) suspend mid-span, (e) External — plus the D1 crash-convergence kill points.
**Gate:** guardrails green + the eight QA suites + full solution build. Commit.

### Increment D — D2 inline single-predecessor completion
Extend the driver to the completion cascade: when the just-completed activity's parent is a `ReplaySafe`
routing composite and the successor edge is single-predecessor (via the spec-119 memo inbound index), run
the parent-completion + continuation-scheduling + checkpoint cores inline and emit the successor
`ScheduleActivity` directly (which re-enters the D1 driver if the successor is ReplaySafe). Fan-in/join and
External parents fall back. Add byte-identical shapes (b) multi-outcome branch and (c) join-falls-back, and
the D2 crash-convergence kill points. **Gate:** all guardrails + eight QA suites + full build. Commit.

### Increment E — amendments + A/B benchmark + final QA
- Spec-095 FR amendment (see tasks); ADR 0047 Follow-up marked implemented → spec 123; ADR 0031
  cross-reference note.
- Benchmark A/B methods for (no fusion) / (D1) / (D1+D2), reporting dispatches/commits/reads per run.
- Full QA pass; capture counts + `uptime` load caveat.
Commit.

## Risk register

| Risk | Mitigation |
|---|---|
| Stage-core extraction subtly changes a commit | Increment A is gated purely on the existing suites passing unchanged; extraction lands before any fusion. |
| Fused commit ordering diverges from discrete | Reuse the same committer + same segment; the byte-identical harness (5 shapes) is the gate, not code review. |
| Crash mid-fused-span loses work | Durable queue holds the original `ScheduleActivity`; redrive ladder unchanged (research §5); crash-convergence suite with in-span kill points is the gate. |
| Mutating intrinsic fused by mistake | v1 excludes all intrinsics from fusion (research §4); guardrail includes a `Finish` shape as a fallback case. |
| Join edge fused by mistake | Single-predecessor probe (`GetInboundConnections.Count == 1`) required before D2 fuses; shape (c) proves fallback. |
| Wall-time noise under fleet load | Dispatch/commit/read counters are the primary deterministic evidence (FR-008); walls run-order-swapped + `uptime` reported. |

## Verification (never subsets)

Full projects: `Elsa.Workflows.Runtime.Tests`, `Elsa.Activities.Runtime.Tests`,
`Elsa.Activities.Flowchart.Tests`, `Elsa.Activities.Sequence.Tests`, `Elsa.Activities.ControlFlow.Tests`,
`Elsa.Activities.Bpmn.Tests`, `Elsa.Persistence.Groundwork.Tests`, `Elsa.Workflows.Publishing.Api.Tests`.
Full build: `dotnet build Elsa.Server.slnx` (target-typed `new(` call sites have broken CI twice — the full
build is the reliable check for the constructor changes in increments A–C).
