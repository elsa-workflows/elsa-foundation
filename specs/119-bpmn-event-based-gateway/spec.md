# 119 — BPMN event-based gateway (first-catch-wins race) (BPMN Phase 2, events tier, seam-A consumer)

## Goal

Let a published BPMN process fork a token into a **race** of intermediate catch events and keep only the
first one to fire. An `eventBasedGateway` element receives a token; every outgoing sequence flow targets
an `intermediateCatchEvent`; all of those catch events arm simultaneously (each schedules its bound
suspending child through the spec 116 machinery); the **first** catch whose stimulus fires *wins* and
routes its outbound path, and every **losing** sibling is cancelled — its token ends AND its scheduled
suspending child's runtime activity-execution subtree is cancelled through the spec 112 seam-A
`RequestChildSubtreeCancellation`. This is the **first BPMN consumer of seam A**: the losing catch's
armed child is a real, suspended activity execution whose durable bookmark/timer must be torn down, not
merely a logical token flip. Behaviors stay race-unaware; the engine owns the race semantics.

## Context (what exists today)

- **Token engine** (`BpmnExecutionEngine`): `StartAsync`/`OnChildCompletedAsync`/`OnChildFaultedAsync`.
  `OnChildCompletedAsync` resolves the completing token (`ResolveTokenId` — `"bpmn.tokenId"` command
  metadata primary, by-node fallback over `state.ActiveChildren`), `RemoveActiveChild`, then guards:
  `state.Terminated || token.Status == Canceled` → an absorbed no-op diagnostic (late-completion
  absorption). Otherwise it resolves the element behavior, `ApplyDecision`, then `Propagate`.
  `ApplyDecision` implements the full command vocabulary (`EmitTokens`, `ScheduleChild`, `ConsumeToken`,
  `TerminateProcess`, `Fault`); **there is no cancel-sibling command**. `Propagate` loops `Active` tokens
  through behaviors until quiescent. `FinishEvaluation` stages the state and picks the continuation:
  fault-with-scheduled-children → `Defer` carrying `PendingFault` (which calls `CancelLiveWork`);
  terminate-with-scheduled-children → `Defer`; quiescence → `Complete`; deadlock fault; else `Defer`.
  `CancelLiveWork` flips every live token to `Canceled` and clears `ActiveChildren` — **logical only**
  (never touches runtime subtrees); it is used by terminate and the pending-fault path.
- **State** (`Elsa.Bpmn.ExecutionState`, schema version 1): `BpmnToken` (status ∈
  Active/AwaitingChild/WaitingAtJoin/Consumed/Canceled), `BpmnActiveChild` (`NodeId`, `ElementId`,
  `TokenId`, `SchedulingCause` — **no** activity-execution id field), `BpmnEventRace` (added here).
  `BpmnStateMutator` is the sole mutation home; all record ids derive from `Sequence`; `Canceled` tokens
  are never pruned; a terminal continuation never co-exists with staged child schedules.
- **Behaviors**: `StartEventBehavior`, `CatchEventBehavior` (`OnTokenArrived → ScheduleChild`;
  `OnChildCompleted → SelectTaskFlows → EmitTokens` or `bpmn.flow.none-taken` fault),
  `Exclusive/Parallel/InclusiveGatewayBehavior`. `IBpmnBehaviorContext` exposes a read-only whole-`State`
  snapshot but **no** mutation/cancel authority. `BpmnElementFamilies.Resolve` has no `eventBasedGateway`
  case; `BpmnElementTypes` has no constant.
- **Graph** (`BpmnGraph`): `OutboundFlows`/`InboundFlows`/`GetRequiredElement` lookups; `Validate` runs
  per-family child-binding rules + acyclicity, but **no gateway flow-count/target rules** today.
  `BpmnSequenceFlow` models a condition as `ConditionOutcome` (outcome-matched) and `IsDefault`
  (BPMN default flow); expression conditions are out of this engine slice.
- **Seam A** (spec 112, merged): `IRuntimeActivityExecutionContext.RequestChildSubtreeCancellation(
  childActivityExecutionId, reason, metadata?)`. Legal **only** during a child-completion/child-fault
  evaluation with a `Defer`/`Complete` continuation (`Fault`/`Cancel` + requests = evaluation fault);
  duplicate target = fault; missing/non-child target = fault; a **terminal** target = benign skip (the
  first-completion-wins no-op). Requests are applied atomically with the continuation's commit by
  `WorkflowParentActivityCompletionSchedulerWorkHandler.PlanChildSubtreeCancellationsAsync` via
  `ActivitySubtreeCancellationPlanner` (BFS over `ParentActivityExecutionId`; targets terminalize
  `Cancelled`/`ParentCancelled`; bookmarks/timers cleaned; non-terminal incidents suppressed). The BPMN
  engine does **not** use seam A anywhere today.
