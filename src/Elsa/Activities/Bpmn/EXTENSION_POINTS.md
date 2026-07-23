# Elsa.Activities.Bpmn Extension Points

## Scoped execution seam

`BpmnExecutionEngine` is the activity-owned scoped execution seam. It owns BPMN runtime state
mutation, child scheduling metadata, token propagation, join accounting, diagnostics, and deferred
composite completion. Its durable snapshot is staged as one typed, versioned structural
private-state document; it does not patch the activity metadata bag.

The token model is intentionally not an extension point directly: custom element semantics cross the
public behavior contract below, and `BpmnExecutionEngine` remains the authority that validates and
applies behavior commands.

## Implementable contributor interfaces

### `IBpmnElementBehavior`

- **Kind:** Contributor (element behavior provider)
- **Contract:** `Elsa.Activities.Bpmn.Contracts.IBpmnElementBehavior`
- **Behavior contract:** behaviors receive `IBpmnBehaviorContext` and return `BpmnBehaviorDecision`
  commands for `BpmnExecutionEngine` to validate and apply.
- **Registration:** Register one or more implementations with DI as `IBpmnElementBehavior`.
- **Aggregation:** `IBpmnBehaviorRegistry` resolves all registered behavior implementations by
  stable element family (`BpmnElementFamilies`).
- **Selection:** `BpmnElementFamilies.Resolve` maps a `BpmnElement` (element type + event
  definitions) to its behavior family.
- **Decision boundary:** Behaviors receive `IBpmnBehaviorContext`, which exposes read-only
  element/flow/state/trigger information. Behaviors return `BpmnBehaviorDecision` commands;
  `BpmnExecutionEngine` validates and applies those commands.

Known implementations:

- `StartEventBehavior` *(intra-domain — default; registered once per start family —
  `startEvent.none`/`startEvent.timer`/`startEvent.message`/`startEvent.signal`, spec 117 — all emit the
  arriving token onto every outbound flow; event-defined starts differ only in how the instance is started)*
