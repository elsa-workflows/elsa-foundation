# 120 — BPMN boundary events (timer/message/signal listener-child + error absorption) (BPMN Phase 2, events tier, seam-A + seam-B consumer)

## Goal

Let a published BPMN process attach a **boundary event** to a host element (a task-family element or an
embedded `subProcess`) that has a bound child. A `boundaryEvent` reacts to a stimulus *while its host is
running* and, depending on its kind, either interrupts the host or runs a side path alongside it:

- A **timer / message / signal** boundary arms a synthesized **suspending listener child** ALONGSIDE the
  host's bound child (a `Delay` for a timer boundary, a mid-flow `Event` with `CanStartWorkflow = false`
  for a message/signal boundary). The listener token is minted at the boundary element the moment the host
  is scheduled. Three outcomes:
  - the **host** completes first → the host routes normally and its still-armed listeners are torn down
    (token cancelled + the listener's suspended child subtree reclaimed through the spec 112 seam-A
    `RequestChildSubtreeCancellation`);
  - an **interrupting** listener fires first (`cancelActivity = true`, the default) → the host's bound
    child subtree is torn down through seam A, the host token and the sibling listeners are cancelled, and
    the boundary's outbound flows route;
  - a **non-interrupting** listener fires (`cancelActivity = false`) → the boundary's outbound flows route
    while the host and its other listeners keep running (single-shot; no re-arm).
