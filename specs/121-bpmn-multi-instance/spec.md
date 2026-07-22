# 121 — BPMN multi-instance activities (sequential + parallel; graphs stay acyclic) (BPMN Phase 2, events tier, iteration-frame + seam-A/B consumer)

## Goal

Let a published BPMN process attach **multi-instance loop characteristics**
(`multiInstanceLoopCharacteristics`) to a host element (a task-family element or an embedded
`subProcess`) that has a bound child, so the bound child runs **N times** — **sequentially** (one instance
at a time, each starting when the previous completes) or **in parallel** (N concurrent instances of the
*same* executable child node, scheduled up front). Each instance receives its own per-iteration variable
frame (`LoopIterationScopeRequest`) seeding a zero-based `loopIndex`. When the last instance completes, the
host routes its outbound flows through its normal behavior, exactly as a single-run task/subprocess does.

This is the module's first consumer of the runtime **iteration-frame** seam (spec-proven by `ForEach`) and
the first activity anywhere to schedule **concurrent same-node children** (N live instances of one
executable node). It composes with the spec 119 event-based-gateway race and the spec 120 boundary/error
teardown: cancelling a multi-instance host cancels **all** its live instances through seam A, and an error
boundary on a multi-instance host absorbs a faulted instance through seam B while the interruption cascade
tears the remaining instances down. Behaviors stay multi-instance-unaware; the engine owns the entire loop
lifecycle, exactly like the spec 119 race and spec 120 boundary interrupt live in the engine, not the
`TaskBehavior`/`SubProcessBehavior`.

The graph acyclicity restriction is **not** lifted in this unit — cyclic sequence flows remain the follow-up
unit, and the importer's cycle degradation and `BpmnGraph.ValidateAcyclic` stay byte-identical. **Token
iteration keys for join accounting stay out of scope**: a multi-instance host is a single coordinator token
with private per-instance sub-tokens, not a fan-out over the join machinery; joins downstream see one
arrival from the coordinator.

### Scope cut carried in this slice (collection mode deferred — see Deviations)

Two authoring shapes are modeled (D1): **cardinality** (a literal instance count) and **collection** (run
once per item of a container-scoped collection variable). Only **cardinality mode is executable in this
slice.** Collection mode's per-instance item value requires reading a container-scoped variable's *current
value* during a structural evaluation, and that read is **not cleanly available**: the runtime threads the
`VariableScope` read view as `null` into every `BpmnProcess` structural evaluation
(`WorkflowInvokeActivitySchedulerWorkHandler`, `WorkflowParentActivityCompletionSchedulerWorkHandler`,
`WorkflowResumeBookmarkSchedulerWorkHandler` all pass `variableScope: null`), and
`IRuntimeActivityExecutionContext` exposes no variable-value read at all. Wiring that read would need a new
runtime seam (thread `VariableScope` into consumer structural evaluations **and** expose it on the
interface) beyond the single ratified runtime model addition (D2c), or an improvised reach into
`ActivityExecutionState.VariableFrame.Values` re-implementing envelope-unwrap + name→key resolution. Per the
work-unit's stop-and-report discipline, collection mode is **deferred**: it is authoring-representable and
validated as a stated cut, degraded on import, and left for the follow-up unit that adds the variable-read
seam. Cardinality mode delivers the complete multi-instance sub-token engine, the teardown fixes, the
boundary interplay, determinism, and interchange.

## Context (what exists today, origin/main = bb4caac50)

- **Token engine** (`BpmnExecutionEngine`): `StartAsync` / `OnChildCompletedAsync` / `OnChildFaultedAsync`.
  `OnChildCompletedAsync` resolves the completing token (`ResolveTokenId` — `bpmn.tokenId` command-metadata
  primary, by-node fallback), `RemoveActiveChild`, guards terminated/canceled tokens (late-completion
  absorption), resolves a live event-based-gateway race (spec 119) and applies boundary-event completion
  semantics (spec 120) **before** dispatching the completing element's behavior, then `ApplyDecision`,
  `Propagate`, `FinishEvaluation`. `ApplyDecision`'s `ScheduleChild` case flips the arriving token to
  `AwaitingChild`, schedules the element's single `ChildNodeId`, and arms the host's catch boundaries.
  `OnChildFaultedAsync` resolves the faulted child → its host → an attached error boundary; with one it
  absorbs the fault through seam B (`AbsorbChildFaultThroughErrorBoundary`), else it returns the
  deterministic `bpmn.child.faulted` composite fault. `FinishEvaluation` stages state, picks the
  continuation, and stages the carried seam-A subtree cancellations + the seam-B absorption **only** at the
  clean `Complete`/`Defer` exits.
