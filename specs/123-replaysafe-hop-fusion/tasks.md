# Tasks: ReplaySafe hop fusion (ADR 0047 D1+D2)

Dependency-ordered. Each increment is one review-able commit. File paths are absolute-from-repo-root.
Locate classes by name where the `Core/Services` vs `Services` folder split varies.

> **Baseline (verify before starting, confirmed green 2026-07-22):**
> `Elsa.Workflows.Runtime.Tests` 1378 passed · `Elsa.Activities.Runtime.Tests` 196 passed.
>
> **Read [research.md §8](./research.md) first — it de-risks this plan substantially:**
> - **D1 needs NO invoke-handler surgery** (§8.1): only the schedule + start handlers get a fused-mode
>   commit builder; the driver dispatches the existing invoke handler inline. This supersedes task A3 below
>   (invoke-core extraction) for D1 — A3 is deferred to the D2 seam only.
> - **Byte-identity compares checkpoint+state, not the outbox** (§8.2) — the spec-109 fingerprint is
>   already correct; do not add the outbox.
> - **Inline reads already overlay through the coalescing session** (§8.3) — no new read plumbing.
> - **Two harness prerequisites gate the guardrail** (§8.4) — now **Increment A0**, the true first step.

## Increment A0 — test-harness prerequisites (MUST precede the guardrail; without these C's guardrail is vacuous) — DONE 2026-07-22

Landed: `WithCoalescing(maxSegmentCheckpoints?)` builder + `NewReplaySafeProbeNode` + `ReplaySafeProbeActivity`
(`[ActivitySideEffectProfile(ReplaySafe)]`). Gate green: Activities.Runtime.Tests **198** (was 196, +2 smoke),
Workflows.Runtime.Tests **1378**, Flowchart.Tests **74**. Smoke test `ReplaySafeFusionHarnessPrerequisiteTests`
proves coalesced folds (coalesced commits < immediate) and ReplaySafe probe resolves ReplaySafe / Probe stays External.

- [x] **A0.1** Add a **coalesced-mode path to `WorkflowExecutionHarness`**
  (`tests/Elsa/Activities/Testing/WorkflowExecutionHarness.cs`): a `WithCoalescing(maxSegmentCheckpoints)`
  builder that registers `IRuntimeCoalescingDrainScopeFactory`, `CoalescingRuntimeCheckpointPersistenceOptions`,
  the coalescing store decorators (`CoalescingRuntimeStateStores` etc.), and a cadence resolver, so the
  same workflow can be driven coalesced. Model the registration on how the coalescing feature wires them in
  `RuntimeCoreServiceCollectionExtensions` / the coalescing tests (`RuntimeCheckpointCoalescingTests`).
- [ ] **A0.2** Add a **`ReplaySafe` probe activity** (e.g. `ReplaySafeProbeActivity` marked
  `[ActivitySideEffectProfile(SideEffectProfile.ReplaySafe)]`) + a `NewProbeNode(..., replaySafe: true)`
  overload (or a `NewReplaySafeProbeNode`) so a fusion shape's leaves pin a ReplaySafe contract. Do NOT
  change `ProbeActivity` itself (would perturb the External guardrail-3 suites).
- [ ] **A0.3 GATE**: build tests + a throwaway assertion that a coalesced ReplaySafe run completes and its
  commits fold into a segment (proves the harness path works before any fusion code exists). Commit:
  `test(harness): coalesced-mode harness path + ReplaySafe probe activity (spec 123 prep)`.

## Increment A — stage-core extraction refactor (behavior-preserving) — DONE 2026-07-22 (reduced scope per §8.1)

Landed the schedule + start commit-builder cores only (A3 invoke / A4 parent-completion deferred to D2 per §8.1):
`WorkflowScheduleActivitySchedulerWorkHandler.BuildScheduledCommitAsync` → `ScheduledCommitCore(commit-no-intent,
StartWorkItem, OccurredAt)` and `WorkflowStartActivitySchedulerWorkHandler.BuildStartedCommitAsync` →
`StartedCommitCore(commit-no-intent, InvokeWorkItem, OccurredAt)`. Each discrete `NewCommitAsync` is now a thin adapter
that re-attaches the continuation intent via `with { PostCommitIntents = [...] }`, reproducing today's commit
byte-for-byte. Gate green, unchanged: Workflows.Runtime.Tests **1378**, Activities.Runtime.Tests **198**.

