# Research: ReplaySafe hop fusion (ADR 0047 D1+D2)

This file records the source-of-truth reading that grounds the spec. Everything here is
`code-is-truth` — read from `src/` on the `worktree-agent-a9886080ad6b0451b` branch, not from prose.
Where a claim is load-bearing for the fusion seam, the exact file and behavior is cited.

## 1. The discrete hop chain, as it actually runs today

A leaf `ReplaySafe` activity inside a routing composite is executed by this handler chain (all under
`src/Elsa/Workflows/Runtime/Services/` unless noted). Each item is one drain-loop iteration:
claim → dispatch → commit → outbox-deliver-continuation → next iteration.

1. **`WorkflowScheduleActivitySchedulerWorkHandler`** (`ScheduleActivity`). `ExecuteAsync` reads the
   pinned executable (`PinnedExecutableRead.FindAsync`, spec-111 burst-cached), resolves the
   `ExecutableNode`, and — for a fresh activity execution — builds the `Scheduled`
   `ActivityExecutionState` (`NewActivityExecutionState`) and an `ActivityScheduled` **mandatory**
   commit (`NewCommitAsync`) whose single `PostCommitIntent` is
   `SchedulerWorkHandlerHelpers.NewEnqueueSchedulerWorkIntent(... startWorkItem ...)`. The
   `StartActivity` work item is produced by `NewStartActivityWorkItem` (derived ids via
   `RuntimeChainId.Derive`). If the state already exists and is `Scheduled`, it re-enqueues the same
   `StartActivity` item (idempotent fold-forward). **This is the fusion entry point.**

2. **`WorkflowStartActivitySchedulerWorkHandler`** (`StartActivity`). Finds the `Scheduled` state,
   optionally activates owned variable frames (`RuntimeContainerScopeService.ActivateOwnedFramesAsync`),
   **materializes the input snapshot** (`MaterializeInputSnapshotAsync`: durable-value
   `ListAllDurableValueStatesAsync` + runtime-view `ListAllAsync` + visible variable frames), transitions
   to `Running` (`StartActivity`), and builds an `ActivityStarted` mandatory commit whose
   `PostCommitIntent` enqueues the `InvokeActivity` item (`NewInvokeActivityWorkItem`). Intrinsics route
   to `WorkflowIntrinsicExecutor` instead (see §4). If already `Running`, re-enqueues `InvokeActivity`.

3. **`WorkflowInvokeActivitySchedulerWorkHandler`** (`InvokeActivity`, in
   `src/Elsa/Activities/Runtime/Services/`, ~1310 lines). Claims the attempt
   (`ActivityAttemptActivationClaimer.ClaimInvokeAsync`, already `Deferred` for `ReplaySafe` per ADR 0032
   R2), activates the activity, runs the body (`activity.ExecuteAsync` or
   `IRuntimeStructuralActivity.ExecuteStructureAsync`), and commits one of several terminal shapes:
   `CommitCompletedActivityAsync` (the straight-line case — `ActivityCompleted`, post-commit intent =
   the parent-completion `CompleteActivity` work item), `CommitChildSchedulingActivityAsync` (a composite
   that scheduled children), `CommitStatefulSuspensionAsync` (suspend/bookmark), or `RecordFaultAsync`.
   **The suspend / child-scheduling / fault arms are the D1 mid-span fallback exits.**

4. **`WorkflowCompleteActivitySchedulerWorkHandler`** + **`WorkflowParentActivityCompletionSchedulerWorkHandler`**
   (`CompleteActivity`, the latter in `src/Elsa/Activities/Runtime/Services/`). The completion cascade —
   `ActivityCompleted` → `ParentCompletionEvaluation` (route the child outcome to successors via the
   composite engine) → `ContinuationScheduling` (emit the successor `ScheduleActivity`) → `Checkpoint`.
   **This is the D2 cascade.**

**Confirmed:** the *only* difference between the discrete path and a fused pass is whether the
intermediate `StartActivity` / `InvokeActivity` / cascade work items **round-trip through the queue +
drain loop**, or are executed **inline within the outer dispatch**. Every stage produces the same
`RuntimeCheckpointCommit` (same checkpoint id, name, state changes, inspection projection, metadata)
regardless of dispatch boundary. This is what makes byte-identical achievable: fusion reuses the exact
stage code, it does not re-implement it.

## 2. Where a "live burst" lives, and why the spec-109 carrier is NOT usable inside it

`WorkflowDrainOrchestrator.DrainCoreAsync` chooses:

- **Immediate path** (`DrainImmediateAsync`) — no coalescing scope factory, or a per-run authored
  Immediate cadence (ADR 0032 R5). Pushes a `RuntimeLiveDrainDeliveryScope` (spec 109 carrier home).
- **Coalescing path** — `_coalescingScopeFactory.Begin(...)` establishes a `RuntimeCoalescingSession`,
  drains, then `FlushAtQuiescenceAsync`. **This is "a live burst"** in the ADR 0047 sense: the session
  buffers deferred commits into one segment and folds them into a single flush.