- **The aei-bookkeeping gap (resolved in D4).** Seam A needs the losing child's **activity-execution
  id**. BPMN state records armed children by node/token id only, and the child aei is minted by the
  completion handler (`IRuntimeExecutionIdGenerator.NewActivityExecutionId`) **after** the structural
  callback returns — it is never surfaced back to the engine before completion. `IRuntimeActivityExecution
  Context` and `SimpleActivityExecutionContext` carry no live-children lookup. So the engine cannot resolve
  a losing catch's node id → its live child aei from anything that exists today.

## Design decisions

### D1 — Element, family, and validation

`BpmnElementTypes.EventBasedGateway = "eventBasedGateway"`; family
`BpmnElementFamilies.EventBasedGateway = "eventBasedGateway"` (matching the existing gateway family
naming); a `Resolve` case; `EventBasedGatewayBehavior` registered in `ActivitiesBpmnFeature`. Because
`Resolve` runs during `BpmnGraph.Validate`, every rule below throws a deterministic
`BpmnExecutionException` naming the element at validation (publish/first-execution) time:

1. **≥2 outbound flows.** An event-based gateway with fewer than two outbound flows is rejected (a race
   needs at least two members).
2. **Every outbound targets a catch event with exactly one inbound flow.** Each outbound flow's
   `TargetRef` must be an `intermediateCatchEvent` element whose inbound-flow count is exactly `1` (the
   gateway is that catch's only entry — a catch that can also be reached another way is not a pure race
   member).
3. **Binds no child.** An event-based gateway is engine-interpreted; a `ChildNodeId` binding is rejected
   (parallel-gateway parity).
4. **No conditions or default on the outbound flows.** Every outbound flow must carry no
   `ConditionOutcome` and must not be `IsDefault`, and the gateway element must declare no `DefaultFlowId`.
   BPMN forbids conditional/default flows out of an event-based gateway (the race is decided by stimulus
   arrival, not by a condition), so this is **rejected deterministically** rather than silently ignored —
   surfacing an authoring error instead of masking it.

### D2 — Race semantics (engine-level; behaviors stay race-unaware)

`EventBasedGatewayBehavior.OnTokenArrived` emits tokens on **every** outbound flow (parallel-split style);
the catch elements then arm exactly as spec 116 already does (`CatchEventBehavior` unchanged). The race is
recorded and resolved entirely by the engine — **no new behavior command kind**:

- **Recording.** When `ApplyDecision` applies the gateway's `EmitTokens`, the member tokens are the tokens
  it mints from the gateway's outbound flows. The engine appends an additive `BpmnEventRace(RaceId,
  GatewayElementId, MemberTokenIds, Resolved=false)` to `BpmnExecutionState.Races` via `BpmnStateMutator`
  (`RaceId` derived from `Sequence`). This is additive payload growth: **state schema stays version 1**.
- **Resolving.** `OnChildCompletedAsync`, immediately after `RemoveActiveChild` and the
  terminated/canceled guard and **before** dispatching the completing element's behavior, checks whether
  the completing token is a member of a live (unresolved) race. If so, the completing token is the
  **winner**; the engine (a) marks the race resolved, (b) flips every **losing** member token to
  `Canceled` via `BpmnStateMutator`, (c) removes each loser from `ActiveChildren`, and (d) computes the
  seam-A cancellation intents for each loser's armed child (see D3/D4). It then lets
  `CatchEventBehavior.OnChildCompleted` route the winner as normal.
- **Late completion.** A losing sibling that completes after the race resolved arrives on a `Canceled`
  token and is absorbed by the existing token-status guard (an absorbed no-op diagnostic, no fault, no
  routing) — asserted by test.

### D3 — Continuation discipline (the seam-112 legality contract)

Seam-A staging is legal only with a `Defer`/`Complete` continuation; pairing it with a `Fault`/`Cancel`
continuation faults the evaluation. The winner's routing can fault (e.g. `bpmn.flow.none-taken` when the
winner catch has no matching outbound flow), so the engine must **not** have already staged cancellations
when that happens. Chosen shape:

- The race is resolved **logically** first (loser tokens flipped `Canceled`, loser `ActiveChildren`
  removed, race marked resolved) — pure, safe state mutation regardless of the winner's outcome. The
  per-loser seam-A cancellation intents are **computed and carried on the evaluation result, not yet
  staged**.
- The winner behavior is then dispatched and propagated. `FinishEvaluation` stages the carried loser
  cancellations onto the context (`RequestChildSubtreeCancellation`) **only** at the two clean, non-fault
  return points (normal `Complete` and normal `Defer`). Every fault/pending-fault/terminated/deadlock
  return point **skips** staging.
- **Winner-routing-faults path (documented, tested).** When the winner routing faults, staging is skipped.
  If the fault is raised with child schedules pending in the same evaluation it flows through the
  pending-fault `Defer` path, whose `CancelLiveWork` **logically** cancels the losers (already `Canceled`
  here); otherwise it faults directly. Either way, **the losers' runtime subtrees are handled
  logically-only** on the fault path — their suspended children are abandoned and their late completions
  are absorbed by the token-status guard, exactly matching `CancelLiveWork`'s existing terminate/pending-
  fault precedent. This is **accepted** for this slice: seam-A teardown of loser subtrees is a best-effort
  optimization on the happy path, and a faulting process is already tearing itself down.

### D4 — The aei-bookkeeping gap: resolution (investigated first; highest workable option chosen)

**Option 3 (capture the child aei into `BpmnActiveChild`) — rejected as unviable.** The child aei is
generated by the completion handler *after* the structural callback returns; no callback or state carries
it back to the engine before the child completes, so there is no point at which the engine could capture a
*losing* (still-suspended) sibling's aei. Verified against `BpmnScheduler.ScheduleChild` /
`WorkflowParentActivityCompletionSchedulerWorkHandler.NewChildActivityScheduleWorkItems`.

**Option 1 (runtime already exposes a live-children lookup) — rejected as absent.**
`SimpleActivityExecutionContext` is constructed with no child-execution data;
`IRuntimeActivityExecutionContext`, `ActivityChildCompletedContext`, and `ActivityExecutionState` expose
only the parent's own state and the *completing* child's aei — never the parent's other live children.

**Option 2 (minimal, opt-in runtime extension) — chosen.** The completion handler already loads the whole
activity-execution set (`ListAllAsync`) when it plans staged cancellations, and a parent-scoped read
(`IActivityExecutionStateStore.ListAllByParentAsync`) already exists for "a composite's direct children"
(the Parallel fork/join uses it). This slice surfaces that read to the structural callback, opt-in and
spoof-proof:

- New model `RuntimeLiveChildActivity(ActivityExecutionId, ExecutableNodeId, Status)`.
- New **opt-in marker** `IRuntimeLiveChildActivityConsumer` (engine-only, alongside
  `IRuntimeStructuralActivity`). Only a structural activity that implements it pays the extra read.
- New member `IRuntimeActivityExecutionContext.GetLiveChildActivities()` returning the parent's **direct,
  non-terminal** child executions (each `ActivityExecutionId`+`ExecutableNodeId`+`Status`). Populated
  **only** during a child-completion/child-fault evaluation and **only** when the parent activity is an
  `IRuntimeLiveChildActivityConsumer`; empty otherwise (initial structural execution, resume, non-consumer
  parents). Loaded from committed activity-execution state via `ListAllByParentAsync` (read-only,
  not user input).
- `BpmnProcess` implements `IRuntimeLiveChildActivityConsumer`; the engine resolves each loser's
  `BpmnActiveChild.NodeId` → live child aei through this lookup (node ids are unique among a process's
  direct live children — each catch binds a distinct child, validation-enforced). A loser whose child is
  **not** in the live lookup (already terminal / completing) is a benign skip: its token is still flipped
  `Canceled` (absorbing its late completion), only the seam-A stage is omitted — the same terminal-target
  no-op seam 112 already models.

The public-API compatibility guard only asserts specific *absent* members, so the additive
`GetLiveChildActivities` member needs no baseline edit. `SimpleActivityExecutionContext` is the sole
implementer of the interface; the new member is added there with an optional trailing constructor channel
(empty default), so existing construction sites are unaffected.

### D5 — Terminate / fault interplay (logical-only, unchanged precedent)

`CancelLiveWork` stays **logical-only** for terminate and pending-fault, exactly as today. A live race hit
by a terminate end event: its member tokens are flipped `Canceled` logically, the race dies, and **no**
seam-A cancellation is staged for its armed children (their late completions are absorbed). Routing
terminate (or any `CancelLiveWork` path) through seam-A subtree teardown is **explicitly out of scope**
and noted as a future follow-up. D3's winner-routing-faults path is the same logical-only treatment.

### D6 — Interchange (minimal, included)

- **Import.** `BpmnXmlNames.GatewayLocalNamesToElementTypes` gains `eventBasedGateway → eventBasedGateway`,
  so the importer's existing gateway branch maps it to the new element type (a plain unbound gateway —
  event-based gateways carry no event definitions to parse). Graph validation applies on the analyze/commit
  path through `BpmnGraph.From`, so an event-based gateway whose outbound targets a non-catch element is
  rejected deterministically once bridged — covered by an importer/analyze test.
- **Export.** The exporter's `_ => new XElement(Model + element.ElementType)` local-name fallback already
  emits `<eventBasedGateway>`, and its DI-bounds synthesis already sizes any type ending in `Gateway` at
  50×50 — **both verified to hold** for `eventBasedGateway`; a round-trip test proves it.

## In scope (this slice)

- **Runtime seam (D4):** `RuntimeLiveChildActivity`; `IRuntimeLiveChildActivityConsumer`;
  `IRuntimeActivityExecutionContext.GetLiveChildActivities()`; `SimpleActivityExecutionContext` support;
  the completion handler's opt-in parent-scoped live-children pre-load. Runtime EXTENSION_POINTS updated.
- **Element/family/behavior (D1/D2):** `BpmnElementTypes.EventBasedGateway`; `BpmnElementFamilies`
  constant + `Resolve` case; `EventBasedGatewayBehavior`; DI registration.
- **Validation (D1):** the four event-based-gateway graph rules in `BpmnGraph.Validate`.
- **State (D2):** `BpmnEventRace`; `BpmnExecutionState.Races` (additive, schema v1);
  `BpmnStateMutator.AddRace`/`MarkRaceResolved`.
- **Engine (D2/D3/D4/D5):** race recording on the gateway `EmitTokens`; race resolution in
  `OnChildCompletedAsync` (logical loser cancel + carried seam-A intents); conditional seam-A staging in
  `FinishEvaluation`; `BpmnProcess` implements the consumer marker.
- **Interchange (D6):** importer gateway-map entry; round-trip + invalid-target analyze tests.
- **Tests + module docs:** validation, race-win, 3-member race, late completion, winner-routing-fault,
  terminate-with-live-race, determinism, interchange. BPMN README + EXTENSION_POINTS; runtime
  EXTENSION_POINTS.

## Out of scope (deferred follow-ups, stated cuts)

- **Seam-A teardown on `CancelLiveWork` paths** (terminate, pending-fault, winner-routing-fault): loser
  subtrees are logical-only there (D3/D5). Routing those through seam A is a later unit.
- **Boundary events, event subprocesses, multi-instance, message correlation subsystem** (later Phase 2
  units). The event-based gateway does not itself introduce boundary/interrupt semantics.
- **Expression-conditioned flows** anywhere (unchanged engine-wide cut).
- **Instantiating (start) event-based gateway** — the BPMN "receive-task/gateway-initiated" instantiation
  pattern where the gateway itself starts the process; only the mid-flow gateway is in scope.
- **Interchange authoring of event definitions** beyond the existing spec 118 surface (unchanged).

## Functional requirements

**FR-1 — Element + family.** `BpmnElementFamilies.Resolve` maps an `eventBasedGateway` element to the
`eventBasedGateway` family; `EventBasedGatewayBehavior` is registered for it.

**FR-2 — Validation.** `BpmnGraph.Validate` rejects, each with a deterministic `BpmnExecutionException`
naming the element: an event-based gateway with <2 outbound flows; an outbound flow whose target is not an
`intermediateCatchEvent`; an outbound-target catch whose inbound-flow count ≠ 1; a `ChildNodeId` binding;
any outbound flow carrying a `ConditionOutcome` or `IsDefault`, or a `DefaultFlowId` on the gateway.

**FR-3 — Fan-out.** `EventBasedGatewayBehavior.OnTokenArrived` emits one token per outbound flow; each
target catch arms via the unchanged spec 116 `CatchEventBehavior`. Record ids stay a pure function of
`Sequence`.

**FR-4 — Race record.** Applying the gateway `EmitTokens` appends one `BpmnEventRace` whose
`MemberTokenIds` are exactly the tokens minted from the gateway's outbound flows; `RaceId` derives from
`Sequence`; the state schema stays version 1.

**FR-5 — First-catch-wins.** When a race member token's child completes, the engine marks the race
resolved, flips every other member token to `Canceled`, removes those losers from `ActiveChildren`, and
routes the winner through `CatchEventBehavior` unchanged.

**FR-6 — Loser subtree cancellation (seam A).** For each loser whose armed child is a live direct child of
the process, the engine stages `RequestChildSubtreeCancellation(loserChildAei, reason, metadata)` — but
only on a non-fault (`Complete`/`Defer`) winner continuation (D3). The loser child terminalizes
`Cancelled`/`ParentCancelled` and its bookmark/timer is cleaned in the winner's commit. A loser whose
child is not live is a benign skip (token still `Canceled`).

**FR-7 — aei resolution (D4).** The engine resolves each loser's child aei from
`IRuntimeActivityExecutionContext.GetLiveChildActivities()`, populated by the runtime for the
`IRuntimeLiveChildActivityConsumer` parent (`BpmnProcess`) during the child-completion evaluation, keyed by
executable node id.

**FR-8 — Late/absorbed completion.** A loser that completes after cancellation arrives on a `Canceled`
token and is absorbed by the token-status guard (no fault, no routing, process outcome unchanged).

**FR-9 — Winner-routing-fault (D3).** If the winner routing faults (`bpmn.flow.none-taken`), no seam-A
cancellation is staged in that evaluation; the fault surfaces per the existing fault/pending-fault path and
the losers are logically-only cancelled.

**FR-10 — Terminate with a live race (D5).** A terminate end event reached while a race is live logically
cancels the member tokens (via `CancelLiveWork`), kills the race, stages no seam-A request, and completes
the process.

**FR-11 — Determinism.** Identical runs produce identical token/race/record ids and identical resolution
order (loser cancellation order follows `MemberTokenIds` order, itself derived from `Sequence`).

**FR-12 — Interchange.** An `eventBasedGateway` round-trips through import→export; an imported document
whose event-based gateway targets a non-catch element surfaces the deterministic validation rejection when
bridged through `BpmnGraph.From`.

## Invariants that MUST survive

- `Elsa.Bpmn.ExecutionState` stays schema version 1; the only mutation home remains `BpmnStateMutator`;
  all record ids (tokens, diagnostics, **races**) derive from `Sequence`; `Canceled` tokens are never
  pruned; a terminal continuation never co-exists with staged child schedules; **a `Fault`/`Cancel`
  continuation never co-exists with a staged seam-A cancellation** (D3).
- Behaviors stay decision-only and race-unaware; the race lives entirely in the engine.
- `CancelLiveWork` stays logical-only (terminate + pending-fault + winner-routing-fault).
- The runtime live-children read is opt-in (marker-gated), read-only, spoof-proof, and populated only for
  child-completion/child-fault evaluations — no cost imposed on non-consumer structural activities.
- Deterministic ids only; no wall-clock-derived identity. No new HTTP endpoints; the domain project-tree
  naming guard and VF-ACT gates hold.

## Success criteria

- Validation tests: each FR-2 rule rejected deterministically (extends `BpmnGraphValidationTests`).
- Race-win test: gateway → message catch + timer catch; resume the message bookmark → winner path runs to
  completion; loser token `Canceled` in `GetBpmnStateAsync()`; loser child `Cancelled`/`ParentCancelled`
  via `run.State(...)`; loser bookmark **gone** from `BookmarksAsync()`; cancellation reason metadata
  present.
- 3-member race: one winner, two losers, both losers cancelled (token + subtree + bookmark).
- Late-completion test: a loser completes after cancellation → absorbed no-op, no fault, unchanged outcome.
- Winner-routing-fault test: a winner catch with no matching outbound flow faults `bpmn.flow.none-taken`;
  no seam-A evaluation fault; documented D3 behavior holds.
- Terminate-with-live-race test: logical cancel only, process completes, no seam-A fault.
- Determinism: token/race/record ids stable across identical runs.
- Interchange: `eventBasedGateway` round-trip; invalid-target analyze/bridge rejection.
- Full test projects green: BPMN, BPMN Interchange, Activities Runtime, Workflows Runtime, Architecture.
  Full solution build clean.
