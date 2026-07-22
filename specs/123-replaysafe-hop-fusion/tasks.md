# Tasks: ReplaySafe hop fusion (ADR 0047 D1+D2)

Dependency-ordered. Each increment is one review-able commit. File paths are absolute-from-repo-root.
Locate classes by name where the `Core/Services` vs `Services` folder split varies.

## Increment A — stage-core extraction refactor (behavior-preserving)

- [ ] **A1** Extract the **schedule stage core** from `WorkflowScheduleActivitySchedulerWorkHandler.ExecuteAsync`
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

## Increment B — toggle + dispatch counter

- [ ] **B1** Add `src/Elsa/Workflows/Runtime/Core/Models/RuntimeReplaySafeFusionOptions.cs`
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

## Increment C — D1 fused schedule→start→invoke

- [ ] **C1** Add the fused-span driver, invoked from the schedule handler's terminal continuation point,
  gated by: `RuntimeReplaySafeFusionOptions.Enabled` AND an active coalescing session
  (`IRuntimeCoalescingSessionAccessor.Current?.AppliesTo(executionId)`) AND
  `executableNode.ActivityContract?.SideEffectProfile == ReplaySafe` AND not a mutating intrinsic.
- [ ] **C2** Driver loop: after the schedule core commits, call the start core inline with its produced
  work item; if the start core returns a non-mutating-intrinsic Running result, call the invoke core
  inline. Stage every commit through the same `RuntimeCheckpointCommitter` (buffers into the same segment).
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