- **The carry plumbing (spec 119/120).** `EvaluationResult.PendingSubtreeCancellations` /
  `BpmnPendingSubtreeCancellation(ActivityExecutionId, ElementId, Reason)` + `EvaluationResult.FaultAbsorption`
  / `BpmnFaultAbsorption(IncidentId, Reason)`; `CancelTokenAndChild` flips a live token to `Canceled`, drops
  its active-child record, and — when its armed child is a live direct child — appends a seam-A cancellation;
  `CancelHostListeners` tears down boundary listeners parented to a host token; `BuildLiveChildAeiByNode`
  resolves a subtree's live child aei from a `BpmnActiveChild.NodeId`. Reason consts:
  `bpmn.event-based-gateway.superseded-by-first-catch`, `bpmn.boundary.superseded-by-host-completion`,
  `bpmn.boundary.host-interrupted`, `bpmn.boundary.error-absorbed`.
- **State** (`Elsa.Bpmn.ExecutionState`, schema version 1): `BpmnToken`
  (`TokenId, AtElementId, FlowId, ParentTokenId, Status ∈ Active/AwaitingChild/WaitingAtJoin/Consumed/Canceled,
  ProducingActivityExecutionId`), `BpmnActiveChild` (`NodeId, ElementId, TokenId, SchedulingCause`),
  `BpmnEventRace` (spec 119). `BpmnStateMutator` is the sole mutation home; every record id derives from
  `Sequence`; `Canceled` tokens are never pruned; a terminal continuation never co-exists with staged child
  schedules; a `Fault`/`Cancel` continuation never co-exists with a staged seam-A/seam-B request. Additive
  state growth keeps schema version 1.