- An **error** boundary has NO listener child. When the host's bound child **faults**, the process
  **absorbs** the fault through the spec 115 seam-B `RequestChildFaultAbsorption` (the named incident
  resolves, the faulted child's subtree is reclaimed), cancels the host token and its sibling catch
  listeners, and routes the error boundary's outbound flows — instead of faulting the whole composite. A
  child fault on a host WITHOUT an error boundary keeps the existing deterministic `bpmn.child.faulted`
  composite fault, byte-for-byte.

This is the **first BPMN consumer of seam B** (fault absorption) and the module's second seam-A consumer
(after the spec 119 event-based-gateway race). Behaviors stay boundary-unaware; the engine owns all
boundary interrupt/absorption semantics, exactly like the spec 119 race lives in the engine, not the
`EventBasedGatewayBehavior`.

## Context (what exists today)

- **Token engine** (`BpmnExecutionEngine`): `StartAsync` / `OnChildCompletedAsync` / `OnChildFaultedAsync`.
  `OnChildCompletedAsync` resolves the completing token (`ResolveTokenId` — `"bpmn.tokenId"` command
  metadata primary, by-node fallback over `state.ActiveChildren`), `RemoveActiveChild`, guards
  terminated/canceled tokens (late-completion absorption), resolves a live event-based-gateway race
  (spec 119) and carries its loser subtree cancellations, then dispatches the element behavior,
  `ApplyDecision`, `Propagate`, and `FinishEvaluation`. `ApplyDecision`'s `ScheduleChild` case flips the
  arriving token to `AwaitingChild` and schedules the element's single `ChildNodeId`; there is **no**
  engine path today that schedules a *second, sibling* child on a freshly minted token.
  `OnChildFaultedAsync` finds the faulted active child by node id, removes it, and **unconditionally**
  returns `Faulted(bpmn.child.faulted)` (its doc comment already says "Error boundary events replace this
  rule in the events tier"). `FinishEvaluation` stages the state, picks the continuation, and — for the
  race — stages the carried seam-A loser cancellations ONLY at the two clean, non-fault return points
  (normal `Complete` and normal `Defer`). `CancelLiveWork` flips every live token to `Canceled` and clears
  `ActiveChildren` — logical only (terminate + pending-fault).
- **State** (`Elsa.Bpmn.ExecutionState`, schema version 1): `BpmnToken` (status ∈
  Active/AwaitingChild/WaitingAtJoin/Consumed/Canceled, plus `ParentTokenId`), `BpmnActiveChild`
  (`NodeId`, `ElementId`, `TokenId`, `SchedulingCause`), `BpmnEventRace` (spec 119). `BpmnStateMutator` is
  the sole mutation home; all record ids derive from `Sequence`; `Canceled` tokens are never pruned; a
  terminal continuation never co-exists with staged child schedules. VERIFIED: N concurrent children each
  on their OWN token round-trip cleanly (`bpmn.tokenId` provenance) — a token-per-listener needs NO
  `BpmnActiveChild` change.
- **Behaviors**: `TaskBehavior`/`SubProcessBehavior` (`OnTokenArrived → ScheduleChild` when a child is
  bound; `OnChildCompleted → SelectTaskFlows → EmitTokens` or `bpmn.flow.none-taken`), `CatchEventBehavior`
  (schedule bound suspending child; route on completion), `EventBasedGatewayBehavior`, the three classic
  gateways. `IBpmnBehaviorContext` exposes a read-only whole-`State` snapshot but **no** mutation/cancel
  authority.
- **Graph** (`BpmnGraph.Validate`): unique ids, resolvable refs, ≥1 start event, per-family child-binding
  rules, single default flow per element, `ValidateEventBasedGateways`, `ValidateAcyclic`.
  `BpmnElementFamilies.Resolve` throws on unknown element types; `BpmnElementTypes` has no `boundaryEvent`;
  `BpmnEventDefinitionTypes.Error = "error"` exists but is inert.
- **Models**: `BpmnElement` has NO attachment fields (`ElementId`, `ElementType`, `Name`, `ChildNodeId`,
  `LaneId`, `DefaultFlowId`, `EventDefinitions`, `Properties`).
- **Seam A** (spec 112, merged): `IRuntimeActivityExecutionContext.RequestChildSubtreeCancellation(
  childActivityExecutionId, reason, metadata?)`. Legal only during a child-completion/child-fault
  evaluation with a `Defer`/`Complete` continuation; terminalizes the target `Cancelled`/`ParentCancelled`,
  cleans its bookmarks/timers, suppresses its non-terminal incidents. A terminal target is a benign skip.
- **Seam B** (spec 115, merged, ZERO BPMN consumers today):
  `IRuntimeActivityExecutionContext.RequestChildFaultAbsorption(incidentId, reason, metadata?)`. Legal
  **only** during a child-fault evaluation with a `Defer`/`Complete` continuation; at most one per
  evaluation; NEVER with a `Fault`/`Cancel` continuation; the request's `incidentId` MUST equal the
  evaluation's incident id (delivered on `ActivityChildFaultedContext.IncidentId`, which is exactly
  `ReadIncidentId(workItem)`); a missing/mismatched incident faults the evaluation. Effect: the spec-112
  planner runs rooted at the faulted child with subStatus `"FaultAbsorbed"` (descendants reclaimed,
  bookmarks/timers cleaned, their incidents `Suppressed`), and the named incident →
  `IncidentStatus.Resolved` + `IncidentResolutionAction.Continue` + `ResolvedAt` + metadata keys
  `FaultAbsorbedBy`/`FaultAbsorptionReason`. The faulted child's own state stays `Faulted`. The absorption
  plan is appended to the seam-A plan list — seams A and B compose in ONE evaluation/commit.
- **The aei-bookkeeping surface (spec 119 D4, reused unchanged).** `RuntimeLiveChildActivity`,
  `IRuntimeLiveChildActivityConsumer` (opt-in marker; `BpmnProcess` already implements it), and
  `IRuntimeActivityExecutionContext.GetLiveChildActivities()` are populated by the runtime for BOTH the
  child-completion AND the child-fault evaluation. The engine already resolves a subtree's live child aei
  from a `BpmnActiveChild.NodeId` through this lookup. **No new runtime seam is introduced by this slice.**

## Design decisions

### D1 — Modeling

- `BpmnElementTypes.BoundaryEvent = "boundaryEvent"`.
- `BpmnElement` gains two **additive** fields (append-only optional constructor channels; the state schema
  stays version 1):
  - `AttachedToRef` (`string?`, `null` except on boundaries) — the host element id.
  - `CancelActivity` (`bool`, default `true` = interrupting; meaningful only on boundaries).
- Two boundary families, distinguished by the single event definition (all share ONE behavior family
  `BpmnElementFamilies.BoundaryEvent`, so one behavior is registered):
  - **boundary CATCH** — exactly one `timer`/`message`/`signal` definition; **REQUIRES** a bound listener
    child (`Delay` for timer, `Event` with `CanStartWorkflow = false` for message/signal). May be
    interrupting or non-interrupting.
  - **boundary ERROR** — exactly one `error` definition; must **NOT** bind a child; must be interrupting
    (`cancelActivity = false` + `error` = validation reject).
- `BpmnElementFamilies.Resolve` maps `boundaryEvent` to the `BoundaryEvent` family after validating that
  the element declares exactly one supported definition (`timer`/`message`/`signal`/`error`); anything
  else is a deterministic reject.

### D2 — Graph validation (new `ValidateBoundaryEvents`, mirroring `ValidateEventBasedGateways`)

Each rule throws a deterministic `BpmnExecutionException` naming the element at validation
(publish/first-execution) time:

1. **`attachedToRef` resolves** to an existing element that is a **task-family OR `subProcess`** element,
   and that host **HAS a bound child** (`ChildNodeId`). A boundary attached to a missing element, a
   non-host family (start/end/gateway/catch), or a childless host is rejected. (Hosting on `subProcess` is
   supported; the runtime arming/absorption is host-family-agnostic. No deviation was needed.)
2. **No inbound flows.** A boundary is entered by its host's activation, never by a sequence flow.
3. **≥1 outbound flow.** A boundary with no outbound path has nowhere to route.
4. **CATCH binds a listener child; ERROR binds none.** A catch boundary with no `ChildNodeId`, or an error
   boundary with one, is rejected.
5. **ERROR must be interrupting.** An error boundary with `cancelActivity = false` is rejected.
6. **≤1 error boundary per host.** Because this slice does no `errorRef` matching (D7), a host may have at
   most one error boundary (otherwise two would both claim the same child fault).
7. **No default flow on a boundary.** A `DefaultFlowId` on a boundary element, or an `IsDefault` outbound
   flow, is rejected (a boundary's outbound is taken unconditionally on fire, not by default-flow
   fallback). Conditional outcome flows on a boundary are permitted but only matched by the (empty)
   fire-time outcome set, so an all-conditional boundary that matches nothing faults `bpmn.flow.none-taken`
   at fire time — a stated, tested edge.

Acyclicity is unchanged. A boundary is never a valid flow *source* for another element's default-flow
purposes beyond its own outbound. An intermediate catch event targeted by an event-based gateway can never
also be a boundary host — it isn't a host-family element, so D2 rule 1 rejects it (falls out of the rule).

### D3 — Arming (engine-owned; behaviors stay boundary-unaware)

`BpmnGraph` precomputes host element id → attached boundary elements. In `ApplyDecision`'s `ScheduleChild`
case, after scheduling the host's bound child on the host token, for **each attached CATCH boundary** the
engine mints a listener token (`NewToken` at the boundary element, `ParentTokenId = host token id`, status
`AwaitingChild`) and schedules that boundary's listener `ChildNodeId` on the listener token (the ordinary
`BpmnScheduler.ScheduleChild` staging + active-child record, one per listener token). **Error** boundaries
mint NO token and schedule NO child at arm time — they are dormant until the host's child faults (D5). No
new state record is added: listener membership is DERIVED from graph attachment + `ParentTokenId` linkage
(a listener token sits at a boundary element whose `AttachedToRef` = the host element and whose
`ParentTokenId` = the host token). This derivation was verified sufficient for every scenario; no additive
state record was needed.