- `NoneEndEventBehavior` *(intra-domain — default)*
- `TerminateEndEventBehavior` *(intra-domain — default)*
- `CatchEventBehavior` *(intra-domain — default; timer/message/signal intermediate catch events,
  spec 116 — schedules the element's bound suspending child and routes on its resumed completion)*
- `TaskBehavior` *(intra-domain — default)*
- `SubProcessBehavior` *(intra-domain — default)*
- `ExclusiveGatewayBehavior` *(intra-domain — default)*
- `ParallelGatewayBehavior` *(intra-domain — default)*
- `InclusiveGatewayBehavior` *(intra-domain — default)*
- `EventBasedGatewayBehavior` *(intra-domain — default; event-based gateway, spec 119 — emits one token
  per outbound flow to arm the racing catch events; the first-catch-wins race resolution and losing-sibling
  cancellation are owned by `BpmnExecutionEngine`, not this behavior)*
- `BoundaryEventBehavior` *(intra-domain — default; boundary events, spec 120 — routes the boundary's
  outbound flows when it fires (a catch listener's completion or an error boundary's engine-minted token);
  the interrupt/absorption semantics — host/listener teardown via seam A and error-fault absorption via
  seam B — are owned by `BpmnExecutionEngine`, not this behavior)*
- `CompensationThrowEventBehavior` / `CompensationEndEventBehavior` *(intra-domain — default; compensation,
  spec 124 — a compensate intermediate throw / compensate end event emits a single `TriggerCompensation`
  command and nothing else; target selection, atomic claiming, sequential handler replay, run-coordinator
  cascade, and the throw's route/consume on completion are all owned by `BpmnExecutionEngine`, not these
  behaviors)*
- `CancelEndEventBehavior` *(intra-domain — default; transactions, spec 125 — a cancel end event emits a
  single `CancelTransaction` command and nothing else; stopping other live work, claiming the scope's
  compensables, the spec-124 replay, and the `Cancelled` completion are all owned by `BpmnExecutionEngine`,
  not this behavior)*
- `EscalationThrowEventBehavior` / `EscalationEndEventBehavior` *(intra-domain — default; escalation, spec 127
  — an escalation intermediate throw / escalation end event emits `[RaiseEscalation, EmitTokens|ConsumeToken]`;
  reading the escalation code from the element, seam-C staging (or the root no-op), and all boundary
  matching/firing/bubbling/late-race handling are owned by `BpmnExecutionEngine`, not these behaviors)*
- **Compensation (spec 124)** adds one command kind — `BpmnBehaviorCommandKind.TriggerCompensation` — and no
  new token status or state-schema break. A **compensation boundary** (`BpmnEventDefinitionTypes.Compensation`
  on a `boundaryEvent`, dormant like an error boundary) names its handler via
  `BpmnElement.CompensationHandlerElementId`; a **handler** is a task-family/`subProcess` element marked
  `BpmnElement.IsForCompensation` that takes no flows. The engine registers a successful host completion in
  the additive reverse-order log `BpmnExecutionState.Compensables` (`BpmnCompensable`, `comp:N`) inside
  `ApplyBoundaryCompletionSemantics` Case B; on `TriggerCompensation` it claims the target `Registered`
  compensables (all, or an `activityRef` host's) newest-first, opens a `BpmnExecutionState.CompensationRuns`
  record (`BpmnCompensationRun`, `comprun:N`) on the throw token (`AwaitingChild` coordinator), and replays
  handlers one sub-token at a time — each handler completion is intercepted before behavior dispatch. The
  run-coordinator cascade in `CancelTokenAndChild` (reason `bpmn.compensation.run-cancelled`) cancels a live
  handler sub-token, drops the run, and releases its unrun `Claimed` compensables back to `Registered`.
  Compensables are never pruned (parallel to `Canceled` tokens). All of it lives in the engine; behaviors stay
  semantics-unaware.
- **Transactions (spec 125)** add one command kind — `BpmnBehaviorCommandKind.CancelTransaction` — one
  additive state flag `BpmnExecutionState.Cancelling` (parallel to `Terminated`), and **no** new token status
  or state-record type (the spec-124 `BpmnCompensationRun` is reused). `BpmnElement.IsTransaction` marks a
  transaction `subProcess`; `BpmnStructure.IsTransaction` marks the nested process. `BpmnEventDefinitionTypes.
  Cancel` on an `endEvent` inside a transaction is the **cancel end** (family `EndEventCancel`); on a
  `boundaryEvent` attached to a transaction host it is a **cancel boundary** (dormant, no listener, ≥1
  outbound). On `CancelTransaction` the engine stops all other live work through `CancelTokenAndChild`
  (logical-only, reason `bpmn.transaction.cancel-stopped-live-work`), sets `Cancelling`, claims every
  `Registered` compensable and opens a `BpmnCompensationRun` coordinated by the cancel-end token, then
  completes with the `Cancelled` outcome (`FinishEvaluation` stages `Complete("Cancelled")` when `Cancelling`
  is set and nothing is live; `HandleCompensationHandlerCompletion` grows a third tail branch for a cancel-end
  coordinator). In the parent scope, a transaction child completing `Cancelled` is intercepted in
  `OnChildCompletedAsync` before Case B / behavior dispatch — the host token is consumed, no compensable
  registers, no normal outbound routes, catch listeners tear down, and an `Active` token is minted at the
  attached cancel boundary (error-boundary minting pattern); no cancel boundary faults
  `bpmn.transaction.cancelled-unhandled`. All of it lives in the engine; behaviors stay semantics-unaware.
  The one touch outside the module is the structure-dependent `Cancelled` outcome declaration (see below).
- **Escalation (spec 127)** adds one command kind — `BpmnBehaviorCommandKind.RaiseEscalation` — and **no** new
  token status or state-record type; it is the module's first consumer of **runtime seam C** (spec 126).
  `BpmnEventDefinitionProperties.Code` carries the matching key on an escalation throw/end (required) and
  escalation boundary (optional = catch-all). `EscalationThrowEventBehavior` / `EscalationEndEventBehavior`
  emit `[RaiseEscalation, EmitTokens|ConsumeToken]`; on `RaiseEscalation` the engine stages
  `context.RequestParentNotification(BpmnExecutionEngine.EscalationNotificationCode = "bpmn.escalation", { code,
  name? })` when the process has a committed parent (`ActivityExecutionState.ParentActivityExecutionId`), else a
  root `EscalationUnhandled` no-op. `BpmnProcess : IRuntimeActivityChildNotificationHandler`;
  `OnChildNotifiedAsync` delegates to `BpmnExecutionEngine.OnChildNotifiedAsync`, which resolves the host
  element via `BpmnGraph.FindElementByChildNodeId`, matches the payload code against
  `BpmnGraph.AttachedEscalationBoundaries` (exact beats catch-all), and fires: non-interrupting mints an
  `Active` boundary token alongside the untouched host; interrupting cancels the host token (or the MI
  coordinator) through the reused `CancelTokenAndChild` cascade (reason
  `BpmnExecutionEngine.EscalationHostInterruptedReason = "bpmn.escalation.host-interrupted"`, seam-A staged from
  the live-children projection) before minting. Unmatched → bubble (a carried `BpmnPendingParentNotification`
  re-staged via `RequestParentNotification` at the clean exit) or a root `EscalationUnhandled` no-op; late races
  fire non-interrupting boundaries and `EscalationLate`-no-op interrupting ones; any other seam-C code is a
  diagnostic pass-through. Four additive diagnostic kinds
  (`EscalationRaised`/`EscalationCaught`/`EscalationUnhandled`/`EscalationLate`). Escalation boundaries are
  validated (`ValidateEscalation`): dormant, subprocess host only, distinct codes + ≤1 catch-all per host. All
  of it lives in the engine; behaviors stay decision-only.
- **Event subprocesses (spec 128, tier 1)** add **no** behavior, command kind, token status, or state record — a
  flow-less `BpmnElement.TriggeredByEvent` subprocess is a **graph-derived, dormant** catcher indexed on
  `BpmnGraph.EventSubprocesses` (a `BpmnEventSubprocessCatcher` per element: trigger kind + code + interrupting +
  body start element id). `ValidateEventSubprocesses` reads each body's authored structure (the way MI reads
  `BpmnStructure.Variables`) and enforces the D1 rules. Two additive `StartEventBehavior` families
  (`StartEventEscalation`/`StartEventError`) route an event-subprocess body start like a none start; the publish
  trigger provider skips them (`BpmnElementFamilies.IsExternalStartTrigger`). **Scheduled-start seeding** extends
  `BpmnScheduler.ScheduleChild` with an optional `startElementId` forwarded as the command-metadata hint
  `BpmnStartTrigger.StartElementIdMetadataKey`, gated in `BpmnProcess.StartAsync`'s third seeding path on the
  `BpmnExecutionEngine.EventSubprocessBodySchedulingCause` so an inherited hint can never contaminate an ordinary
  nested process (a bad hint faults `bpmn.start.unresolved-hint`). Activation is engine-owned
  (`BpmnExecutionEngine.ActivateEventSubprocess`): mint a scope-level activation token, schedule the body with the
  hint, and — when interrupting — stop all other live work through the shared
  `BpmnExecutionEngine.StopOtherLiveWork` helper (extracted from the spec-125 cancel-transaction stop-others loop;
  reason `BpmnExecutionEngine.EventSubprocessScopeInterruptedReason`). `RaiseEscalation` gains the own-scope check
  (returns the matched catcher instead of staging upward); `OnChildNotifiedAsync` gains the specificity ladder
  (`BpmnGraph.EscalationEventSubprocessExact`/`EscalationCatchAllEventSubprocess`); `OnChildFaultedAsync` gains
  the scope error catcher (`BpmnGraph.ErrorEventSubprocess`, seam-B absorption then interrupting activation). Body
  completion is intercepted before behavior dispatch (`TriggeredByEvent`); two additive diagnostic kinds
  (`EventSubprocessActivated`/`EventSubprocessCompleted`). `ResolveTokenId` was hardened so a nested
  BpmnProcess body's leaked inner `bpmn.tokenId` on an inline completion is not mistaken for the parent's
  activation token. Behaviors stay decision-only. See the README for the error-trigger runtime seam-B limitation.
- **Multi-instance loops (spec 121)** add no behavior: a `BpmnElement.LoopCharacteristics`
  (`BpmnLoopCharacteristics`) turns a task/subprocess host's `ScheduleChild` decision into a loop the
  engine owns entirely — a coordinator token plus private per-instance sub-tokens, each scheduled through
  the runtime iteration-frame seam (`ScheduleChildActivity(..., iterationFrame)`) seeding `loopIndex`. The
  loop record `BpmnLoopState` and the instance-completion advance / last-completion routing / cascade
  cancellation all live in `BpmnExecutionEngine`. **Collection mode (spec 123)** resolves `N` from a declared
  container-scoped collection variable read **once** at loop start through the runtime scoped-variable read
  seam (`BpmnProcess : IRuntimeScopedVariableReader`; `context.TryReadScopedVariableValue`); the snapshot
  persists on the additive `BpmnLoopState.Items` and each iteration frame also seeds the item under
  `ItemVariable`. Null/absent → `N == 0` (immediate route); a non-array/external/unreadable value faults
  deterministically (`bpmn.loop.collection-not-a-collection` / `-not-inline` / `-unreadable`). All of it lives
  in the engine; behaviors stay semantics-unaware.
- **Cyclic sequence flows (spec 122)** add no behavior: `BpmnGraph` precomputes the backward (loop-back)
  flow set (`IsBackwardFlow`, the standard DFS back edge from the ordinal-sorted start-event roots) and
  `BpmnToken` carries an additive `IterationKey`. The engine mints a fresh key
  (`BpmnStateMutator.NewIterationKey`) only when `EmitTokens` traverses a backward flow; every other
  minting site inherits its source/parent/group token's key. `BpmnTokenCoordinator` groups
  `WaitingAtJoin` arrivals by `(element, iteration key)`. `ValidateAcyclic` is removed; the remaining
  structural rules constrain where a loop-back may land. State schema stays version 1 (additive).

## Activity-owned structure contracts

This module also exposes these activity-owned contracts:

- `Bpmn.Activities` child slot
- `elsa.bpmn.structure` structure payload with schema version `1.0.0`
- `BpmnStructure.Elements` containing `BpmnElement[]` (each element optionally carries `AttachedToRef`/
  `CancelActivity` for boundary events, spec 120; `LoopCharacteristics` — `BpmnLoopCharacteristics`,
  cardinality XOR collection — for multi-instance loops, spec 121/123; `IsForCompensation`/
  `CompensationHandlerElementId` for compensation, spec 124; and `IsTransaction` for a transaction
  subprocess, spec 125; all additive, state schema stays version 1, including the collection-mode
  `BpmnLoopState.Items` snapshot)
- `BpmnStructure.IsTransaction` (spec 125) — marks the nested process a transaction; drives cancel-end
  validation and the structure-dependent `Cancelled` outcome declaration (see below). Additive.
- `BpmnStructure.SequenceFlows` containing `BpmnSequenceFlow[]`
- `BpmnAuthoredStructure.Pools` / `BpmnAuthoredStructure.Lanes` (authored/designer-side only)
- `BpmnAuthoredStructure.Diagram` opaque BPMN-DI-shaped layout document (authored-side only,
  stripped at compile time)
- `BpmnAuthoredStructure.Variables` optional container-scoped variable declarations (ADR 0027)

## Consumed runtime contracts

`BpmnProcess` implements the engine-only structural execution protocol
(`Elsa.Workflows.Runtime.Core.Contracts`):

- `IRuntimeStructuralActivity` — builds and validates the BPMN graph, emits start-event tokens,
  propagates them, and returns a `RuntimeStructuralContinuation`.
- `IRuntimeActivityChildCompletionHandler` — invoked when a bound child completes; routes through
  `BpmnExecutionEngine.OnChildCompletedAsync` to select outbound flows and continue propagation.
- `IRuntimeActivityChildFaultHandler` — invoked when a child faults. When the faulted child's host has an
  attached **error boundary** (spec 120), the process absorbs the fault through the spec 115 seam-B
  `RequestChildFaultAbsorption` (the named incident resolves, the faulted child's subtree is reclaimed) and
  routes the error boundary's outbound flows instead of faulting; otherwise the composite faults
  deterministically (`bpmn.child.faulted`) instead of hanging a join.
- `IRuntimeLiveChildActivityConsumer` (spec 119) — opt-in marker that makes the runtime populate
  `IRuntimeActivityExecutionContext.GetLiveChildActivities()` for the child-completion/child-fault
  callback. `BpmnExecutionEngine` uses it to resolve a subtree's node id to its live child
  activity-execution id before staging that subtree's seam-A cancellation
  (`RequestChildSubtreeCancellation`, spec 112) — for a losing event-based-gateway catch (spec 119), for
  a boundary event's torn-down host/listener subtrees (spec 120), and for a multi-instance host's live
  instances (spec 121). The lookup is keyed by `(executable node id, iteration id)` so N concurrent
  same-node multi-instance instances resolve distinctly (`RuntimeLiveChildActivity.IterationId`); ordinary
  single-run children key under a `null` iteration id. Cancellation reasons:
  `bpmn.event-based-gateway.superseded-by-first-catch`, `bpmn.boundary.superseded-by-host-completion`,
  `bpmn.boundary.host-interrupted` (also used for a multi-instance coordinator's cascade); the seam-B
  error-boundary absorption reason is `bpmn.boundary.error-absorbed`. Seam-A cancellations and the seam-B
  absorption are staged only on a clean (`Defer`/`Complete`) continuation.
- **Iteration-frame seam (spec 121)** — `BpmnScheduler.ScheduleChild` passes a `LoopIterationScopeRequest`
  on `ScheduleChildActivity` for multi-instance instances (owner = the `BpmnProcess` node, iteration id
  minted deterministically from the Sequence-based instance token id, `loopIndex` value), scheduling with
  the process's own aei so the runtime iteration-frame ownership guard holds.

## Publish-time start-trigger surface (spec 117)

`BpmnProcess` is a `[TriggerActivity]`, so the publish compiler marks its node `executionType=Trigger` and
the runtime trigger seams index its event-defined start events at publish time. Two providers, both
registered in `ActivitiesBpmnFeature`, read only the pinned published node's BPMN structure:

- `BpmnProcessTriggerStimulusProvider` implements `IActivityTriggerStimulusProvider`
  (`Elsa.Workflows.Runtime.Core`) — one `TriggerStimulusDescriptor` per event-defined start element
  (message/signal via `BpmnMessageStartStimulus`, timer via `BpmnTimerStartStimulus`), each carrying the
  start element id in `Metadata` under `BpmnStartTrigger.StartElementIdMetadataKey` (`"bpmn.startElementId"`).
  No event-defined starts → `Recognized([])`. A nested process authored `CanStartWorkflow = false` →
  `Recognized([])`.
- `BpmnProcessRecurringScheduleProvider` implements `IRecurringTriggerScheduleProvider` — one
  `RecurringScheduleDescriptor` per **timer** start element, with the same `(StimulusType, StimulusHash)`
  pair the stimulus provider emits for that element, so the recurring-trigger pump's `StartOnly` dispatch
  matches the element's start binding.

Message/signal starts collapse onto the named-event routing pair (identical `(type, hash)` to `Event`'s
`EventStimulus`, replicated in-module to keep the dependency envelope free of the Primitives package;
`BpmnEventStartTriggerTests` pins the equivalence). Timer starts use a BPMN-owned `Bpmn.TimerStart` stimulus
type that folds the element id into the hash for per-element uniqueness, isolated from the `Timer`/`Cron`
activities. Event-definition property keys the surface reads live in `BpmnEventDefinitionProperties`
(`name` / `interval` / `cron`). At runtime a trigger delivery seeds a single token at the element named by
the forwarded binding metadata (`IRuntimeActivityExecutionContext.TriggerNodeId`/`TriggerMetadata`); direct
invocation seeds every none start. Fault codes: `bpmn.start.unresolved-trigger`, `bpmn.start.none-available`.

## Cross-domain contributions

- `BpmnStructureHandler` implements `IActivityStructureHandler` (`Elsa.Workflows.Design.Core`) with
  `SupportsScopedVariables = true` and `ProjectScopedVariables` — a `BpmnProcess` is a container
  scope that can own container-scoped variables visible to its descendant activities, using the same
  generic scope semantics as `Sequence` and `Flowchart` (ADR 0027). It carries the authored
  `BpmnAuthoredStructure.IsTransaction` flag onto the compiled executable `BpmnStructure.IsTransaction`.

## Structure-dependent `Cancelled` outcome (spec 125)

`BpmnProcess` statically declares `[ActivityOutcome(Done)]`. A **transaction** process (its compiled
`elsa.bpmn.structure` payload has `isTransaction: true`) additionally declares the `Cancelled` outcome so a
cancel end event's `Complete("Cancelled")` passes VF-ACT-006 and the parent can map it to a cancel boundary.
There is no per-activity outcome-projection surface on `IActivityStructureHandler`, so this rides the same
mechanism as Switch case labels: **`ExecutableNodeCompiler.ResolveOutcomes`** (in
`Elsa.Workflows.Publishing.Api`) reads the compiled structure and adds `Cancelled` when the BPMN structure is
a transaction — a one-branch additive extension mirroring the existing `elsa.switch.structure` special case.
This is the single touch outside the BPMN module; the outcome channel (`Complete(outcomeName)`,
contract-declared outcomes, parent `OutcomeNames`) is otherwise unchanged. (The test harness's
`WorkflowExecutionHarness.ResolveOutcomes` carries the same one-branch mirror for hand-built graphs.)