- **Iteration frames (runtime, `ForEach`-proven).**
  `LoopIterationScopeRequest(OwnerNodeId, IterationId, Values)` — `Values` a non-empty
  `IReadOnlyDictionary<string, ValueEnvelope>`; values never travel through scheduling metadata; transient
  (`DurableValueLifecycle.None`) envelopes are rejected at activation.
  `IRuntimeActivityExecutionContext.ScheduleChildActivity(..., LoopIterationScopeRequest? iterationFrame = null)`
  is the per-schedule input channel. `RuntimeContainerScopeService.ActivateOwnedFramesAsync` materializes the
  frame as the innermost lexical scope; `ValidateIterationOwnershipAsync` requires
  `OwnerNodeId == parentState.Execution.ExecutableNodeId` (the scheduling parent's executable node id),
  `IterationId == activityState.IterationId == activityState.Provenance.IterationId`, and
  `activityState.SchedulingActivityExecutionId == activityState.ParentActivityExecutionId`. The scheduled
  child's `IterationId` is set from `provenance.IterationId`; its `ParentActivityExecutionId` is always the
  structural parent's aei; its `SchedulingActivityExecutionId` is the schedule request's
  `SchedulingActivityExecutionId` (defaulting to the parent aei). `IterationId` is otherwise OPAQUE to the
  runtime (`StructuredExecutionIdentity.Iteration` is just `ForEach`'s format; a caller may mint its own).
- **Concurrent same-node children (verified permitted, exercised by no shipped activity).** Each
  `ScheduleChildActivity` mints a fresh unique activity-execution id; the state store keys by aei; live
  bookmarks are aei-scoped (`Delay` mints `timer:{invocationId}`; an `Event` wait registers a per-execution
  bookmark). N live instances of one node therefore neither collide on state nor on bookmarks.
- **What breaks under same-node concurrency (fixed here).** `BuildLiveChildAeiByNode` keys
  `liveChildAeiByNode[nodeId] = aei` **last-wins**; with N live instances of one node it resolves only one
  aei, so the spec 119/120 teardown could cancel the wrong subtree. Fixed per D2c (key by `(nodeId,
  iterationId)`). `ResolveTokenId`'s node-id **fallback** throws when >1 live child shares a node id — but the
  primary `bpmn.tokenId` command-metadata path always wins for engine-scheduled children, so completion
  routing stays safe; the fallback is kept as-is and documented.
- **Graph** (`BpmnGraph.Validate`): unique ids, resolvable refs, ≥1 start event, per-family child-binding
  rules, single default flow, `ValidateEventBasedGateways`, `ValidateBoundaryEvents`, `ValidateAcyclic`.
  `BpmnElement` carries no loop fields. `BpmnElementFamilies.IsBoundaryHostFamily` (= task family +
  `subProcess`) is the host predicate reused for multi-instance placement. `BpmnStructure.Variables` /
  `BpmnAuthoredStructure.Variables` carry the container-scoped variable declarations (ADR 0027) and are
  available at validation time.
- **Interchange.** No `loopCharacteristics` handling exists; the importer detects cycles and degrades ("not
  executable in this slice"), which stays.

## Design decisions

### D1 — Authoring model (additive; schema stays version 1)

- `BpmnElement` gains one additive optional constructor channel, `LoopCharacteristics` (`BpmnLoopCharacteristics?`,
  `null` on every non-multi-instance element).
- `BpmnLoopCharacteristics` (nullable record):
  - `IsSequential` (`bool`) — `true` = one instance at a time; `false` = all instances up front (parallel).
  - Exactly **one** of `Cardinality` (`int?`, positive) **XOR** `CollectionVariable` (`string?`, the name of a
    declared container-scoped variable holding a collection).
  - `ItemVariable` (`string`, default `"item"`) — the per-iteration frame's item key; meaningful only in
    collection mode.
  - The per-iteration frame **always** seeds `"loopIndex"` (zero-based `int`); in collection mode it also
    seeds the item under `ItemVariable`.
- Valid only on an element whose family is task-family or `subProcess` **and** that binds a child
  (`ChildNodeId`) — the boundary-host predicate `BpmnElementFamilies.IsBoundaryHostFamily`, reused (a
  multi-instance host runs a child, so childlessness is a reject).
- **`BpmnGraph.Validate` gains `ValidateMultiInstance`**, each rule a deterministic `BpmnExecutionException`
  naming the element:
  1. **Loop only on a multi-instance host with a bound child.** A `LoopCharacteristics` on a childless
     element, or on a non-host family (start/end/gateway/catch/boundary), is rejected.
  2. **Exactly one of cardinality/collection.** Both-set or neither-set is rejected.
  3. **Cardinality ≥ 1.** A non-positive `Cardinality` is rejected.
  4. **Collection variable declared.** A `CollectionVariable` that names no declared container-scoped variable
     (`BpmnStructure.Variables`) is rejected.
  5. **Collection mode is not executable in this slice (stated cut).** A collection-mode `LoopCharacteristics`
     is rejected with a `not executable in this slice` message (parallel to the acyclicity cut). This keeps
     the executable graph honest and keeps the importer validate-representable (it degrades collection mode
     rather than emit a rejected element). The follow-up unit removes rule 5 when the variable-read seam
     lands. *(Rule 4 still fires first for the undeclared-variable case so the authoring error is specific.)*

  Boundary events and multi-instance compose: a multi-instance host may still carry boundary events (D2e);
  a boundary element itself may never carry loop characteristics (rule 1 — a boundary is not a host family).

### D2 — Runtime semantics (engine-owned; behaviors stay multi-instance-unaware; UNIFORM sub-token model for sequential AND parallel)

**D2a — Loop start.** When a token arrives at a multi-instance element and the element's behavior returns
`ScheduleChild`, the engine does **not** schedule the bound child on that token. Instead:

- It resolves the instance count `N`. Cardinality mode: `N = Cardinality`. **Collection mode: rejected at
  validation (D1 rule 5), so it never reaches the engine this slice.** The collection value would be read
  once at loop start (a documented snapshot) through the context's variable view; because that view is
  `null` for structural evaluations (see Deviations), collection mode is deferred rather than reading through
  a side channel.
- **`N == 0`** (empty-loop-equivalent; unreachable via cardinality since `Cardinality ≥ 1`, retained for the
  collection follow-up): the multi-instance element completes immediately and routes its outbound flows via
  its normal behavior (`OnChildCompleted` with empty outcomes — task-flow selection; `bpmn.flow.none-taken`
  fault as usual). Documented.
- **`N ≥ 1`**: the arriving token becomes the **loop coordinator** — it stays live with status
  `AwaitingChild` (it is awaiting its instance children; no new token status is introduced). An additive
  state record is written via `BpmnStateMutator`:
  `BpmnLoopState(LoopId, TokenId, ElementId, IsSequential, TotalCount, NextIndex, CompletedCount)` —
  `LoopId` from `Sequence`, `TokenId` = the coordinator token id, `TotalCount = N`, `NextIndex`/`CompletedCount`
  starting at 0. The host's catch boundaries are armed **once** here, parented to the coordinator token
  (D2e). Then instances are scheduled: **sequential** → the single instance at index 0; **parallel** → all
  `N` instances (indexes 0..N-1), in one evaluation.

**D2b — One instance = one sub-token + one framed child schedule.** Each instance is a child token
(`NewToken` at the SAME element, `ParentTokenId` = the coordinator token id, status `AwaitingChild`) plus a
`ScheduleChild` of the host's bound child node on that instance token, carrying an iteration frame:

- `OwnerNodeId` = the `BpmnProcess`'s own executable node id (`context.ExecutableNode.ExecutableNodeId`) — the
  scheduling parent required by the ownership guard.
- `IterationId` minted deterministically from BPMN state: `bpmn-mi:{instanceTokenId}` (the instance token id
  is `Sequence`-derived and unique; **not** `ForEach`'s `parentAEI:index` format, because multiple
  multi-instance hosts share the one `BpmnProcess` parent aei and would collide).
- `Values` = `{ "loopIndex": <index:Int32> }` (+ item in collection mode). The `loopIndex` envelope is a
  durable `InstanceInline` envelope (never transient, so it survives the scheduling boundary).
- `SchedulingActivityExecutionId` = the `BpmnProcess`'s own aei (NOT the completing child's aei that ordinary
  BPMN completion-driven propagation uses), so the child's `SchedulingActivityExecutionId ==
  ParentActivityExecutionId == owner-node aei` and the ownership guard passes. Provenance `iterationId` = the
  minted `IterationId`; `executionPathId` = the instance token id.

Sequential: exactly one live instance token at any time. Parallel: all `N` instance tokens + framed
schedules staged in the same evaluation → `N` concurrent same-node children (each a fresh unique aei, each
its own aei-scoped bookmark).

**D2c — Instance completion + the same-node teardown fix.** An instance child's completion arrives on its
instance token (resolved by the primary `bpmn.tokenId` path). The engine intercepts multi-instance instance
completions **before** the normal behavior dispatch (mirroring the spec 120 boundary interception):

- Drop the instance's active-child record; **consume** the instance token.
- Advance the loop record: `CompletedCount + 1`.
- **Not the last instance**: sequential → schedule the next instance (index `NextIndex`, `NextIndex + 1`);
  parallel → nothing more to schedule. Persist the advanced loop record. The instance-completion evaluation
  does **not** route outbound and does **not** tear boundary listeners down.
- **Last instance** (`CompletedCount == TotalCount`): drop the loop record; the **coordinator** token
  "completes" — its still-armed catch listeners are torn down (spec 120 host-completion semantics on the
  coordinator, `bpmn.boundary.superseded-by-host-completion`), then the coordinator routes the element's
  outbound flows through its NORMAL behavior (`OnChildCompleted` with empty outcomes — task-flow selection;
  `bpmn.flow.none-taken` as usual).

Runtime model addition for teardown: `RuntimeLiveChildActivity` gains a nullable `IterationId` (populated
from the child's `ActivityExecutionState.IterationId`, which the completion handler already loads;
additive), and `BuildLiveChildAeiByNode` becomes a `(NodeId, IterationId)`-keyed structure so
`CancelTokenAndChild` resolves the RIGHT instance's aei — the null-iteration entry preserves the existing
non-multi-instance behavior byte-for-byte. `BpmnActiveChild` gains a nullable `IterationId` (additive BPMN
state; the iteration id the instance child was scheduled with, `null` for ordinary children) so a teardown
knows which `(NodeId, IterationId)` key to resolve. `ResolveTokenId`'s node-id fallback is unchanged (the
primary `bpmn.tokenId` path always wins for engine-scheduled children); its throw-on-ambiguity is retained
and documented as unreachable for engine-scheduled multi-instance children.

**D2d — Teardown interplay (compose with specs 119/120).** Whenever the multi-instance coordinator token is
cancelled, all its live instance tokens cascade-cancel (flip `Canceled`, drop active-child records, carry a
seam-A cancellation per instance keyed by `(NodeId, IterationId)`) and the loop record is dropped.
`CancelTokenAndChild` is generalized: cancelling a token that is a loop coordinator first cascade-cancels its
instance tokens (parented to it, at the same element) and drops the loop record — so cancelling a
multi-instance host cancels ALL live instances through one code path. Every path that cancels a host token
rides this: an interrupting boundary (catch or error) on the multi-instance host, an event-based-gateway
loser whose armed child is a multi-instance host, and the error-boundary absorption path.

- **Instance fault with NO error boundary on the host** → the existing `bpmn.child.faulted` composite fault,
  unchanged (any sibling instances/listeners fall to logical-only teardown when the composite faults,
  matching the spec 119/120 pending-fault precedent).
- **Instance fault WITH an error boundary on the host** → spec-120 absorption absorbs the faulted instance
  (seam B), and the host-interruption cascade cancels the coordinator + all remaining live instances (seam
  A) and the sibling catch listeners; the error boundary's outbound flows route; the composite continues.
  The faulted instance's own subtree reclaim rides the seam-B absorption plan (no seam-A for it); the faulted
  instance token flips `Canceled`. `OnChildFaultedAsync` resolves the interrupt target as the coordinator (the
  faulted instance token's parent when it is a loop coordinator), else the faulted child's own token
  (non-multi-instance path, unchanged).

**D2e — Boundary events on a multi-instance host.** Catch listeners arm **once** when the coordinator token
arrives (loop start, D2a — not per instance), parented to the coordinator token. The host "completes" for
listener-teardown purposes when the LAST instance completes (D2c). An interrupting listener firing mid-loop
cancels the coordinator (→ cascade-cancels all instances, D2d) and routes the boundary path. A
non-interrupting listener firing leaves the coordinator and its instances running. The spec 120 arming
(`ArmCatchBoundaries`) and teardown (`CancelHostListeners`, `ApplyBoundaryCompletionSemantics`) hooks fire at
the multi-instance coordinator's arrival/completion points; tests assert they fire once and at the right
times.

### D3 — Stated cuts

- **Collection mode execution** (deferred; authoring-modeled + validated as a cut + import-degraded — see
  Deviations). `completionCondition`; per-instance `elementVariable` **data-output aggregation**;
  `standardLoopCharacteristics` (the classic while/until loop); cyclic sequence flows (the next unit —
  acyclicity validation and the importer's cycle degradation stay byte-identical); multi-instance on
  event-defined elements; token iteration keys for join accounting (a multi-instance host is one coordinator
  arrival at any downstream join, not a fan-out).
- **Nested multi-instance.** Multi-instance on the SAME element is impossible by shape (one
  `LoopCharacteristics` per element). A multi-instance host whose bound child is itself a nested `BpmnProcess`
  containing its own multi-instance host **composes naturally** (each `BpmnProcess` runs its own engine over
  its own state) and needs no special handling.

### D4 — Interchange

- **Import.** A `<multiInstanceLoopCharacteristics isSequential="…">` on a supported host (task-family or
  `subProcess`):
  - `<loopCardinality>` an integer literal → `Cardinality` (`isSequential` absent → `false`, matching BPMN).
  - Collection mode via elsa-namespaced attributes on the loop-characteristics element
    (`elsa:collection="varName"`, `elsa:itemVariable="item"` — no standard BPMN dataInput machinery is
    modeled here; documented extension attributes) → **Degraded**: the host imports **without** loop
    characteristics + a finding (collection mode is not executable in this slice; validate-representable —
    the importer never emits an element the validator rejects).
  - A non-integer/empty `loopCardinality`, a `standardLoopCharacteristics`, or `<dataInputRefs>`/completion
    forms → **Degraded** (host imported without loop characteristics) + a finding.
  - Loop characteristics on an unsupported host (childless / non-host family) → **Degraded** finding
    likewise.
- **Export.** A task/subprocess element carrying cardinality `LoopCharacteristics` emits a nested
  `<multiInstanceLoopCharacteristics isSequential="…"><loopCardinality>N</loopCardinality></…>`
  (`isSequential="false"` is emitted explicitly for clarity; the importer defaults absent → false, so the
  round-trip holds either way — the exporter emits the authored value). A collection-mode element (only
  reachable when the follow-up unit lands, since import degrades it and validation rejects it) exports
  `elsa:collection`/`elsa:itemVariable`; this slice never produces one. Round-trip is stable for cardinality
  mode. `SynthesizeBounds` is unchanged (a multi-instance host is still a `100×80` task/subprocess shape;
  loop marker glyphs are a Studio concern). DI bounds unchanged.

## In scope (this slice)

- **Model/validation (D1):** `BpmnElement.LoopCharacteristics` + `BpmnLoopCharacteristics`;
  `BpmnGraph.ValidateMultiInstance`.
- **Runtime model (D2c):** `RuntimeLiveChildActivity.IterationId` (additive); `BpmnActiveChild.IterationId`
  (additive BPMN state); `BuildLiveChildAeiByNode` keyed by `(NodeId, IterationId)`; the loader in
  `WorkflowParentActivityCompletionSchedulerWorkHandler` passes `child.IterationId`.
- **State (D2a):** `BpmnLoopState` record + `BpmnStateMutator` add/update/remove; `BpmnExecutionState.Loops`.
- **Engine (D2a–e):** loop start in `ApplyDecision`'s `ScheduleChild` case (coordinator + framed instance
  schedules; `N==0` immediate route); multi-instance instance-completion interception in
  `OnChildCompletedAsync` (advance / schedule-next / last-completes-and-routes); coordinator cascade
  cancellation in `CancelTokenAndChild`; multi-instance-aware interrupt target in
  `AbsorbChildFaultThroughErrorBoundary`; a `BpmnScheduler.ScheduleChild` overload carrying the iteration
  frame + minted iteration id + owner-aei scheduling id.
- **Interchange (D4):** importer `multiInstanceLoopCharacteristics` parse on task/subprocess (cardinality →
  model; collection/unsupported → Degraded finding); exporter cardinality emission; the lane-rewrite path
  preserves `LoopCharacteristics` (and the spec-120 `AttachedToRef`/`CancelActivity`).
- **Tests + module docs:** validation, sequential/parallel cardinality, per-iteration `loopIndex`,
  empty-loop, interrupting boundary mid-loop, instance fault with/without error boundary, determinism,
  interchange import/round-trip/degrade. BPMN README + EXTENSION_POINTS; Interchange README; runtime
  EXTENSION_POINTS (the one `RuntimeLiveChildActivity.IterationId` addition).

## Out of scope (deferred follow-ups, stated cuts)

- Collection-mode execution (needs the container-variable read seam); `completionCondition`; data-output
  aggregation; `standardLoopCharacteristics`; cyclic sequence flows (next unit); multi-instance on
  event-defined elements; token iteration keys for join accounting; expression-conditioned flows (unchanged
  engine-wide cut). Seam-A teardown on `CancelLiveWork` paths stays logical-only (spec 120 invariant
  unchanged).

## Functional requirements

**FR-1 — Model + validation.** `BpmnElement` carries `LoopCharacteristics` additively (schema stays version
1). `BpmnGraph.Validate` rejects, each with a deterministic `BpmnExecutionException` naming the element: loop
characteristics on a childless or non-host element; both-or-neither cardinality/collection; cardinality < 1;
a collection variable that names no declared container-scoped variable; a collection-mode loop (stated cut,
not executable in this slice).

**FR-2 — Loop start (cardinality).** A token arriving at a cardinality-`N` multi-instance host becomes an
`AwaitingChild` coordinator with a `BpmnLoopState` record; the host does not schedule its bound child on the
coordinator. Sequential mints one instance token + framed schedule (index 0); parallel mints `N` instance
tokens + framed schedules (indexes 0..N-1) in one evaluation. Each instance token sits at the host element,
is parented to the coordinator token, and its framed child receives `loopIndex` for its index. Record ids
stay a pure function of `Sequence`.

**FR-3 — Per-iteration frame.** Instance `k`'s child resolves `loopIndex == k` through the real
variable-read evaluator (the frame is owned by the `BpmnProcess` node, materialized as the innermost scope).

**FR-4 — Sequential progression.** In sequential mode exactly one instance token is live at a time; each
instance completion schedules the next until `TotalCount` is reached. Never two live instances of the host's
child concurrently.

**FR-5 — Parallel concurrency.** In parallel mode `N` instances of the SAME child node run concurrently —
`N` distinct activity executions (distinct aeis, distinct iteration ids) and `N` distinct aei-scoped
bookmarks when the child suspends — and resume independently.

**FR-6 — Loop completion routes outbound.** When the last instance completes, the coordinator routes the
host element's outbound flows through its normal behavior (task-flow selection; `bpmn.flow.none-taken`
fault as usual); the loop record is dropped. An `N == 0` loop routes immediately at loop start.

**FR-7 — Same-node teardown.** `RuntimeLiveChildActivity` and `BpmnActiveChild` carry the iteration id, and
`BuildLiveChildAeiByNode` is keyed by `(NodeId, IterationId)`, so a teardown resolves the correct instance's
aei with `N` live instances of one node. Cancelling a multi-instance coordinator token cancels ALL its live
instances (seam-A per instance) and drops the loop record.

**FR-8 — Boundary interplay.** Catch boundaries on a multi-instance host arm once at loop start (parented to
the coordinator) and tear down when the last instance completes. An interrupting boundary firing mid-loop
cancels the coordinator (→ all instances cascade-cancel through seam A) and routes the boundary path.

**FR-9 — Error boundary on a multi-instance host.** An instance fault on a host WITH an error boundary is
absorbed through seam B (incident `Resolved`/`Continue`/`ResolvedAt` + `FaultAbsorbedBy`, faulted instance
stays `Faulted`) while the host-interruption cascade cancels the coordinator + remaining instances (seam A)
and sibling catch listeners, and the error path routes; the composite continues. An instance fault on a host
with NO error boundary returns the existing `bpmn.child.faulted` composite fault unchanged.

**FR-10 — Continuation discipline.** Seam-A subtree cancellations and the seam-B fault absorption are staged
only at the clean `Complete`/`Defer` exits of `FinishEvaluation`; every fault/pending-fault/terminated/
deadlock exit skips staging. A `Fault`/`Cancel` continuation never co-exists with a staged seam-A/seam-B
request. A terminal continuation never co-exists with staged child schedules (a multi-instance loop start
that schedules instances defers).

**FR-11 — Determinism.** Identical runs produce identical token/loop-record/iteration ids and identical
instance-scheduling and cancellation order (following `Sequence`-derived id order).

**FR-12 — Interchange.** A cardinality `multiInstanceLoopCharacteristics` (sequential or parallel) on a
bound host round-trips through import→export→import with `isSequential`/`loopCardinality` fidelity. A
collection-mode or otherwise-unsupported loop-characteristics imports as **Degraded** (host without loop
characteristics) with a specific finding; the importer never emits a loop-characteristics the validator
rejects.

## Invariants that MUST survive

- `Elsa.Bpmn.ExecutionState` stays schema version 1 (the `Loops` collection and `BpmnActiveChild.IterationId`
  are additive); the only mutation home remains `BpmnStateMutator`; all record ids derive from `Sequence`;
  `Canceled` tokens are never pruned; a terminal continuation never co-exists with staged child schedules; a
  `Fault`/`Cancel` continuation never co-exists with a staged seam-A/seam-B request.
- Behaviors stay decision-only and multi-instance-unaware; the entire loop lifecycle lives in the engine. No
  new behavior family, no new token status.
- `CancelLiveWork` stays logical-only (terminate + pending-fault + error-routing-fault).
- The runtime live-children read stays opt-in (marker-gated), read-only, spoof-proof, populated only for
  child-completion/child-fault evaluations. The one runtime model addition is
  `RuntimeLiveChildActivity.IterationId`; no new runtime seam is introduced (the iteration-frame seam is
  reused as-is).
- Acyclicity is unchanged (`ValidateAcyclic` + the importer's cycle degradation byte-identical). Deterministic
  ids only; no wall-clock-derived identity. No new HTTP endpoints; the domain project-tree naming guard and
  VF-ACT gates hold. The spec 119/120 suites pass unmodified except for the mechanical `(NodeId, IterationId)`
  key generalization.

## Success criteria

- Validation tests: each FR-1 rule rejected deterministically (extends `BpmnGraphValidationTests`).
- Sequential cardinality-3: instances run one at a time (never 2 live — resumed one at a time, bookmark count
  stays 1), each `loopIndex` observed (`0,1,2` via a capture child), the loop routes outbound to completion.
- Parallel cardinality-3: 3 concurrent instances of the SAME node (3 states with distinct aeis + distinct
  iteration ids, 3 distinct bookmarks when suspended), independent resumes, last completion routes.
- Empty/zero-equivalent loop → immediate completion + route (documented; unreachable via cardinality, covered
  through the engine's `N == 0` path where testable).
- Interrupting boundary on a multi-instance host mid-loop: all live instances torn down (durable child state
  `Cancelled`/`ParentCancelled` + bookmarks gone), boundary path routes.
- Instance fault: without error boundary → composite fault; with error boundary → absorption (incident
  `Resolved`) + remaining instances cancelled + error path routes + workflow completes.
- Determinism: identical runs → identical token/loop-record ids.
- Interchange: import + round-trip for sequential + parallel cardinality; collection-mode / unsupported
  loop-characteristics degraded with a finding.
- Spec 119/120 suites pass unmodified (beyond the mechanical `(NodeId, IterationId)` key rename).
- Full test projects green: BPMN, BPMN Interchange, Activities Runtime, Workflows Runtime, ControlFlow,
  Architecture. Full solution build clean.

## Deviations from the ratified plan

- **D2a collection-mode variable read → collection mode deferred.** The ratified plan required reading a
  container-scoped collection variable's current value at loop start through the context's variable view, and
  authorized stop-and-report if that read was not cleanly possible. It is not: `IRuntimeActivityExecutionContext`
  exposes no variable-value read, and the concrete `SimpleActivityExecutionContext.VariableScope` is threaded
  as `null` into every `BpmnProcess` structural evaluation (verified in
  `WorkflowInvokeActivitySchedulerWorkHandler`, `WorkflowParentActivityCompletionSchedulerWorkHandler`,
  `WorkflowResumeBookmarkSchedulerWorkHandler`). Wiring it would need a new runtime seam beyond the single
  ratified runtime addition, or an improvised reach into `ActivityExecutionState.VariableFrame.Values`
  (re-implementing envelope-unwrap + name→key resolution) — the forbidden side channel. Resolution:
  **cardinality mode is implemented fully; collection mode is authoring-modeled (D1), validated as a stated
  cut (D1 rule 5), and import-degraded (D4)**, deferred to the follow-up unit that adds the variable-read
  seam. The D4 interchange test for collection mode asserts the **degrade** path (not a round-trip), and the
  runtime collection-mode test is a deviation (no executable collection mode this slice).
- **`BpmnActiveChild.IterationId` added (beyond the single named runtime addition).** D2c named
  `RuntimeLiveChildActivity.IterationId` as "the one runtime model addition." Correctly resolving an
  instance's live child aei from the `(NodeId, IterationId)` map also requires knowing which iteration id an
  active child was scheduled with, so `BpmnActiveChild` (BPMN engine state, not a runtime model) carries a
  nullable `IterationId` additively. This is additive BPMN state growth (schema stays v1), explicitly
  permitted, and is not a new runtime seam.
