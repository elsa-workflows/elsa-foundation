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

- [ ] **D1** Add the single-predecessor probe on the routing memo:
  `ExecutableNode.GetOrAddRoutingStructure<FlowchartGraph>(FlowchartGraph.From).GetInboundConnections(successorNodeId).Count == 1`
  (Sequence is intrinsically single-successor). No graph walk, no second cache.
- [ ] **D2** Extend the driver: when the invoke core returns Completed and the parent is a `ReplaySafe`
  routing composite and the successor edge is single-predecessor, call the parent-completion core inline,
  then emit the successor `ScheduleActivity` directly into the D1 driver (re-enters C1). Fan-in/join
  (`Count > 1`) and External parents fall back to the discrete cascade.
- [ ] **D3** Extend `ReplaySafeFusionGuardrailTests`: shapes (b) multi-outcome branch (single-predecessor
  successors) and (c) join-falls-back — ENABLED vs DISABLED byte-identical.
- [ ] **D4** Extend `ReplaySafeFusionCrashConvergenceTests`: D2 kill points inside the inline cascade.
- [ ] **D5 GATE**: all guardrails + eight QA suites + full solution build. Commit:
  `feat(runtime): D2 inline single-predecessor completion propagation for ReplaySafe composites`.

## Increment E — amendments + A/B benchmark + final QA

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