### D4 — Completion semantics (engine-level in `OnChildCompletedAsync`, before behavior dispatch)

Following the spec 119 pattern, the engine resolves boundary semantics **logically first** and **carries**
the seam-A subtree cancellations on the evaluation result; staging happens only at the clean `Complete`/
`Defer` exits in `FinishEvaluation`.

- **HOST child completes** (the completing token sits at a boundary-host element with live listeners):
  find the live listener tokens for that host token, flip each to `Canceled`, drop its active-child
  record, and carry its listener child's seam-A cancellation
  (`bpmn.boundary.superseded-by-host-completion`). The host then routes via its own behavior as normal.
- **INTERRUPTING listener completes** (the completing token sits at a boundary CATCH element with
  `cancelActivity = true`): flip the HOST token to `Canceled`, drop its bound child's active-child record,
  and carry the host child's seam-A cancellation; also cancel the host's OTHER live listener tokens and
  carry their children (`bpmn.boundary.host-interrupted`). The completing listener token then routes on the
  BOUNDARY element's outbound flows via the boundary behavior's `OnChildCompleted` (catch-style task-flow
  selection; `bpmn.flow.none-taken` if nothing matches).
- **NON-INTERRUPTING listener completes** (`cancelActivity = false`): the host and the sibling listeners
  are untouched; only the boundary routes its outbound. Single-shot — no re-arm (BPMN `timeCycle`
  repetition is out of scope, documented).

