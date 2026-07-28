# 117 — BPMN event-defined start events (message/signal/timer) (BPMN Phase 2, event-start slice)

## Goal

Let a published BPMN process be *started* by an external stimulus: a `startEvent` element carrying a
timer, message, or signal event definition registers a durable start trigger at publish time, and the
matching stimulus (a raised event, or a due recurring timer schedule) starts a new process instance
whose token seeds at exactly that start element. **No new start machinery inside the process**: an
event-defined start element is a pure BPMN element (token seeds `Active`, routes outbound exactly like
a none start) — the trigger surface lives entirely at publish/dispatch time and reuses the existing
`IActivityTriggerStimulusProvider` (message/signal) and `IRecurringTriggerScheduleProvider` +
recurring-trigger pump (timer) seams. This closes the event-defined-start cut that spec 116 deferred.

## Context (what exists today)

- `BpmnElement.EventDefinitions` and `BpmnEventDefinitionTypes.Timer/Message/Signal` already exist and
  round-trip in the structure payload. `BpmnEventDefinition.Properties` is a free-form `string→string`
  bag with no established keys — this slice sets the property-key convention (a later interchange unit
  follows it).
- `BpmnElementFamilies.ResolveStartEvent` currently throws for **any** start event with event
  definitions ("only none start events are supported by this engine slice").
  `ResolveIntermediateCatchEvent` (spec 116) is the template: it accepts exactly one Timer/Message/Signal
  definition. `BpmnElementTypes` is an open string set and `BpmnEventDefinitionTypes` too, so no
  state-schema break.
- `BpmnExecutionEngine.StartAsync` unconditionally seeds one `Active` token per element in
  `graph.StartEvents` (all start events). `BpmnProcess` is a `StructuralActivity` and is **not** a
  `[TriggerActivity]` today.
- The start-trigger seam (W7, spec 089): a node marked with `[TriggerActivity]` is compiled with node
  metadata `executionType=Trigger`; the `WorkflowTriggerBindingExtractor` requires **exactly one**
  `IActivityTriggerStimulusProvider` to recognize each such node and turns each returned
  `TriggerStimulusDescriptor` into one `WorkflowTriggerBinding` keyed by
  `(artifactId, executableNodeId, stimulusHash)` with a duplicate-binding-id guard. A recognized node
  may return zero descriptors (`Recognized([])`) → `IntentionallyNonStarting`, no publish failure. The
  descriptor's `Metadata` is carried verbatim onto `WorkflowTriggerBinding.Metadata`. `HttpEndpoint`
  (one descriptor per method) is the live multi-descriptor precedent.
- `EventStimulus` (Primitives) maps an event **name** to the opaque `(StimulusType="Event", Hash)`
  routing pair; a raised `Event` stimulus and a published `Event` start trigger resolve to the same
  key. `TimerStimulus`/`CronStimulus` (Scheduling) do the same for an interval/cron literal, and each
  Timer/Cron start records **both** a trigger binding and a `RecurringTriggerSchedule` that the
  `RecurringTriggerPumpTask` fires through `IStimulusRouter` in `StimulusRoutingMode.StartOnly`.
- `IRecurringTriggerScheduleProvider.Describe(node)` returns a **single** `RecurringScheduleDescriptor?`
  today; `RecurringTriggerScheduleIndexer` builds one schedule per trigger node keyed by
  `RecurringTriggerSchedule.BuildId(artifactId, executableNodeId)`.
- `StimulusRouter.StartMatchingTriggersAsync` starts one instance per matching binding, threading
  `binding.ExecutableNodeId` as `triggerNodeId` into the start dispatch — but **not**
  `binding.Metadata` (the gap this slice closes). `TriggerNodeId` is threaded end to end
  (dispatch request → start command payload → workflow-started checkpoint seed → durable value under
  `RuntimeMetadataKeys.TriggerNodeId` → `RuntimeInputBindingStateProjection.ProjectTriggerNodeId` →
  `SimpleActivityExecutionContext.TriggerNodeId`), where `StatefulActivity` compares it to its own
  `ExecutableNodeId` to compute `IsTriggerDelivery`. `IRuntimeActivityExecutionContext` exposes no
  trigger info today.