Critical finding: under coalescing, **the spec-109 in-process-hop carrier stands down** —
`RuntimeCheckpointCommitter.PublishInProcessHopWorkItems` returns early when a coalescing session owns
the execution (`_coalescingSessionAccessor.Current.AppliesTo(...)`), mirroring spec-106 FR-003. Inside a
burst, continuations flow through the **coalescing overlay outbox** instead:
`RuntimeCoalescingSession` mirrors the scheduler queue (`EnqueueOverlayAsync` / `ClaimOverlayAsync` /
`DequeueOverlayAsync`) and the post-commit outbox (`GetDeliverableOutbox` / `ClaimOutbox`), and
`AdvanceInnerQueueAsync` reconciles the durable queue only at flush. So the fused pass **must not** rely
on `RuntimeLiveDrainDeliveryScope`; it operates against the coalescing session's overlay, or (cleaner)
drives the continuation inline before the continuation work item is ever enqueued to the overlay.

`DrainSchedulerAndPostCommitWorkAsync` runs the burst as **cycles**: `schedulerDrainer.DrainAsync`
drains the (overlay) queue to empty, then `postCommitOutboxProcessor.ProcessAsync` delivers
`EnqueueSchedulerWork` intents (enqueuing continuations back into the overlay), and the cycle repeats
until an outbox delivery adds nothing. Each stage today = one drain iteration + one outbox delivery.
**Fusion collapses the intermediate drain iterations + outbox deliveries into the single outer
dispatch.**

## 3. The `ReplaySafe` signal and single-predecessor detection (both already pinned)

- **`SideEffectProfile.ReplaySafe`** lives on `ActivityContract.SideEffectProfile`
  (`src/Elsa/Activities/Runtime/Core/Models/ActivityContract.cs`), part of the pinned, fingerprinted
  contract. `External` is the fail-safe default; unmarked ⇒ `External`. The node carries its contract at
  `ExecutableNode.ActivityContract` (null for intrinsics). The invoke handler already reads
  `activityContract.SideEffectProfile` when claiming the attempt. **So the fused-eligibility test on a
  leaf is `executableNode.ActivityContract?.SideEffectProfile == ReplaySafe`** — no new plumbing.

- **ReplaySafe routing composites** (D2 parents): the ADR 0032 worked classification pins
  `Flowchart` / `Sequence` as `ReplaySafe`. `src/Elsa/Activities/Flowchart/Activities/Flowchart.cs` and
  `Sequence/Activities/Sequence.cs` both reference `SideEffectProfile` — confirm each carries the
  ReplaySafe marker on its contract before fusing its cascade (a D2 precondition, not a new mechanism).