- [x] **A1** Extract the **schedule stage core** from `WorkflowScheduleActivitySchedulerWorkHandler.ExecuteAsync`
  (`src/Elsa/Workflows/Runtime/Services/`) into a reusable core that returns
  `(RuntimeCheckpointCommit? commit, RuntimeSchedulerWorkItem? nextWorkItem)` — the `ActivityScheduled`
  commit + the `StartActivity` item from `NewStartActivityWorkItem`. Handler becomes a thin adapter.
- [ ] **A2** Extract the **start stage core** from `WorkflowStartActivitySchedulerWorkHandler.ExecuteAsync`
  (input-snapshot materialization + `Running` transition + `ActivityStarted` commit + `InvokeActivity`
  item). Keep the intrinsic branch inside the core but flagged so the driver can detect a mutating
  intrinsic and refuse to fuse it.
- [ ] **A3** Extract the **invoke stage core** from `WorkflowInvokeActivitySchedulerWorkHandler`
  (`src/Elsa/Activities/Runtime/Services/`). The core returns a discriminated result: `Completed(commit,
  parentCompletionWorkItem)` | `ChildScheduling(commit, childItems)` | `Suspended(commit, ...)` |
  `Faulted(commit)`. The four existing commit arms already exist — the extraction wraps their outcomes in
  the discriminated result rather than committing inline for the fused caller. **The per-kind handler keeps
  committing inline exactly as today.**
- [ ] **A4** Extract the **parent-completion stage core** from
  `WorkflowParentActivityCompletionSchedulerWorkHandler` (evaluation + routing via the spec-119 memo +
  continuation scheduling + checkpoint), returning `(commit, successorScheduleItems)`.
- [ ] **A5 GATE**: run FULL `Elsa.Workflows.Runtime.Tests` + `Elsa.Activities.Runtime.Tests` — must pass
  unchanged (proves behavior-preserving). Commit: `refactor(runtime): extract scheduler stage cores for reuse`.

## Increment B — toggle + dispatch counter — DONE 2026-07-22

Landed `RuntimeReplaySafeFusionOptions { Enabled = true }` (default-ON, registered in RuntimeCore) and a singleton
`RuntimeSchedulerDispatchDiagnostics { Dispatches, FusedSpans }` incremented once per `WorkflowSchedulerDrainer.DispatchAsync`
(new optional last ctor arg, wired through the RuntimeCore drainer factory) — exposed as `DispatchesPerRun`/`FusedSpansPerRun`
on `DurableRoundTripDiagnostics`. The single diagnostics singleton doubles as the fusion-engagement counter the C guardrail reads.
Gate green: build + Workflows.Runtime.Tests **1378**.

- [x] **B1** Add `src/Elsa/Workflows/Runtime/Core/Models/RuntimeReplaySafeFusionOptions.cs`
  (`{ bool Enabled = true }`), mirroring `RuntimeInProcessHopFastPathOptions` doc/shape.
- [ ] **B2** Register it default-ON in `RuntimeCoreServiceCollectionExtensions`; thread an optional
  `RuntimeReplaySafeFusionOptions?` ctor arg (default `new()`) into the fusion seam collaborator.
- [ ] **B3** Add a per-run **dispatches** counter to
  `benchmarks/Elsa/Workflows/Runtime/Benchmarks/DurableRoundTripDiagnostics.cs`; increment once per
  `WorkflowSchedulerDrainer.DispatchAsync`; expose `DispatchesPerRun` next to the existing commits/reads
  metrics. (The drainer already threads `IWorkflowEngineTracer`; add the counter through the same
  diagnostics seam the benchmark uses, not a static global unless that is the existing pattern.)
- [ ] **B4 GATE**: build + FULL `Elsa.Workflows.Runtime.Tests`. Commit:
  `feat(runtime): ReplaySafe fusion toggle (default on) + per-run dispatch counter`.