- BPMN state discipline (unchanged): `Elsa.Bpmn.ExecutionState` stays schema version 1; all record ids
  derive from `BpmnExecutionState.Sequence`; the only mutation home is `BpmnStateMutator`; `Canceled`
  tokens are never pruned; a terminal continuation cannot co-exist with staged child schedules.

## Design decisions

### D1 — Start trigger surface

`BpmnProcess` becomes a `[TriggerActivity]` so the compiler marks its node `executionType=Trigger`. A
new `BpmnProcessTriggerStimulusProvider` (Bpmn module) recognizes `BpmnProcess` nodes, reads the
authored structure from the node payload (`BpmnGraph.From`), and emits **one descriptor per
event-defined start element** (message/signal via `EventStimulus`, timer via
`BpmnTimerStartStimulus`), each carrying the start element id in `Metadata` under the constant key
`BpmnStartTrigger.StartElementIdMetadataKey` (`"bpmn.startElementId"`). A process with **no**
event-defined start elements returns `Recognized([])` (`IntentionallyNonStarting`) — none-start
processes are started by direct invocation, unchanged.

**Nested `BpmnProcess`.** `ExecutableNode` exposes no parent/root hierarchy, so the provider cannot
detect root position from the node alone. Per the ratified fallback, `BpmnProcess` gains a
`CanStartWorkflow` literal input **defaulting `true`** (mirroring `Event`): the provider describes start
stimuli only when `CanStartWorkflow` is not the literal `false`; a `BpmnProcess` bound as a child
inside another workflow/process is authored `CanStartWorkflow = false` and yields `Recognized([])`
(no start bindings). Recorded as a deviation-of-convenience below.

### D2 — Message/signal starts collapse onto `EventStimulus`

A message/signal start element derives its stimulus from a `name` property
(`BpmnEventDefinitionProperties.Name`) on the event definition, using the exact
`EventStimulus.StimulusType`/`Hash(name)` derivation `Event` uses. Consequences (all **intended**):

- One event delivery can both **start** processes (matching start-trigger bindings) and **resume**
  waiting message/signal catch events (matching bookmarks) — the router does both on one stimulus.
- Two start elements in one process with the same event `name` collide on `stimulusHash` → identical
  `WorkflowTriggerBinding` id → the extractor's duplicate-binding-id guard fails the publish.
