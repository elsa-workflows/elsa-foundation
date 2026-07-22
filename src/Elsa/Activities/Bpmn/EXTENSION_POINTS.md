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
- **Multi-instance loops (spec 121)** add no behavior: a `BpmnElement.LoopCharacteristics`
  (`BpmnLoopCharacteristics`) turns a task/subprocess host's `ScheduleChild` decision into a loop the
  engine owns entirely — a coordinator token plus private per-instance sub-tokens, each scheduled through
  the runtime iteration-frame seam (`ScheduleChildActivity(..., iterationFrame)`) seeding `loopIndex`. The
  loop record `BpmnLoopState` and the instance-completion advance / last-completion routing / cascade
  cancellation all live in `BpmnExecutionEngine`. Collection mode is validated as a stated cut (not
  executable this slice).

## Activity-owned structure contracts

This module also exposes these activity-owned contracts:

- `Bpmn.Activities` child slot
- `elsa.bpmn.structure` structure payload with schema version `1.0.0`
- `BpmnStructure.Elements` containing `BpmnElement[]` (each element optionally carries `AttachedToRef`/
  `CancelActivity` for boundary events, spec 120, and `LoopCharacteristics` — `BpmnLoopCharacteristics` —
  for multi-instance loops, spec 121; both additive, state schema stays version 1)
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
  generic scope semantics as `Sequence` and `Flowchart` (ADR 0027).