**Generalization of the spec 119 loser carry.** `EvaluationResult.LoserCancellations` /
`BpmnLoserCancellation` / `StageLoserCancellations` are renamed to the neutral
`PendingSubtreeCancellations` / `BpmnPendingSubtreeCancellation(ActivityExecutionId, ElementId, Reason)` /
`StagePendingSubtreeCancellations`, now carrying a per-item reason so races and boundaries share the same
carry-and-stage plumbing. The existing race reason const
`bpmn.event-based-gateway.superseded-by-first-catch` is kept verbatim; the boundary reason consts
`bpmn.boundary.superseded-by-host-completion`, `bpmn.boundary.host-interrupted`, and (D5)
`bpmn.boundary.error-absorbed` are added. Staging stays gated to the clean `Complete`/`Defer` exits only.

### D5 — Error boundary (seam B, first BPMN consumer)

`OnChildFaultedAsync` is reworked. It resolves the faulted child → its token → its host element (the
`bpmn.tokenId` command-metadata path with the existing by-node-id fallback). If the faulted child's host
element has an attached **ERROR** boundary AND the evaluation carries an incident id
(`ActivityChildFaultedContext.IncidentId`):

1. **Carry** a fault-absorption request `RequestChildFaultAbsorption(evaluationIncidentId,
   bpmn.boundary.error-absorbed)` (staged only at the clean `Complete`/`Defer` exit, like the seam-A
   carries) — the incident id is taken from `ActivityChildFaultedContext.IncidentId`.
2. Flip the host token to `Canceled` and drop its bound child's active-child record. The faulted child's
   own subtree reclaim rides the seam-B absorption plan, so **no** seam-A cancellation is staged for the
   faulted child itself.
3. Cancel the host's sibling CATCH listeners (logical flip + seam-A carry, D4 pattern,
   `bpmn.boundary.host-interrupted`).
4. Mint an `Active` token at the ERROR boundary element and `Propagate`, so the boundary behavior's
   `OnTokenArrived` routes its outbound flows.
5. Return through `FinishEvaluation` (`Defer` or `Complete` — both legal with absorption; **NEVER**
   `Fault`). Both the absorption request and the sibling-listener seam-A cancellations are staged only at
   the clean exits.

If the host has **no** error boundary (or the fault evaluation carries no incident id), the existing
unconditional `bpmn.child.faulted` composite fault is returned **byte-for-byte** — no absorption, no
cancellations staged on that fault path (any sibling listeners fall to logical-only teardown when the
composite faults, matching the spec 119 D3/D5 pending-fault precedent).

**Winner-routing-fault edge (documented, tested).** An error boundary with only conditional outbound flows
that match nothing at fire time faults `bpmn.flow.none-taken`. Because the fault exit skips staging, the
absorption is NOT applied and the composite faults on the routing fault — accepted for this slice, exactly
like the spec 119 winner-routing-fault treatment.

**NO error-code matching this slice.** Any child fault matches the host's single error boundary (that is
why ≤1 error boundary per host, D2 rule 6). A stated cut (D7).

### D6 — Interchange

- **Import.** A `boundaryEvent` is resolved in a second pass (after all host elements are known, so
  attachment resolves regardless of document order):
  - `attachedToRef` missing/unresolvable, or naming a non-host / childless host → **Dropped** (its outbound
    flows cascade-drop as unresolved references). This keeps the importer **validate-representable**: it
    never emits a boundary the graph validator would reject.
  - `cancelActivity` attribute absent → `true` (interrupting).
  - exactly one supported definition: a timer `<timeDuration>` → a `Delay` listener child (reusing the
    spec-118 `BuildDelayCatchChild` + `interval` property; a `<timeCycle>`/`<timeDate>` boundary timer is
    Dropped); a message/signal `messageRef`/`signalRef` → an `Event` listener child (`BuildEventCatchChild`
    + `name` property); an `errorEventDefinition` → an error boundary (pure, no child). An error boundary
    with `cancelActivity = false` → Dropped (validator would reject). `errorRef` is recorded verbatim into
    the element's `Properties` under `"bpmn.errorRef"` for future error-code matching (not read this
    slice) and is not re-emitted.
