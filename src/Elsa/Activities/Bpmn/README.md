# Elsa.Activities.Bpmn

`BpmnProcess` is a scoped composite activity that executes a BPMN 2.0 element graph with token
semantics — the third structural container next to `Flowchart` and `Sequence`. `BpmnExecutionEngine`
schedules bound Elsa child activities through BPMN-owned execution state that tracks tokens, active
children, and diagnostics.

## Execution model

- Start events emit tokens; sequence flows carry them; end events absorb them. The process completes
  when no live tokens remain and no children are running.
- Gateways and start/end events are **engine-interpreted**: they never schedule an Elsa activity.
  Task-family elements (`task`, `userTask`, `serviceTask`, …), `subProcess` elements, and
  `intermediateCatchEvent` elements bind a child activity from the `Bpmn.Activities` slot
  (`childNodeId`). A BPMN task is a visual/semantic wrapper around any Elsa activity; an embedded
  subprocess typically binds a nested `BpmnProcess`.
- **Event-defined start events** (spec 117) declare exactly one `timer`, `message`, or `signal`
  event definition and let an external stimulus *start* a process. They are pure elements (no bound
  child): the trigger surface lives entirely at publish/dispatch time. `BpmnProcess` is a
  `[TriggerActivity]`; at publish time its message/signal starts register a named-event trigger
  binding (same `(type, hash)` as `Event`, so one delivery both starts processes and resumes catch
  events) and its timer starts register a recurring schedule the trigger pump fires. A trigger
  delivery seeds a single token at the start element the matched binding names (forwarded through
  `IRuntimeActivityExecutionContext.TriggerMetadata`); direct invocation seeds every none start and
  leaves event-defined starts dormant. Nested processes opt out with `CanStartWorkflow = false`.
- **Intermediate catch events** (spec 116) declare exactly one `timer`, `message`, or `signal`
  event definition and bind a **suspending** child: `Delay` for timer catches, a mid-flow `Event`
  (`CanStartWorkflow = false`) for message/signal catches. The token parks as awaiting-child while
  the child holds its durable timer/bookmark through the runtime's ordinary suspension surface;
  the resumed child's completion routes outbound flows by the shared task selection rules. No BPMN
  wait machinery exists — raising the named event through the existing stimulus dispatch surface
  is what resumes a message/signal catch.