- A message/signal start element with a missing/blank `name` → the provider throws → the publish fails
  deterministically (mirrors `EventTriggerStimulusProvider`'s non-literal throw style). Signal and
  message are mechanically identical here (both named-event stimuli); the definition type is
  authoring/interchange semantics.

### D3 — Timer starts ride the recurring-schedule template

The repo is pre-release (no back-compat). `IRecurringTriggerScheduleProvider.Describe` changes from a
single `RecurringScheduleDescriptor?` to `IReadOnlyCollection<RecurringScheduleDescriptor>` (empty =
"no contribution": not-my-type or recognized-with-no-schedules). `TimerRecurringScheduleProvider` and
`CronRecurringScheduleProvider` return a one-element (or empty) collection; a new
`BpmnProcessRecurringScheduleProvider` returns **one descriptor per timer start element**. Timer start
properties: exactly one of `interval` (`BpmnEventDefinitionProperties.Interval`, ISO-8601 duration →
`RecurringScheduleKind.Interval`) **xor** `cron` (`BpmnEventDefinitionProperties.Cron` →
`RecurringScheduleKind.Cron`); missing-both or both-present throws → publish fails.

**Collision scoping.** Timer stimuli are pump-internal (never externally sent). `BpmnTimerStartStimulus`
folds the **BPMN element id** into the hash input (`elementId + "\n" + normalizedExpression`) under a
BPMN-owned stimulus type (`BpmnTimerStartStimulus.StimulusType = "Bpmn.TimerStart"`), giving
per-element uniqueness within a process and full isolation from the `Timer`/`Cron` activities' stimulus
types. The `(StimulusType, StimulusHash)` pair the **schedule** provider emits equals the pair the
**stimulus** provider emits for the same element, so the pump's `StartOnly` dispatch matches the
element's start binding. Because `RecurringTriggerScheduleIndexer` keyed schedule rows by
`(artifactId, executableNodeId)` and a `BpmnProcess` node yields several timer descriptors, the indexer
now disambiguates multi-descriptor nodes by folding the stimulus hash into the schedule id
(`RecurringTriggerSchedule.BuildId(artifactId, nodeId, stimulusHash)`); single-descriptor nodes
(`Timer`/`Cron`) keep the existing `BuildId(artifactId, nodeId)` id, so their schedule identity is
unchanged. Cross-artifact identical `(element id, expression)` collides identically to the existing
`Timer`-identical-interval property — a pre-existing routing model, not introduced here.

### D4 — Identity threading (the runtime seam this unit adds)

The matched binding's `Metadata` is forwarded through the start path, mirroring `TriggerNodeId` end to
end and kept minimal:

- `StimulusRouter` forwards `binding.Metadata` as `triggerMetadata` on the start dispatch request.
- `WorkflowExecutionStartDispatchRequest` → `WorkflowExecutionStartCommandPayload` →
  `RuntimeCheckpointCommandPayload` each gain a nullable `triggerMetadata` / `TriggerMetadata` /
  `SeedTriggerMetadata` channel (optional trailing constructor parameter; JSON name-matched, so legacy
  serialized payloads default to empty).
- `RuntimeWorkflowStateSeed` seeds it as one durable value (the serialized `string→string` map) under a
  new reserved slot (`TriggerMetadataValueIdPrefix` = `"trigger-meta:"`, slot name `"metadata"`),
  tagged with a new `RuntimeMetadataKeys.TriggerMetadataName` key — its own spoof-proof channel, never
  the `input:*` namespace.
- `RuntimeInputBindingStateProjection.ProjectTriggerMetadata` reads it back into a `string→string` map;
  `RuntimeInputBindingStateProjectionSet` gains a `TriggerMetadata` field.
- `IRuntimeActivityExecutionContext` exposes nullable `TriggerNodeId` (already on
  `SimpleActivityExecutionContext`, now on the interface) and `TriggerMetadata`
  (`IReadOnlyDictionary<string,string>?`). Both are populated from the **committed** start seed
  (spoof-proof, not user input), available during the start evaluation.

The recurring-trigger pump's `StartOnly` dispatch flows through the same `StimulusRouter` start code, so
timer-start metadata rides along for free (verified).

### D5 — Family resolution + runtime behavior

`ResolveStartEvent` accepts exactly one Timer/Message/Signal event definition, yielding the families
`startEvent.timer` / `startEvent.message` / `startEvent.signal`; zero definitions stays
`startEvent.none`; anything else (multiple definitions, or an unsupported type) keeps a deterministic
`BpmnExecutionException`. Each event-start family reuses the none-start token behavior (a single
family-parameterized `StartEventBehavior`: emit tokens on every outbound flow). Event-defined start
elements are **pure elements**: graph validation rejects a `ChildNodeId` binding on them (none-start
parity); at runtime a seeded token behaves exactly like a none start (routes outbound). The trigger
machinery lives entirely at publish/dispatch time.

### D6 — Start semantics in `BpmnExecutionEngine.StartAsync`

- **Trigger delivery** (`context.TriggerNodeId == context.ExecutableNodeId`, the `BpmnProcess` node):
  read the start element id from `context.TriggerMetadata[bpmn.startElementId]`, resolve it to an
  event-defined start element, and seed exactly **one** `Active` token there. A missing key, or an id
  that resolves to no event-defined start element, is a deterministic fault
  (`bpmn.start.unresolved-trigger`).
- **Direct invocation** (no trigger delivery): seed `Active` tokens at **all** none start events and
  **no** tokens at event-defined start elements. A process whose only start events are event-defined
  (zero none starts) faults deterministically (`bpmn.start.none-available`).

All ids from `BpmnExecutionState.Sequence` via `BpmnStateMutator`, as always.

## In scope (this slice)

- **Family + validation**: `startEvent.timer/message/signal` families; `ResolveStartEvent` acceptance;
  the pure-element (`no ChildNodeId`) validation for event-defined starts.
- **Start behavior**: `StartEventBehavior` family-parameterized over none/timer/message/signal (replaces
  `NoneStartEventBehavior`), registered in `ActivitiesBpmnFeature` for all four start families.
- **Start semantics**: trigger-delivery vs direct-invocation seeding in `BpmnExecutionEngine.StartAsync`,
  with the two deterministic `bpmn.start.*` faults.
- **Publish-time trigger surface**: `BpmnProcess` `[TriggerActivity]` + `CanStartWorkflow` input;
  `BpmnProcessTriggerStimulusProvider` (message/signal via `EventStimulus`, timer via
  `BpmnTimerStartStimulus`); `BpmnProcessRecurringScheduleProvider` (timer schedules);
  `BpmnEventDefinitionProperties` / `BpmnStartTrigger` constants; registered in `ActivitiesBpmnFeature`.
- **Recurring provider fan-out**: `IRecurringTriggerScheduleProvider.Describe` → collection; Timer/Cron
  providers and the indexer updated; multi-descriptor schedule-id disambiguation.
- **Identity threading (D4)**: `TriggerMetadata` seeded/projected/exposed; `StimulusRouter` forwards
  `binding.Metadata`; `IRuntimeActivityExecutionContext.TriggerNodeId`/`TriggerMetadata`.
- **Tests + module docs**: extraction, schedule-provider parity, runtime start semantics, determinism;
  BPMN README + EXTENSION_POINTS; runtime EXTENSION_POINTS.

## Out of scope (deferred follow-ups, stated cuts)

- **Interchange XML support** for event-defined start events (import still drops event definitions);
  synthesizing the `name`/`interval`/`cron` properties from `messageRef`/`signalRef`/`timerEventDefinition`
  is an authoring-surface unit that belongs with the Studio start-event UX. This slice only sets the
  property-key convention.
- **Automatic root detection** for nested `BpmnProcess` (the provider uses the `CanStartWorkflow` opt-out
  fallback; see D1).
- **Stimulus payload mapping** into the seeded process beyond the existing start-stimulus channel; a
  start element does not project the stimulus payload into a process variable (a later mapping unit).
- Event-based gateway, boundary events (including error/timer boundary), event subprocesses,
  multi-instance (later Phase 2 units).
- A correlation subsystem or message-delivery API (passive correlation + existing stimulus dispatch only).
- Repairing the pre-existing `Timer`-identical-interval cross-artifact routing collision (out of scope;
  BPMN timer starts avoid it within a process by folding in the element id).

## Functional requirements

**FR-1 — Start family resolution.** `BpmnElementFamilies.Resolve` maps a `startEvent` declaring exactly
one event definition of type `timer`/`message`/`signal` to `startEvent.timer`/`startEvent.message`/
`startEvent.signal`; zero definitions stays `startEvent.none`. Multiple definitions, or one of an
unsupported type, throws a deterministic `BpmnExecutionException` naming the element.

**FR-2 — Event start is a pure element.** `BpmnGraph.Validate` rejects an event-defined start element
that binds a `ChildNodeId` (none-start parity). The existing start-event rules (no inbound flows, ≥1
start event) apply unchanged.

**FR-3 — Start behavior parity.** A token that seeds at an event-defined start element routes onto every
outbound sequence flow exactly like a none start (`StartEventBehavior`). No child scheduling, no new
token status; record ids stay a pure function of `Sequence`.

**FR-4 — Trigger-delivery start.** When `context.TriggerNodeId == context.ExecutableNodeId`,
`StartAsync` seeds exactly one `Active` token at the event-defined start element named by
`context.TriggerMetadata["bpmn.startElementId"]`; a missing key or an unresolvable element id faults
`bpmn.start.unresolved-trigger`.

**FR-5 — Direct-invocation start.** With no trigger delivery, `StartAsync` seeds tokens at all none
start events and none at event-defined start elements; a process with zero none start events faults
`bpmn.start.none-available`.

**FR-6 — Message/signal start indexing.** `BpmnProcessTriggerStimulusProvider` recognizes `BpmnProcess`
nodes (when `CanStartWorkflow` is not literal `false`) and, per message/signal start element, emits an
`EventStimulus.Describe(name)` descriptor carrying `bpmn.startElementId` in `Metadata`. A missing/blank
`name` throws (publish fails). No event-defined starts → `Recognized([])`. A `BpmnProcess` authored
`CanStartWorkflow = false` (e.g. nested) → `Recognized([])`.

**FR-7 — Timer start indexing + schedule.** For each timer start element the stimulus provider emits a
`BpmnTimerStartStimulus` descriptor (element id folded into the hash) and
`BpmnProcessRecurringScheduleProvider` emits a matching `RecurringScheduleDescriptor` with the same
`(StimulusType, StimulusHash)`. Timer properties require exactly one of `interval`/`cron`; missing-both
or both-present throws (publish fails).

**FR-8 — Duplicate/collision publish failure.** Two start elements resolving to the same
`(StimulusType, StimulusHash)` (two message starts with the same `name`) produce duplicate binding ids
and fail the publish via the extractor's guard. Timer starts avoid intra-process collision by folding in
the element id.

**FR-9 — Identity threading.** `StimulusRouter` forwards the matched `binding.Metadata` through the
start path; it is seeded on its reserved durable channel and surfaced on
`IRuntimeActivityExecutionContext.TriggerMetadata` (and `TriggerNodeId`), populated from the committed
start seed. The recurring-trigger pump's `StartOnly` path carries timer-start metadata through the same
router code.

**FR-10 — Recurring provider fan-out.** `IRecurringTriggerScheduleProvider.Describe` returns a
collection; `Timer`/`Cron` still index exactly one schedule each with unchanged schedule ids; a
`BpmnProcess` with N timer starts indexes N schedules with per-element schedule ids.

## Invariants that MUST survive

- `Elsa.Bpmn.ExecutionState` stays schema version 1; the only state mutation home remains
  `BpmnStateMutator`; all record ids derive from `Sequence`; `Canceled` tokens are never pruned.
- Behaviors stay decision-only; the BPMN module gains no dependency on timer/bookmark services — the
  start trigger surface reads only the pinned published node at publish time.
- Existing behavior is preserved for: none-start processes (direct invocation seeds all none starts);
  `Timer`/`Cron` recurring start triggers (single-descriptor path, unchanged schedule ids); the atomic
  `EventStimulus` hashing (a raised event and a BPMN message start resolve to the same key).
- Deterministic ids only; no wall-clock-derived identity in the stimulus/schedule hashes.
- No new HTTP endpoints; the domain project-tree naming guard and VF-ACT gates hold.

## Success criteria

- Extraction tests: a `BpmnProcess` with message + signal + timer starts yields the expected bindings
  (count, `(type, hash)` per element, `bpmn.startElementId` metadata); no event-defined starts →
  `IntentionallyNonStarting`; two message starts with the same name → publish failure; a message start
  with a missing name → publish failure; a timer start with missing-both/both expressions → publish
  failure; a nested `BpmnProcess` (`CanStartWorkflow = false`) → no start bindings.
- Schedule-provider tests: N timer starts → N descriptors; `(type, hash)` parity with the stimulus
  provider for the same elements; `Timer`/`Cron` providers still work after the fan-out change.
- Runtime start-semantics tests (harness): trigger delivery seeds only the targeted element's token and
  the process routes to completion; direct invocation with mixed starts seeds only none starts; direct
  invocation with only event-defined starts faults `bpmn.start.none-available`; an unresolvable trigger
  element id faults `bpmn.start.unresolved-trigger`.
- Determinism: token/record ids are stable across identical runs.
- Full test projects green: BPMN, BPMN Interchange, Activities Runtime, Scheduling, Primitives, Http,
  Workflows Runtime (+ Publishing), Architecture. Full solution build clean.