- **Export.** A dedicated `boundaryEvent` case emits `attachedToRef`, `cancelActivity="false"` only when
  non-interrupting (omitted when interrupting; the importer defaults absent → true, so the round-trip
  holds), and the single event-definition child (`error` → `<errorEventDefinition/>`, timer →
  `<timerEventDefinition><timeDuration>…`, message/signal → the existing `AppendEventDefinition` path). The
  boundary's listener `Delay`/`Event` child is engine detail and is not exported (re-synthesized on
  import). `SynthesizeBounds` sizes a `boundaryEvent` as a `36×36` event.
- **Corpus.** The sample document's `boundary-1` attaches to a childless `userTask`, so it stays **Dropped**
  (now with the specific childless-host finding); the existing severity/id assertion holds.

### D7 — Stated cuts

Escalation / compensation boundaries; error-code (`errorRef`) matching; non-interrupting `timeCycle`
repetition (boundaries are single-shot); event subprocesses; terminate routed through seam-A subtree
teardown (still logical-only). Boundary events on event-based-gateway catch targets fall out of D2 rule 1
(a catch event is not a host-family element).

## In scope (this slice)

- **Element/family/behavior (D1/D3/D4):** `BpmnElementTypes.BoundaryEvent`; `BpmnElement.AttachedToRef` +
  `.CancelActivity`; `BpmnElementFamilies.BoundaryEvent` + `Resolve` case; `BoundaryEventBehavior`; DI
  registration.
- **Validation (D2):** `ValidateBoundaryEvents` in `BpmnGraph.Validate`; host must have a bound child.
- **Engine (D3/D4/D5):** boundary arming in `ApplyDecision`; boundary completion semantics in
  `OnChildCompletedAsync`; error absorption in `OnChildFaultedAsync`; the neutral
  `PendingSubtreeCancellations` carry (renamed from `LoserCancellations`) + the carried `FaultAbsorption`;
  conditional staging of both at the clean `FinishEvaluation` exits; new reason consts.
- **Interchange (D6):** importer `boundaryEvent` second-pass resolution; exporter `boundaryEvent` case +
  `SynthesizeBounds` event branch.
- **Tests + module docs:** validation, interrupting-timer-first, host-completes-first, non-interrupting
  message, error absorption, fault-without-boundary, multiple boundaries per host, determinism,
  interchange import/round-trip. BPMN README + EXTENSION_POINTS; Interchange README. (No runtime
  EXTENSION_POINTS change — no new runtime seam.)

## Out of scope (deferred follow-ups, stated cuts)

- Seam-A teardown on `CancelLiveWork` paths (terminate, pending-fault, error-routing-fault): logical-only.
- Escalation / compensation boundaries; event subprocesses; multi-instance; call activities.
- `errorRef` / error-code matching; non-interrupting `timeCycle` boundary repetition.
- Expression-conditioned flows anywhere (unchanged engine-wide cut).

## Functional requirements

**FR-1 — Element + family.** `BpmnElementFamilies.Resolve` maps a `boundaryEvent` element to the
`BoundaryEvent` family (after validating exactly one `timer`/`message`/`signal`/`error` definition);
`BoundaryEventBehavior` is registered for it. `BpmnElement` carries `AttachedToRef` and `CancelActivity`
additively (schema stays version 1).

**FR-2 — Validation.** `BpmnGraph.Validate` rejects, each with a deterministic `BpmnExecutionException`
naming the element: a boundary whose `attachedToRef` is missing, names a non-host family, or names a
childless host; a boundary with any inbound flow; a boundary with no outbound flow; a catch boundary that
binds no child; an error boundary that binds a child; a non-interrupting error boundary; more than one
error boundary on a host; a default flow declared on or for a boundary.

**FR-3 — Arming.** Scheduling a boundary host with attached CATCH boundaries mints one `AwaitingChild`
listener token per catch boundary (`ParentTokenId` = host token) and schedules its listener child, in
addition to the host's own bound child. Error boundaries arm nothing. Record ids stay a pure function of
`Sequence`.

**FR-4 — Host completes first.** When the host's bound child completes, every live listener token for that
host is flipped `Canceled`, its active-child dropped, and its listener child's subtree torn down through
seam A (`bpmn.boundary.superseded-by-host-completion`) on a non-fault continuation; the host routes
normally.

**FR-5 — Interrupting listener fires first.** When an interrupting catch listener completes, the host
token and the host's other listener tokens are flipped `Canceled`, the host's bound child and the sibling
listeners' children are torn down through seam A (`bpmn.boundary.host-interrupted`), and the boundary's
outbound flows route.

