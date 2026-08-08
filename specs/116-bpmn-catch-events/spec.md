# 116 — BPMN timer/message/signal intermediate catch events (BPMN Phase 2, catch-events slice)

**Status**: Implemented
**Merged**: PR #917

## Goal

Let a BPMN process wait mid-flow: an `intermediateCatchEvent` element with a timer, message, or
signal event definition parks its token until the awaited stimulus arrives, then routes the token
onward. **No new wait machinery**: a catch event binds a suspending Elsa child activity and rides
the existing `ScheduleChild` → `AwaitingChild` → `OnChildCompleted` token path — the child holds
the bookmark/durable timer through the runtime's existing suspension surface, and its resume is an
ordinary child completion to the BPMN engine. The slice also gives `Event` (Primitives) its
long-planned mid-flow wait form (the class's own remarks call this out as the W8 follow-up), which
is the natural message/signal catch child; timer catch events bind the existing `Delay`.

## Context (what exists today)

- `BpmnElement.EventDefinitions` and `BpmnEventDefinitionTypes.Timer/Message/Signal` already exist
  and round-trip in the structure payload; only `terminate` is interpreted.
  `BpmnElementTypes` is an open string set, so adding `intermediateCatchEvent` is not a
  state-schema break. `BpmnElementFamilies.Resolve` currently throws for unknown element types and
  for start events with event definitions.
- The BPMN engine already supports children that suspend: a token parks as `AwaitingChild` while
  the scheduled child runs, the composite defers, and the child's eventual completion arrives as a
  `ParentCompletionEvaluation`. Whether the child completed synchronously or after a
  bookmark/durable-timer resume is invisible to the engine — no BPMN state-envelope change is
  needed (`Elsa.Bpmn.ExecutionState` stays schema version 1).
- Node-scoped resume targets shipped (PR #911): multiple suspending children publish fine;
  resume-target keys are `{nodeId}:{attributeId}` with a `LocalResumeTargetId` fallback in the
  resolvers, so several catch events per process are supported.
- `Delay` (Scheduling) is a complete durable one-shot wait: deterministic timer id, durable timer
  persisted before the suspension, `[ResumeTarget]` resume path. It needs no changes.
- `Event` (Primitives) is start-only: `[TriggerActivity]`, completes immediately, indexed at
  publish time by `EventTriggerStimulusProvider` keyed by `EventStimulus.Hash(EventName)`.
  `HttpEndpoint` is the shipped template for a dual start/mid-flow trigger activity
  (`StatefulTriggerActivity`, `CanStartWorkflow` input, suspend with typed trigger registrations,
  `[ResumeTarget]` resume; its stimulus provider returns `Recognized([])` for non-start nodes).
- Correlation-scoped resume already exists passively (`correlationId` through
  `IGlobalBookmarkStimulusLookup` / the stimulus router). **Decision:** message delivery is the
  existing stimulus dispatch surface — raising an `Event` stimulus (type `Event`, hash of the
  event name) resumes waiting message/signal catch events exactly like it resumes any waiting
  bookmark. No BPMN-module delivery wrapper and no correlation subsystem are added.
- The interchange importer currently drops `intermediateCatchEvent` and degrades event-defined
  start events; `BpmnGraph.Validate` requires every child-slot node to be bound by exactly one
  element.

## In scope (this slice)

- **Element type + family**: `BpmnElementTypes.IntermediateCatchEvent` (`intermediateCatchEvent`)
  and behavior family `BpmnElementFamilies.IntermediateCatchEvent` (`intermediateCatchEvent.catch`).
  `Resolve` accepts an intermediate catch event that declares exactly one event definition of type
  `timer`, `message`, or `signal`; anything else stays a deterministic
  `BpmnExecutionException`.
- **Catch-event behavior**: `CatchEventBehavior` (`IBpmnElementBehavior`) — token arrival schedules
  the element's bound child (required); child completion routes outbound flows through the shared
  task flow-selection rules (`bpmn.flow.none-taken` fault parity). Registered in
  `ActivitiesBpmnFeature`.
- **Graph validation**: an `intermediateCatchEvent` element requires a bound `ChildNodeId`
  (subprocess-style rule in `BpmnGraph.Validate`).
- **`Event` mid-flow wait form** (Primitives): convert `Event` to the `HttpEndpoint` dual-role
  template (`StatefulTriggerActivity<EventResult, EventWaitState, EventReceived>`):
  - New `CanStartWorkflow` input, **default `true`** (preserves the activity's start-first
    identity: existing authored Event start nodes still complete on direct invocation).
  - Completes when the run's trigger delivery targeted this node, or on direct invocation with
    `CanStartWorkflow = true`. Otherwise suspends with one
    `ActivityTriggerRegistration<EventReceived>` (`EventStimulus.StimulusType`,
    `EventStimulus.Hash(EventName)`) and resumes through `[ResumeTarget]` completing with
    `EventResult(EventName)`.
  - `EventTriggerStimulusProvider` mirrors the HttpEndpoint provider: a node whose
    `CanStartWorkflow` literal is not `true` — and `true` is the unauthored default — is
    `Recognized([])` (declared non-start, not indexed, no publish failure), so a mid-flow catch
    `Event` can never start a new workflow instance.
- **Child binding contract** (documented, not new code): a timer catch event binds a `Delay`
  child; message and signal catch events bind an `Event` child with `CanStartWorkflow = false`.
  Signal and message are intentionally identical mechanically in this slice — both are named-event
  stimuli; the BPMN event-definition type is authoring/interchange semantics. Child synthesis from
  event-definition properties is an authoring-surface concern (Studio/importer — follow-ups); the
  runtime contract is only "the bound child suspends until the awaited stimulus".
- **Tests**: BPMN harness coverage (timer catch via `Delay` + durable timer bookmark resume;
  message/signal catch via `Event` + stimulus resume; two concurrent catch events on parallel
  branches; family/validation rejections) and Primitives/runtime coverage for the `Event` dual
  role and provider gating. `BpmnRuntimeFixture` gains bookmark-resume support (exposing the
  harness `ResumeAsync`).
- **Module docs**: BPMN README (execution model + phasing) and EXTENSION_POINTS (new behavior
  family); Primitives README if it describes `Event`.

## Out of scope (deferred follow-ups, stated cuts)

- **Event-defined start events** (message/signal/timer): a BPMN start-event trigger surface needs
  an `IActivityTriggerStimulusProvider` that recognizes `BpmnProcess` nodes and describes stimuli
  from their structure payload, timer starts need the `RecurringScheduleDescriptor` +
  `RecurringTriggerPumpTask` template, and start-event selection needs the triggering element's
  identity threaded into `StartAsync` (today all start events emit tokens). Deliberately cut to
  keep this slice reviewable; `BpmnElementFamilies.ResolveStartEvent` keeps throwing for
  event-defined start events.
- **Interchange XML support** for `intermediateCatchEvent`/event definitions (import currently
  drops them, with an explicit analyze-time issue): synthesizing typed `Delay`/`Event` child
  nodes from `timerEventDefinition`/`messageRef`/`signalRef` is an authoring-surface unit that
  belongs with the Studio catch-event UX.
- Event-based gateway, boundary events, event subprocesses, multi-instance (later Phase 2 units).
- A correlation subsystem or message-delivery API (passive correlation + existing stimulus
  dispatch only).
- Stimulus payload mapping into workflow values beyond the existing `EventResult` projection.
- Timer catch expression durations (the bound `Delay`'s `Duration` input already accepts any
  binding the runtime supports; nothing BPMN-specific is added).

## Functional requirements

**FR-1 — Family resolution.** `BpmnElementFamilies.Resolve` maps an `intermediateCatchEvent`
element declaring exactly one event definition of type `timer`, `message`, or `signal` to the
`intermediateCatchEvent.catch` family. An intermediate catch event with zero, multiple, or
unsupported-type event definitions throws a deterministic `BpmnExecutionException` naming the
element. Unknown element types and event-defined start events keep their existing deterministic
rejections.

**FR-2 — Child binding validation.** `BpmnGraph.Validate` rejects an `intermediateCatchEvent`
element with no bound `ChildNodeId` (deterministic `BpmnExecutionException`, subprocess parity).
The existing rules — bound node must exist in the `Bpmn.Activities` slot, exactly one element per
child node — apply unchanged.

**FR-3 — Token wait semantics.** When a token arrives at a catch event, the behavior returns
`ScheduleChild`: the token parks as `AwaitingChild`, the bound child is scheduled through
`BpmnScheduler` (standard metadata/provenance), and the composite defers. While the child is
suspended on its bookmark/durable timer, the BPMN process remains `Running` with no queued
scheduler work. No new BPMN state fields are introduced; record ids stay a pure function of
`BpmnExecutionState.Sequence`.

**FR-4 — Resume and routing.** When the awaited stimulus resumes the child and it completes, the
ordinary child-completion evaluation routes the token: outbound flow selection follows the shared
task rules (unconditional flows always taken, `conditionOutcome` matched against the child's
outcome names, default flow as fallback). No matching flow with outbound flows declared faults
`bpmn.flow.none-taken` (task parity).

**FR-5 — Multiple concurrent catch events.** Two or more catch events may wait concurrently in one
process (e.g. behind a parallel split). Each child suspends with its own node-scoped resume
handle; resuming one leaves the others waiting, and the process completes when all joined tokens
arrive. Terminate/cancel semantics are untouched: `CancelLiveWork` already cancels `AwaitingChild`
tokens, and late completions of canceled tokens are absorbed by the existing token-status guard.

**FR-6 — `Event` dual role.** `Event` completes with `EventResult(EventName)` when (a) the start
trigger delivery targeted this node, or (b) the invocation is direct and `CanStartWorkflow` is
`true` (the default). In every other case it suspends with exactly one
`ActivityTriggerRegistration<EventReceived>` whose stimulus identity is
`EventStimulus.StimulusType` / `EventStimulus.Hash(EventName)`, and its `[ResumeTarget]` path
completes with `EventResult` built from the committed wait state. The stimulus identity derivation
(`EventStimulus`) is unchanged — a raised event and a waiting catch resolve to the same routing
key.

**FR-7 — Start-trigger indexing gate.** `EventTriggerStimulusProvider` describes start stimuli
only for nodes whose `CanStartWorkflow` resolves to `true` (unauthored counts as `true` —
default-preserving). Other `Event` nodes return `Recognized([])`: not indexed, no publish failure.
The literal-event-name publish-failure rule applies only to start-indexed nodes.

**FR-8 — Correlation stays passive.** `Event.CorrelationId` keeps its existing passive semantics
(trigger-descriptor correlation scope for start indexing; no new routing dimension for mid-flow
waits). Message delivery to a waiting catch event is the existing stimulus dispatch surface;
this slice adds no delivery API.

## Invariants that MUST survive

- `Elsa.Bpmn.ExecutionState` stays schema version 1; the only state mutation home remains
  `BpmnStateMutator`; all record ids derive from `Sequence`; `Canceled` tokens are never pruned
  (late-completion tolerance).
- Behaviors stay decision-only: `CatchEventBehavior` returns commands; the engine keeps mutation
  and scheduling authority. The BPMN module gains **no** dependency on Scheduling, Primitives
  activities, or timer/bookmark services — the child is an opaque `ExecutableNode`.
- A terminal continuation still cannot co-exist with staged child schedules; catch-event
  scheduling rides the existing defer path.
- Existing `Event` start behavior is preserved by default (`CanStartWorkflow` defaults to `true`);
  the atomic `EventResult` contract and `EventStimulus` hashing are unchanged.
- Deterministic ids only (timer ids derive from invocation ids via `Delay`; no wall-clock-derived
  identity anywhere in this slice).

## Success criteria

- BPMN harness tests cover: timer catch event suspends on its `Delay` child (durable timer +
  bookmark written, process `Running`, token `AwaitingChild`) and routes to completion when the
  timer bookmark resumes; message and signal catch events suspend on their `Event` children with
  the event-name stimulus hash and route on stimulus resume; two catch events waiting behind a
  parallel gateway resume independently and join; family resolution rejects an
  intermediate catch event with no/multiple/unsupported event definitions; graph validation
  rejects a catch event without a bound child.
- Runtime/Primitives tests cover: `Event` mid-flow suspension + resume, direct-invocation
  completion with the default, trigger-delivery completion, and the provider's
  `CanStartWorkflow` gate (indexed by default; `Recognized([])` when explicitly `false`).
- No behavior change for existing BPMN processes (full BPMN suite green) and for existing `Event`
  start flows (full runtime suite green, activity-library acceptance updated for the new input).
- Full test projects green: BPMN, BPMN Interchange, Activities Runtime, Scheduling, Workflows
  Runtime, Architecture. Full solution build clean.