- **Single-predecessor detection (ADR 0047 resolution #1):** spec 119 built the inbound index.
  `FlowchartGraph.GetInboundConnections(targetNodeId)` (`src/Elsa/Activities/Flowchart/Internal/FlowchartGraph.cs`,
  backed by `_inboundByTarget`) returns the inbound connections for a node; `FlowchartJoinCoordinator`
  already uses `inboundConnections.Count <= 1` as its "no join" test (line 101). The D2 fused pass reads
  the **successor's** inbound count through the same routing structure via
  `ExecutableNode.GetOrAddRoutingStructure<FlowchartGraph>(FlowchartGraph.From)` (the spec-119 memo, so
  **no graph walk**). `count == 1` ⇒ fuse; `> 1` (fan-in/join) ⇒ fall back to the discrete cascade.
  Sequence is intrinsically single-successor per step.

## 4. Fallback exits (every one must leave discrete-equivalent state)

Enumerated from the handler code so the fused driver knows exactly when to stop fusing and let the
already-produced continuation work item flow through the normal queue instead:

| Exit condition | Detected in | Discrete-equivalent handoff |
|---|---|---|
| Activity suspends / creates a bookmark | invoke handler `CommitStatefulSuspensionAsync` arm (`typedSuspendedState != null` / bookmark work items) | The suspension commit + its bookmark/dispatch intents are the terminal of the span; nothing further fuses. Byte-identical because it is the exact same commit. |
| Contract is `External` or unmarked | `executableNode.ActivityContract?.SideEffectProfile != ReplaySafe` | Never enter fusion; discrete chain from `ScheduleActivity`. |
| Intrinsic with mutating state load (`Finish`/`Correlate`/`SetName`/`SetOutput`) | `executableNode.IntrinsicKind` in the mutating set (see `WorkflowIntrinsicKind`); start handler routes to `WorkflowIntrinsicExecutor` | Excluded in first iteration (ADR 0047 D1 "What is NOT fused"). Non-mutating intrinsics MAY fuse only if proven byte-identical; conservative default is exclude-all-intrinsics in v1. |
| Composite scheduled children (a fork) | invoke handler `CommitChildSchedulingActivityAsync` arm (`pendingChildScheduling != null`) | The child `ScheduleActivity` intents flow normally; the fused pass may continue into a *single* child only if it is single-predecessor and ReplaySafe (D2), else fall back. |
| Fault | invoke handler `RecordFaultAsync` arm | The incident commit (+ optional child-fault parent evaluation) is the terminal; nothing further fuses. |
| Fan-in / join successor | `GetInboundConnections(successor).Count > 1` (ADR 0047 res #1) | Emit the successor `ScheduleActivity` to the queue; discrete cascade evaluates the join on runtime state. |
| External parent composite | parent `ActivityContract.SideEffectProfile != ReplaySafe` | Discrete completion cascade (unchanged; D3 routing table still applies). |
| No live burst / toggle off | `_coalescingSessionAccessor.Current` absent / `RuntimeReplaySafeFusionOptions.Enabled == false` | Discrete chain everywhere. |

**Mid-span invariant:** a span that exits at any of these must have committed exactly the checkpoints the
discrete path would have committed *up to that point*, in the same order, and left the next work item
either (a) enqueued to the overlay (so the drain loop picks it up) or (b) subsumed by a terminal commit.
Because the fused driver reuses the stage handlers verbatim and only elides the *enqueue+redispatch* of
intermediate items, the mid-span state is the discrete state by construction — the guardrail proves it.

## 5. Crash semantics (unchanged idempotency ladder)

The durable queue holds only the original `ScheduleActivity` item for a fused span (the intermediate
`StartActivity`/`InvokeActivity` items are never durably enqueued — they are inline). This is **identical
to a mid-segment coalescing crash today**: `RuntimeCoalescingSession.AdvanceInnerQueueAsync` only deletes
consumed seeded items after the folded commit lands, so a crash mid-burst replays the whole segment from
the last flush plus durable `ScheduleActivity` redelivery. Redelivery resolves through the existing
ladder: queue enqueue-by-identity (ADR 0031 ratification decision 1), status-based handler no-ops
(`ScheduleActivity` sees `Scheduled`/`Running`/`Completed` and re-enqueues or no-ops; `InvokeActivity`
checks `WasInitialActivationCompleted`), and fold-forward claim state. **No new crash-recovery
mechanism is introduced** — this is the load-bearing reason the design is safe: fusion changes dispatch
locality, not the durable truth or its redrive.

## 6. Toggle + counter homes

- **Toggle:** follow `RuntimeInProcessHopFastPathOptions` (`{ bool Enabled = true }`, registered before
  the runtime feature; a `false` registration forces the discrete path and MUST commit byte-identical
  state). New: `RuntimeReplaySafeFusionOptions { bool Enabled = true }` (default ON, resolution #3).
  Wire it into `RuntimeCheckpointCommitter`/the drainer's fusion seam the same way the fast-path options
  are threaded (optional ctor arg defaulting to `new()`), and register in
  `RuntimeCoreServiceCollectionExtensions`.

- **Dispatches-per-run counter:** the A/B evidence lives in
  `benchmarks/Elsa/Workflows/Runtime/Benchmarks/DurableRoundTripDiagnostics.cs` (spec 110/114 home,
  already tracks commits/run and executable-reads/run). Add a `Dispatches` counter incremented once per
  `WorkflowSchedulerDrainer.DispatchAsync` (the deterministic hop-count evidence: ~7/leaf-edge today →
  ~1–2 fused). Expose `DispatchesPerRun` alongside the existing derived metrics.

## 7. Seam decision (the least-invasive equivalent, justified)

The ADR names `RuntimeSchedulerPipelineSelector` as the selection point. That selector currently maps a
work item to a `RuntimePipelineKind` (Activity/Workflow) — it does not carry fused-span state. The
**least-invasive equivalent seam** the spec adopts is a **fused-span driver invoked from the
`ScheduleActivity` handler's terminal (post-commit continuation) point**, gated by
`RuntimeReplaySafeFusionOptions` + an active coalescing session + `ExecutableNode.ActivityContract`
ReplaySafe. Rationale (recorded for the plan):

- The `ScheduleActivity` handler already computes the next (`StartActivity`) work item and the node's
  contract; it is the natural place to decide "continue inline vs enqueue."
- The stage cores are extracted (D1 mechanism) into stage services callable by BOTH the existing
  per-kind handlers (durable path, byte-identical, untouched) and the driver — so the driver never
  re-implements a stage; it calls the same core the handler calls.
- The driver runs each subsequent stage core, staging each stage's commit through the **same
  `RuntimeCheckpointCommitter`** (which, under the active session, buffers it into the same segment the
  discrete path would have buffered), then inspects the produced continuation to decide fuse-vs-fallback
  per §4. On any fallback it stops and lets the last produced work item flow to the overlay queue
  unchanged.
- `RuntimeSchedulerPipelineSelector` stays the *classification* authority; the *fusion* decision is a
  strictly additive driver that reuses it. This keeps the durable wire contract and command kinds
  untouched (ADR 0047 D1 "No new command kinds").

This is the plan of record for the code increment (see tasks.md). It is deliberately handed off with the
guardrail harnesses specified rather than implemented, because byte-identical + crash-convergence are
empirical gates that must be run green before the driver is trustworthy.