- Flow selection matches the completing child's outcome names: unconditional flows are always taken
  (BPMN's implicit AND-split), a conditional flow (`conditionOutcome`) is taken when the child
  reported that outcome, and the default flow is taken only when nothing else was. Exclusive and
  inclusive gateways may bind a decision child whose outcomes drive the selection.
- **Event-based gateways** (spec 119) fork a token into a first-catch-wins **race**: every outbound
  flow targets an `intermediateCatchEvent` (validated: ≥2 outbound flows, each target a catch with
  exactly one inbound flow, no child binding, no conditional/default flows). The gateway arms all its
  catch events simultaneously (parallel-split style); the engine records the race members and, when the
  first member's child completes, marks that member the winner and cancels every losing sibling — its
  token ends **and** its armed suspending child's runtime activity-execution subtree is torn down through
  the spec 112 seam-A `RequestChildSubtreeCancellation` (this is the module's first seam-A consumer). A
  losing sibling's late completion is absorbed by the canceled-token guard. Loser teardown is staged only
  on a non-fault winner continuation; if the winner routing itself faults, or a terminate ends the process
  first, the losers are cancelled **logically only** (their runtime subtrees are left as-is, matching the
  terminate/pending-fault precedent).
- **Boundary events** (spec 120) attach to a host element that runs a bound child (a task-family element
  or an embedded `subProcess`). A **timer/message/signal** boundary arms a synthesized suspending listener
  child (a `Delay` for timer, a mid-flow `Event` for message/signal) ALONGSIDE the host's bound child the
  moment the host is scheduled: if the **host** completes first it routes normally and its still-armed
  listeners are torn down (token cancelled + listener subtree reclaimed through seam A); if an
  **interrupting** listener (`cancelActivity = true`, the default) fires first the host's bound child and
  the sibling listeners are torn down through seam A and the boundary's outbound flows route; a
  **non-interrupting** listener (`cancelActivity = false`) routes its outbound while the host keeps running
  (single-shot — no re-arm). An **error** boundary has no listener: when the host's bound child faults, the
  process **absorbs** the fault through the spec 115 seam-B `RequestChildFaultAbsorption` (the named
  incident resolves, the faulted child's subtree is reclaimed), cancels the host token and sibling
  listeners, and routes the error path — instead of faulting the composite. This is the module's first
  seam-B consumer. Like the race, all interrupt/absorption semantics live in the engine; the
  `BoundaryEventBehavior` only routes outbound flows when a boundary fires. Boundaries are validated (host
  is a task-family/subprocess element with a bound child; no inbound flows; ≥1 outbound; catch boundaries
  bind a listener child, error boundaries bind none and must be interrupting; ≤1 error boundary per host).
- **Multi-instance activities** (spec 121) run a host's bound child N times — **sequentially** (one
  instance at a time) or in **parallel** (N concurrent instances of the *same* child node). Loop
  characteristics (`BpmnLoopCharacteristics`: `IsSequential` + a positive `Cardinality`) are authored on a
  task-family or `subProcess` host that binds a child. When a token reaches the host it becomes a **loop
  coordinator** (stays `AwaitingChild`); the engine schedules the child on private per-instance sub-tokens,
  each carrying an iteration frame that seeds a zero-based `loopIndex`, and routes the host's outbound flows
  through its normal behavior only after the **last** instance completes. This is the module's first user of
  the runtime iteration-frame seam and the first activity anywhere to schedule concurrent same-node
  children. It composes with boundary/error teardown: cancelling a multi-instance host (an interrupting
  boundary, an error-boundary absorption, or an event-gateway loser) cancels **all** its live instances
  through seam A, and an error boundary on a multi-instance host absorbs a faulted instance through seam B
  while the remaining instances cascade-cancel. Catch boundaries arm **once** on the coordinator and tear
  down when the last instance completes. Behaviors stay multi-instance-unaware; the whole loop lifecycle
  lives in the engine. **Collection mode** (spec 123) runs the child once per item of a declared
  container-scoped collection variable (`BpmnLoopCharacteristics.CollectionVariable` XOR `Cardinality`). The
  engine reads the variable **once** at loop start (a documented snapshot) through the runtime
  scoped-variable read seam (`IRuntimeScopedVariableReader` / `TryReadScopedVariableValue`, committed-state
  backed); the snapshot persists on the additive `BpmnLoopState.Items` so sequential later instances never
  re-read it. Each iteration frame seeds the item under `ItemVariable` (default `"item"`, must not be the
  reserved `loopIndex`) alongside `loopIndex`. A null/absent collection is an empty loop (`N == 0`, routes
  immediately); a present non-array value faults `bpmn.loop.collection-not-a-collection`, an externally-stored
  payload faults `bpmn.loop.collection-not-inline`, and an unreadable variable faults
  `bpmn.loop.collection-unreadable`. Everything else — sub-token model, sequential/parallel progression,
  teardown, boundary interplay, determinism — is the cardinality machinery unchanged.
- **Cyclic sequence flows** are executable (spec 122): a token carries an **iteration key** (`null` on the
  implicit first pass); traversing a **backward** (loop-back) sequence flow — the standard DFS back edge,
  precomputed once as `BpmnGraph.IsBackwardFlow` — mints a fresh key, and forward propagation inherits the
  emitting token's key. Join accounting groups arrivals by `(element, iteration key)`, so a join revisited
  across loop iterations never conflates iteration *N* with *N+1*. Every per-token construct re-arms on a
  revisit (a multi-instance host starts a second independent loop, a catch/boundary re-arms, an event
  gateway re-races). The structural rules still forbid a loop-back into a start event, a boundary event, or
  an event-gateway-armed catch. No loop-iteration variable is exposed and no runaway-loop guardrail is added
  (an un-terminating loop is the author's responsibility).
- Multi-inbound parallel joins wait for one arrival per inbound flow (per iteration key). Multi-inbound
  inclusive joins are activation-aware: they wait only while a live token or running child **of the same
  iteration** can still reach an un-arrived inbound flow. Everything else is an implicit XOR merge.
- A terminate end event consumes every live token and completes the composite; late completions of
  in-flight children are absorbed (Flowchart Break parity).
- Branch faults are fault-aware (Flowchart #308 parity): a faulted child whose host has no error boundary
  faults the composite deterministically (`bpmn.child.faulted`) instead of leaving a join waiting forever
  (a host **with** an error boundary absorbs it instead, see above). A parked join arrival that can never
  be satisfied faults as `bpmn.join.deadlock` instead of hanging.
- The engine snapshot is one typed, versioned activity private-state envelope
  (`Elsa.Bpmn.ExecutionState`, schema version 1) with prune-on-persist (consumed tokens, capped
  diagnostics). Terminal decisions raised mid-evaluation defer until the evaluation is quiescent
  (the runtime forbids terminal continuations that also schedule children).

## Slice scope and phasing

This module currently ships the Phase 1 core subset (see `specs/108-bpmn-container-activity/`) plus
the Phase 2 catch-events (see `specs/116-bpmn-catch-events/`), event-start (see
`specs/117-bpmn-event-start-events/`), event-based-gateway (see
`specs/119-bpmn-event-based-gateway/`), boundary-events (see `specs/120-bpmn-boundary-events/`), and
multi-instance (see `specs/121-bpmn-multi-instance/` and `specs/123-runtime-scoped-variable-read/`), and
cyclic-sequence-flow (see `specs/122-bpmn-cyclic-flows/`) slices: none/timer/message/signal start events,
none/terminate end events, timer/message/signal intermediate catch events, timer/message/signal/error
**boundary events**, cardinality and collection **multi-instance** (sequential + parallel) loops, task
family, embedded subprocess, and exclusive/parallel/inclusive/event-based gateways over **cyclic or acyclic**
graphs — loop-back sequence flows are executable via token iteration keys (spec 122). The interchange
importer/exporter round-trips timer/message/signal event definitions on event-defined start and intermediate
catch events (see `specs/118-bpmn-interchange-event-definitions/`) and round-trips the event-based gateway,
boundary event, and cardinality **and collection** multi-instance elements (collection variables carry as
`elsa:collection`/`elsa:itemVariable` plus `elsa:variable` declarations), so these constructs can now be
authored from XML; a cyclic document imports clean. Later units add a
loop-iteration variable surface, escalation/compensation boundaries, event subprocesses, transactions, and
call activities.

## Expression-driven gateway conditions

`BpmnDecision` is the expression-condition evaluator leaf: its `Outcome` input is evaluated by the
runtime's value binding (literal or any bound expression language) and the activity completes with
the evaluated string as its outcome name. Bind one to an exclusive/inclusive gateway and condition
the outbound flows on the values the expression can produce; a blank result matches nothing, so
routing falls through to the default flow (or faults deterministically when none is declared).

## Behavior contract extension point

Element semantics are extensible through the public `IBpmnElementBehavior` contract. Behaviors
receive a read-only `IBpmnBehaviorContext` and return `BpmnBehaviorDecision` commands;
`BpmnExecutionEngine` validates and applies those commands, keeping mutation and scheduling
authority inside the BPMN runtime. Built-in behavior families are defined in `BpmnElementFamilies`
and registered by `ActivitiesBpmnFeature`.