**FR-6 — Non-interrupting listener fires.** When a non-interrupting catch listener completes, the host and
its other listeners are untouched and only the boundary's outbound flows route; the boundary is single-shot
(no re-arm).

**FR-7 — Error absorption.** When the host's bound child faults and the host has an error boundary (and the
evaluation carries an incident id), the engine stages `RequestChildFaultAbsorption(incidentId,
bpmn.boundary.error-absorbed)`, cancels the host token and sibling catch listeners (their children torn
down through seam A), routes the error boundary's outbound flows, and returns `Defer`/`Complete` (never
`Fault`). The named incident resolves (`Resolved`/`Continue`/`ResolvedAt` + `FaultAbsorbedBy`), the faulted
child stays `Faulted`, and the composite completes/continues.

**FR-8 — Fault without an error boundary.** A child fault on a host with no error boundary returns the
existing `bpmn.child.faulted` composite fault unchanged, staging no cancellations/absorption.

**FR-9 — Late/absorbed listener completion.** A listener (or host child) that completes after its token was
cancelled arrives on a `Canceled` token and is absorbed by the token-status guard (no fault, no routing).

**FR-10 — Continuation discipline.** Seam-A subtree cancellations and the seam-B fault absorption are
staged **only** at the clean `Complete`/`Defer` exits of `FinishEvaluation`; every fault/pending-fault/
terminated/deadlock exit skips staging. A `Fault`/`Cancel` continuation never co-exists with a staged
seam-A/seam-B request.

**FR-11 — Determinism.** Identical runs produce identical token/record ids and identical listener-arming
and cancellation order (following `Sequence`-derived id order).

**FR-12 — Interchange.** A timer/message/error `boundaryEvent` attached to a bound host round-trips through
import→export→import with `attachedToRef` and `cancelActivity` fidelity; a boundary attached to a childless
host imports as Dropped with a specific finding; an imported boundary that the validator would reject is
never emitted.

## Invariants that MUST survive

- `Elsa.Bpmn.ExecutionState` stays schema version 1; the only mutation home remains `BpmnStateMutator`; all
  record ids derive from `Sequence`; `Canceled` tokens are never pruned; a terminal continuation never
  co-exists with staged child schedules; a `Fault`/`Cancel` continuation never co-exists with a staged
  seam-A/seam-B request.
- Behaviors stay decision-only and boundary-unaware; boundary interrupt/absorption lives entirely in the
  engine.
- `CancelLiveWork` stays logical-only (terminate + pending-fault + error-routing-fault).
- The runtime live-children read stays opt-in (marker-gated), read-only, spoof-proof, populated only for
  child-completion/child-fault evaluations. No new runtime seam is introduced.
- Deterministic ids only; no wall-clock-derived identity. No new HTTP endpoints; the domain project-tree
  naming guard and VF-ACT gates hold. The spec 119 race tests still pass unmodified except for the
  mechanical rename from the D4 carry generalization.

## Success criteria

- Validation tests: each FR-2 rule rejected deterministically (extends `BpmnGraphValidationTests`).
- Interrupting-timer test: a timer boundary fires (resume the `Delay` bookmark) before the host's bound
  wait — host child `Cancelled`/`ParentCancelled`, its bookmark gone, the boundary path routes to
  completion, host token `Canceled` in `GetBpmnStateAsync()`.
- Host-completes-first test: the host's bound child completes → the listener child `Cancelled`/
  `ParentCancelled`, its bookmark gone, host path routes.
- Non-interrupting message test: a non-interrupting message boundary fires → the boundary path runs AND the
  host still completes normally afterward; both paths reach their ends.
- Error-boundary test: the host's bound child faults → the incident is `Resolved`/`Continue`/`ResolvedAt`
  with `FaultAbsorbedBy`, the faulted child stays `Faulted`, any sibling catch listener is `Cancelled`, the
  error path routes, and the workflow completes. Plus: a fault on a host without an error boundary →
  `bpmn.child.faulted` unchanged.
- Multiple boundaries on one host (interrupting timer + non-interrupting message + error): each kind
  behaves per its rule.
- Determinism: identical runs → identical token/record ids.
- Interchange: import + round-trip for timer/message/error boundaries; `attachedToRef`/`cancelActivity`
  fidelity; childless-host Dropped finding.
- Full test projects green: BPMN, BPMN Interchange, Activities Runtime, Workflows Runtime, Architecture.
  Full solution build clean.