## Increment C — D1 fused schedule→start→invoke — DONE 2026-07-22

Landed `ReplaySafeFusionDriver` (RuntimeCore), invoked from `WorkflowScheduleActivitySchedulerWorkHandler`'s fresh-schedule
branch when `ShouldFuse` holds (toggle on + active coalescing session applies + node is a ReplaySafe non-intrinsic
contract). The schedule handler commits the intent-free ActivityScheduled via `BuildScheduledCommitAsync`, then
`driver.ContinueFusedSpanAsync` runs `WorkflowStartActivitySchedulerWorkHandler.ExecuteFusedStartAsync` inline (commits
intent-free ActivityStarted) and dispatches the retained InvokeActivity through the unchanged invoke handler inline
(resolved custom-before-fallback, mirroring the drainer). Fallback: if the start stage declines (not a fresh Scheduled
ReplaySafe leaf) the StartActivity item is enqueued to the overlay — never dropped, never enqueued on the fused path.
Registered `WorkflowStartActivitySchedulerWorkHandler` (concrete) + `ReplaySafeFusionDriver` (scoped) in RuntimeCore.

**Finding:** fusion also engages on the ReplaySafe **Flowchart composite** itself (FR-001 gates on the contract, not
leaf-ness) — its schedule→start→invoke fuse and the child-scheduling continuation flows via the overlay outbox (research
§4 composite-children case). Byte-identical, confirmed by the guardrail.

**Gate (all green):** guardrail `ReplaySafeFusionGuardrailTests` (4 tests: straight-line + suspend + External
byte-identical ON vs OFF, determinism self-check; fusion proven to engage via `RuntimeSchedulerDispatchDiagnostics`) —
straight-line ON FusedSpans≥5 & dispatches ON<OFF, External leaves never fuse, OFF FusedSpans=0.
`ReplaySafeFusionCrashConvergenceTests` (Groundwork, 3 kill points inside a fused span: commit #2/#3/#4 →
OperationCanceledException; durable redrive source survives + gen-2 sweep converges to the crash-free terminal).
Suites: Workflows.Runtime **1378**, Activities.Runtime **202**, Flowchart **74**, Groundwork **657**, Sequence **16**,
ControlFlow **196**, Bpmn **107**, Publishing.Api **401**; full `dotnet build Elsa.Server.slnx` **0 errors**.
(One Flowchart run flaked a single test under load avg ~96; passed 3×/3 once load dropped — suspected load flake, not fusion.)

- [x] **C1** Add the fused-span driver, invoked from the schedule handler's terminal continuation point,
  gated by: `RuntimeReplaySafeFusionOptions.Enabled` AND an active coalescing session
  (`IRuntimeCoalescingSessionAccessor.Current?.AppliesTo(executionId)`) AND
  `executableNode.ActivityContract?.SideEffectProfile == ReplaySafe` AND not a mutating intrinsic.
- [ ] **C2** Driver loop (per research §8.1 — the safer, no-invoke-surgery shape): after committing the
  schedule core's *fused-mode* `ActivityScheduled` (no `StartActivity` intent), run the start core in
  fused mode inline → commit `ActivityStarted` (no `InvokeActivity` intent), then dispatch the retained
  `InvokeActivity` work item through the **existing unchanged** `WorkflowInvokeActivitySchedulerWorkHandler`
  inline. Stage every commit through the same `RuntimeCheckpointCommitter` (buffers into the same segment).
  Only the schedule + start handlers gain a fused-mode commit builder (omit continuation intent, return
  the next work item); the invoke handler is untouched for D1.
- [ ] **C3** Fallback wiring: on Suspended / Faulted / ChildScheduling / mutating-intrinsic / non-ReplaySafe
  results, STOP fusing and let the last produced work item flow to the overlay queue via the normal
  enqueue path (do not enqueue anything the discrete path would not have). Never enqueue the intermediate
  `StartActivity`/`InvokeActivity` items on the fused path.
- [ ] **C4** `ReplaySafeFusionGuardrailTests` (Elsa.Activities.Runtime.Tests): shapes (a) straight-line hot
  loop, (d) suspend mid-span, (e) External — ENABLED vs DISABLED byte-identical; determinism self-check
  (two OFF runs); assert fusion engaged in the ON run via the dispatch counter.
- [ ] **C5** `ReplaySafeFusionCrashConvergenceTests` (Elsa.Workflows.Runtime.Tests): D1 kill points —
  after schedule, after start, mid-body, after body pre-flush — converge to the discrete terminal on
  redrive.
- [ ] **C6 GATE**: guardrails + all eight QA suites + full solution build. Commit:
  `feat(runtime): D1 fused schedule->start->invoke pass for ReplaySafe activities`.

## Increment D — D2 inline single-predecessor completion

> **STATUS 2026-07-22 (D2 session): DONE — the D2 driver landed, resolving both recorded blockers. Gate green.**
>
> **What shipped (commit `spec 123 D2`):**
> - **Blocker 1 (cross-assembly probe) resolved as designed:** `IReplaySafeSuccessorRoutingProbe` in Runtime.Core;
>   `FlowchartReplaySafeSuccessorRoutingProbe` reads the successor's inbound count off the spec-119
>   `GetOrAddRoutingStructure(FlowchartGraph.From)` memo; `SequenceReplaySafeSuccessorRoutingProbe` answers from the
>   `SequenceNavigator` memo (first child 0, others 1). Registered from `ActivitiesFlowchartFeature` /
>   `ActivitiesSequenceFeature`; injected optionally into `ReplaySafeFusionDriver`; unanswered probe ⇒ fallback.
>   Per-successor parent resolution: schedule payload provenance `ParentActivityExecutionId` → parent activity state →
>   parent `ExecutableNode`; `inbound <= 1` fuses (a composite's entry node has 0), `> 1` is the join fallback.
> - **Blocker 2 (pump vs session invariants) resolved with one deviation from the recorded sketch:** the overlay
>   queue's strict-FIFO claim contract keeps the drain loop's head claim (the fused span's originating item) live for
>   the whole span, so the pump **cannot claim** overlay items at all (`ClaimAsync` only serves the head). Instead the
>   session grew a single-writer peek/consume pair (`PeekNextPumpableOverlayItemAsync` — FIFO-first item with no live
>   claim, via a new insertion-order `InMemoryWorkflowSchedulerWorkQueue.PeekFirstAvailableAsync` —
>   plus `ConsumePumpedOverlayItemAsync`). Consumption is FIFO-after-the-claimed-head, so
>   `AdvanceInnerQueueAsync`'s consumed-seeded-prefix accounting still holds at every flush (in-flight claims are
>   excluded before counting); an item mid-inline-dispatch at a flush counts as remaining continuation — the durable
>   crash backstop — and a redelivery resolves through the existing idempotency ladder. The pump mirrors the drain
>   loop exactly: per-cycle `IRuntimePostCommitOutboxProcessor.ProcessAsync` (EnqueueSchedulerWork), pause-gate and
>   W5 terminal-status parity, custom-before-fallback handler resolution, session-deactivation stop, and re-entrancy
>   guard (nested fused spans are D1-only; the top pump owns the loop iteratively).
> - **A byte-identity fix the pump surfaced:** the session's outbox delivery/claim ordering tie-broke same-timestamp
>   items by outbox-item id (arbitrary hash order). Same-tick bursts — the norm inside a coalesced segment, and every
>   run under the guardrail's fixed clock — could interleave sibling branches differently fused vs discrete. The
>   tie-break is now causal insertion order (stable sort over `_outboxOrder`), byte-identical whenever timestamps are
>   strictly monotonic.
> - **Gate (all green):** guardrails 9 (straight-line + suspend + External + determinism + D1-only + branch + join;
>   engagement proven via `FusedSpans`/`InlineCascadeDispatches`/`CascadeJoinFallbacks`); Groundwork crash suite 8
>   kill points on a 5-leaf chain (inside D1 spans, the inline completion pass, and the D2→D1 recursion boundary);
>   A/B hot-loop×10 dispatches **58 (none) → 36 (D1) → 5 (D1+D2)**, inline-cascade 31, commits unchanged (already
>   folded to 1 by coalescing). Suites: Workflows.Runtime **1381**, Activities.Runtime **205**, Flowchart **74**,
>   Sequence **16**, ControlFlow **196**, Bpmn **143**, Groundwork **662**, Publishing.Api **401**; full
>   `dotnet build Elsa.Server.slnx` **0 errors**.
>
> The original deferral record (D-part-1 seam + the two blockers) is retained below for provenance.
>
> **STATUS 2026-07-22 (earlier finishing session): D-part-1 DONE; D-part-2 (D2 driver) DEFERRED at a genuine correctness/scope boundary — see below.**
>
> **D-part-1 (DONE, committed `8d6941533`):** behavior-preserving completion-seam extraction.
> `WorkflowInvokeActivitySchedulerWorkHandler.CommitCompletedActivityAsync` split into a
> `BuildCompletedCommitAsync` core (intent-free `ActivityCompleted` commit + derived `CompleteActivity`
> work item + `occurredAt`; any staged workflow-dispatch start intent stays on the commit) plus a thin
> adapter that re-attaches the `CompleteActivity` continuation intent AHEAD of the workflow-dispatch start
> intent, reproducing today's commit byte-for-byte. Mirrors the schedule/start `Build*CommitAsync` pattern.
> Gate green, unchanged: `Elsa.Activities.Runtime.Tests` **202**, `Elsa.Workflows.Runtime.Tests` **1378**.
>
> **§3 ReplaySafe-contract verification (was an open research question):** BOTH `Flowchart`
> (`src/Elsa/Activities/Flowchart/Activities/Flowchart.cs:31`) and `Sequence`
> (`src/Elsa/Activities/Sequence/Activities/Sequence.cs:18`) carry
> `[ActivitySideEffectProfile(SideEffectProfile.ReplaySafe)]`. No force-marking needed; the D2 parent gate
> (`parent.ActivityContract.SideEffectProfile == ReplaySafe`) is sound for both composites.
>
> **D-part-2 (D2 driver) DEFERRED — the two concrete blockers discovered while designing it (recorded so the
> next session starts ahead, same way research §8 recorded C's findings):**
>
> 1. **Cross-assembly routing-probe boundary.** The single-predecessor check needs
>    `FlowchartGraph.GetInboundConnections(successorNodeId).Count == 1` (spec-119 memo), but `FlowchartGraph`
>    lives in `Elsa.Activities.Flowchart`, which depends on `Elsa.Workflows.Runtime.Core` (where the fusion
>    driver + `WorkflowScheduleActivitySchedulerWorkHandler` live) — the driver cannot reference it (circular).
>    Routing is emitted by the Flowchart activity itself via `OnChildCompletedAsync` (structural callback), not
>    a Runtime.Core abstraction, and `Elsa.Activities.Runtime` does NOT reference Flowchart either. **Clean fix
>    for next session:** define `IReplaySafeSuccessorRoutingProbe` (or similar) in Runtime.Core, implement it in
>    the Flowchart assembly (`FlowchartGraph.From(composite).GetInboundConnections(successor).Count`) + a
>    Sequence impl (intrinsically single-successor), register from `ActivitiesFlowchartFeature`, inject
>    `optional` into `ReplaySafeFusionDriver`; when absent → cannot prove single-predecessor → fall back. Also
>    needs per-successor parent-composite resolution (the successor `ScheduleActivity`'s
>    `SchedulingProvenance.ParentActivityExecutionId` → parent activity state → `ExecutableNodeId` → composite
>    node).
> 2. **Inline completion-cascade pump vs coalescing-session invariants.** The completion cascade
>    (`CompleteActivity`→`ParentCompletionEvaluation`→`ContinuationScheduling`→`Checkpoint`→successor
>    `ScheduleActivity`) is a 3-handler, ~2900-line machine (parent reconstruction, `OnChildCompletedAsync`,
>    structural continuations, subtree cancellation, fault absorption, checkpoint participants) that advances by
>    **enqueuing to the coalescing overlay queue + delivering post-commit intents through the outbox**. Fusing it
>    inline means an **iterative inline pump** in the driver (re-entrancy-guarded so a re-entered
>    `ContinueFusedSpanAsync` does D1-only and the top pump owns the loop; iterative to avoid O(chain-length)
>    recursion / stack overflow on long hot loops): deliver this execution's outbox inline
>    (`IRuntimePostCommitOutboxProcessor.ProcessAsync`), then claim+dispatch overlay items inline (via
>    `handler.HandleAsync` directly, bypassing `WorkflowSchedulerDrainer.DispatchAsync`'s `RecordDispatch` — the
>    hop-count win), STOPPING (leaving the item in the overlay for the outer loop = fallback) at a
>    `ScheduleActivity` whose successor is not single-predecessor+ReplaySafe. **De-risk that makes this tractable
>    and was validated on the D1 path:** the guardrail commit fingerprint sorts by `CommitId`
>    (`ReplaySafeFusionGuardrailTests.FingerprintCommits`, order-INDEPENDENT), so byte-identity is robust to the
>    pump's interleaving as long as the commit SET is identical (it is — same handlers, deterministic ids); and
>    only the original `ScheduleActivity` is ever durably queued (all cascade items are overlay/inline, discarded
>    on crash, folded at flush), so crash-convergence holds exactly as D1 (research §5). **Residual risk to
>    validate empirically (why it was not landed blind this session):** the pump interleaves outbox delivery +
>    overlay claim/dispatch WHILE the outer drainer holds the original item's claim — the
>    `RuntimeCoalescingSession` seeded-item / in-flight-claim (`AdvanceInnerQueueAsync(consumeInFlightClaims)`) /
>    outbox-reconciliation invariants must be proven undisturbed against the crash suite + the 8-project battery.
>    Design reasoning says they hold (pump only touches transient overlay/outbox with the exact same code the
>    outer loop runs); the strict gates must confirm it. Estimated as one focused session on top of D-part-1.
>
> The tasks below (D1 probe, D2 driver, D3/D4 guardrails+crash) remain the plan for that session.

- [x] **D1** Add the single-predecessor probe on the routing memo:
  `ExecutableNode.GetOrAddRoutingStructure<FlowchartGraph>(FlowchartGraph.From).GetInboundConnections(successorNodeId).Count == 1`
  (Sequence is intrinsically single-successor). No graph walk, no second cache.
  *(Landed as `IReplaySafeSuccessorRoutingProbe` + Flowchart/Sequence impls — see the D2-session status above.)*
- [x] **D2** Extend the driver: inline completion pump on the coalescing overlay (the recorded pump design, not the
  original call-the-cores sketch): the same handlers run inline in drain-loop FIFO order; a fusable successor
  `ScheduleActivity` re-enters the D1 driver (re-entrancy-guarded). Fan-in/join (`Count > 1`), child-fault
  evaluations, External parents, and the workflow tail fall back to the discrete cascade.
- [x] **D3** Extend `ReplaySafeFusionGuardrailTests`: shapes (b) multi-outcome branch (single-predecessor
  successors) and (c) join-falls-back — ENABLED vs DISABLED byte-identical, join fallback proven via counters.
- [x] **D4** Extend `ReplaySafeFusionCrashConvergenceTests`: D2 kill points inside the inline cascade and across
  the D2→D1 recursion boundary (8 ordinals, 5-leaf chain).
- [x] **D5 GATE**: all guardrails + eight QA suites + full solution build — green (counts in the status block).

## Increment E — amendments + A/B benchmark + final QA

> **STATUS 2026-07-22 (finishing session): DONE, scoped to what shipped (D1). E1 + E2 doc amendments verified
> in place (landed with the spec-authoring/groundwork commits); ADR 0047 Follow-up finalized to D1-implemented /
> D2-driver-deferred; A/B benchmark added measuring none vs D1 (D1+D2 travels with the deferred D2 follow-up).**
>
> - **E1 (verified):** the FR-004 amendment is present in `specs/095-runtime-intent-handlers/spec.md` (the FR-004
>   home) and is correctly worded ("MAY", scoped to a live coalescing burst, byte-identical guarantee). It already
>   covers the D2 single-predecessor completion cascade as a capability, so no wording change was needed when D2 was
>   deferred. Mirroring into `specs/095-value-flow-redesign` was **not** done (deferred — FR-004 lives in
>   runtime-intent-handlers; a mirror adds no contract).
> - **E2 (verified + finalized):** the ADR 0031 cross-reference note is present (`docs/adr/0031-*.md` line 14,
>   pointing at ADR 0047 D1+D2 / spec 123). The ADR 0047 Follow-up (`docs/adr/0047-*.md`) was updated to state
>   D1 implemented + D2 seam extracted / D2 driver deferred with the honest reason, replacing the pre-implementation
>   plan wording.
> - **E3 (done, D1-only):** added `DurableRoundTripDiagnostics.ReplaySafeFusionDispatchAb` — ReplaySafe hot-loop×10
>   coalesced, fusion OFF vs ON, durable (Groundwork/Sqlite) + in-memory, run-order-swapped walls.
>   **Measured (single fleet-loaded run, counters are the evidence, walls indicative):**
>   durable OFF dispatches=58 fused=0 commits=1 reads=1; durable ON dispatches=36 fused=11 commits=1 reads=1;
>   in-mem OFF dispatches=58 fused=0 commits=2; in-mem ON dispatches=36 fused=11 commits=2. D1 fuses 11 spans
>   (10 leaves + the ReplaySafe Flowchart composite) and cuts dispatches 58→36; commits are already folded to 1
>   by coalescing (D1 does not change commit count — the completion cascade still dispatches per stage until D2
>   folds it, which is exactly why ON here is labelled D1-only). The **D1+D2** third column lands with the D2
>   follow-up. *(Landed by the D2 session: the benchmark now runs all three columns via the `FuseCompletionCascade`
>   dial — dispatches none 58 / D1 36 / D1+D2 **5**, inline-cascade 31 — with run-order-swapped walls.)*
> - **E4 (done):** full 8-project battery + `dotnet build Elsa.Server.slnx` — results recorded in the report.

- [ ] **E1** Spec-095 FR amendment: in `specs/095-runtime-intent-handlers/spec.md` add an amendment note to
  FR-004 (delivery ordering/queueing) — fusion collapses intermediate stage work items within a burst
  without changing the durable delivery contract for items that ARE enqueued; the durable wire contract and
  command kinds are unchanged. (If review prefers, mirror the note in `specs/095-value-flow-redesign`.)
- [ ] **E2** ADR 0047 Follow-up: mark D1/D2 implemented → spec 123 (this unit). ADR 0031: add the
  cross-reference note its follow-up promised ("see ADR 0047 for the ReplaySafe fusion that removes most of
  them") to the "5–7 work-item hops" arithmetic. *(E2 doc edits already landed by the spec-authoring
  session — verify they are present and consistent with the shipped code before final commit.)*
- [ ] **E3** Benchmark A/B methods: (no fusion `Enabled=false`) / (D1 only) / (D1+D2), reporting
  dispatches/commits/reads per run; run-order-swapped walls with `uptime` reported.
- [ ] **E4 GATE**: full QA — all eight suites + `dotnet build Elsa.Server.slnx`; record counts + load
  caveat. Commit: `docs+bench(runtime): ADR/FR amendments + ReplaySafe fusion A/B benchmark`.

## Notes for the implementer

- The stage cores must be the SAME code the discrete handlers run — see research §1. If you find yourself
  re-deriving a commit, stop; call the core.
- The coalescing overlay (`RuntimeCoalescingSession`) is where a burst's continuations live, NOT the
  spec-109 `RuntimeLiveDrainDeliveryScope` (which stands down under coalescing) — research §2.
- Every fallback must leave state the discrete path would produce; the byte-identical + crash suites are
  the gate, not inspection.
- Run FULL test projects, never `--filter` subsets, at every GATE (per project convention).
