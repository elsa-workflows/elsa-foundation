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
- **Compensation** (spec 124) lets a process undo completed work. A **compensation boundary** attaches a
  compensation **handler** activity (an `isForCompensation` task-family/`subProcess` element that binds a
  child, takes no sequence flows, and is reached by association — never by token flow) to a host. When the
  host's bound child completes successfully, the engine appends a **Registered** entry to a durable
  reverse-order **compensation log** (`BpmnExecutionState.Compensables`, `comp:N`), per completion (a host
  completed on multiple loop passes registers once per pass). A **compensate intermediate throw event** or
  **compensate end event** emits a single `TriggerCompensation` command; the engine claims the target
  `Registered` compensables atomically (all, or only an `activityRef` host's) in reverse registration order —
  newest-first — flipping them **Claimed** so concurrent throws claim disjoint targets. An empty selection
  completes the throw immediately (route/consume, no fault). Otherwise the throw token becomes an
  `AwaitingChild` **run coordinator** (`CompensationRuns`, `comprun:N`) that replays the handlers **one at a
  time**: each handler runs on a minted sub-token; its completion is intercepted before behavior dispatch
  (the multi-instance precedent), the compensable flips **Compensated**, and the next handler is scheduled or
  the run drops and the throw routes outbound (intermediate) / consumes (end). A handler fault rides the
  ordinary `bpmn.child.faulted` composite path (handlers host no boundary). Compensables are **never pruned**;
  the log is intra-process (the transactions unit rides it next). Behaviors stay semantics-unaware — the
  throw behaviors emit `TriggerCompensation` and nothing else; registration, ordering, claiming, and replay
  are engine-owned. Compensation on a multi-instance host, nested-process cascade, escalation, and
  compensation event subprocesses are stated cuts.
- **Transactions** (spec 125) let a `subProcess` model a **transaction** whose nested scope can be
  **cancelled from within**. The flag lives on **both** sides independently (isolation): the parent's
  `BpmnElement.IsTransaction` (drives cancel-boundary validation + the parent-side outcome mapping) and the
  nested `BpmnStructure.IsTransaction` (drives cancel-end validation + the contract's extra outcome). A
  **cancel end event** (only inside a transaction) emits a single `CancelTransaction` command; the engine
  stops all **other** live work logically (routing every other live token through `CancelTokenAndChild` so
  MI/race/compensation-run cascades tear down consistently, while in-flight children keep running and are
  absorbed on late completion — the terminate precedent), records a `Cancelling` verdict, then **claims every
  `Registered` compensable** (the whole scope, reverse registration order) and opens a spec-124
  `BpmnCompensationRun` coordinated by the cancel-end token — reusing the compensation replay machinery
  verbatim. When the replay finishes (or the log was empty) the process **completes with the distinguishable
  `Cancelled` outcome** instead of `Done` (`FinishEvaluation` grows the outcome-aware exit; the runtime's
  `Cancel` continuation is deliberately NOT used — it would never reach the parent's completion handler). The
  published contract declares `Cancelled` **in addition to** `Done` iff the authored structure is a
  transaction — the structure-dependent outcome pattern (VF-ACT-006 / FlowSwitch), via the
  `ExecutableNodeCompiler` outcome resolver reading the compiled `elsa.bpmn.structure` `isTransaction` flag.
  In the parent scope, a transaction child completing `Cancelled` is intercepted **before** normal routing and
  Case B registration: the host token is consumed, **no** compensable registers, **no** normal outbound
  routes, still-armed catch listeners tear down, and an `Active` token is minted at the attached **cancel
  boundary** (dormant, no listener, ≥1 outbound — the error-boundary minting pattern) to route the
  cancellation path; with no cancel boundary the parent faults `bpmn.transaction.cancelled-unhandled`. A
  transaction completing `Done` is byte-identical to a plain subprocess (normal routing + Case B). A handler
  fault mid-cancel rides the ordinary `bpmn.child.faulted` composite path (no `Cancelled` completion).
  Multi-instance transactions, auto-compensation when a transaction is interrupted from outside, escalation,
  transaction hazards, and nested-transaction cross-scope cascades are stated cuts.
- **Escalation** (spec 127) lets a nested process **signal upward**, riding **runtime seam C** (spec 126, the
  first consumer). An **escalation throw event** (intermediate) or **escalation end event** carries a required
  `code` and emits a single `RaiseEscalation` command (companion to `EmitTokens`/`ConsumeToken`); the engine
  reads the code from the element and, when the process has a committed parent, stages
  `RequestParentNotification("bpmn.escalation", { code, name? })` on the process's own Defer/Complete commit —
  at a **root** process it is a no-op with an `EscalationUnhandled` diagnostic (an escalation nobody can catch
  is a signal, not an error). `BpmnProcess` implements `IRuntimeActivityChildNotificationHandler`;
  `OnChildNotifiedAsync` resolves the notifying child to its **host element** (graph-derived from the child's
  node id, robust to the late case), matches the payload code against the host's attached **escalation
  boundaries** — exact code beats the code-less **catch-all** — and fires. A **non-interrupting** match
  (`CancelActivity = false`) mints an `Active` boundary token alongside the untouched host and routes (repeated
  notifications fire repeatedly); an **interrupting** match cancels the host token through the existing
  `CancelTokenAndChild` cascade (nested subtree torn down via seam A, MI coordinator cascade honored, reason
  `bpmn.escalation.host-interrupted`) before minting the boundary token. An **unmatched** escalation **bubbles**
  — re-staged verbatim to the grandparent when this process itself has a parent (consumer-side recursion, one
  hop per level), else a root `EscalationUnhandled` no-op. **Late races** are deterministic and never fault:
  once the host terminalized, a non-interrupting boundary **still fires** (additive) while an interrupting one
  **no-ops** with an `EscalationLate` diagnostic. Escalation boundaries are dormant (no listener child), attach
  only to a `subProcess` host, honor `CancelActivity`, and per host carry **distinct codes** with **≤1**
  catch-all. Any non-escalation seam-C code is a forward-compatible diagnostic pass-through. No new BPMN state
  record or token status is introduced; the notification evaluation rides `FinishEvaluation`'s existing exits,
  and the bubble/seam-A staging happens only at the clean Defer/Complete exit. Escalation intermediate catch
  events remain a stated cut (not a BPMN construct — boundary/event-subprocess only).
- **Event subprocesses** (spec 128, tier 1) let a scope contain a flow-less `subProcess` marked
  `triggeredByEvent` whose **body** activates when its **start-event trigger** fires while the enclosing scope
  is active. This slice ships the **escalation** dormant-catcher trigger (interrupting or non-interrupting) and the
  **error** trigger (always interrupting; see the end of this entry). Catchers are **graph-derived** on `BpmnGraph`
  (`EventSubprocesses`, indexed by trigger kind + code); no state record, no arming. Validation
  (`ValidateEventSubprocesses`, the compensation-handler rule family mirrored): the element takes no flows, no
  loop, hosts no boundary, is not a compensation handler nor a transaction; its body declares **exactly one**
  start event carrying **one** supported trigger (escalation with optional code = catch-all, or error catch-all),
  with **no nested** event subprocess; the interrupting flag is the body start event's `isInterrupting`
  (default true; error must be interrupting); per scope escalation codes are distinct with **≤1** catch-all and
  **≤1** error subprocess. Bodies are seeded through the **scheduled-start seeding** path: `BpmnScheduler`
  forwards the body's single event-start element id as a command-metadata **hint** (gated on a body scheduling
  cause so an inherited hint never contaminates an ordinary nested process), and `BpmnProcess.StartAsync` gains
  a third seeding path that seeds exactly that start element (a bad hint faults deterministically); root-trigger
  and direct-invocation seeding are byte-identical when the hint is absent. **Own-scope catching**: `RaiseEscalation`
  consults the throwing scope's own escalation event subprocesses **first** (exact beats catch-all) and activates
  one **instead of** staging upward — preserving one-hop bubbling. **Notification-side ladder** (spec 128 D3):
  (1) host boundary exact, (2) scope event subprocess exact, (3) host boundary catch-all, (4) scope event
  subprocess catch-all, (5) bubble. **Activation** mints a scope-level activation token (`AwaitingChild`,
  `ParentTokenId` null, inheriting the triggering context's iteration key) and schedules the body with the hint;
  an **interrupting** activation first stops all other live work through the shared `StopOtherLiveWork` helper
  (extracted from spec 125's cancel-transaction stop-others loop — the transaction path stays byte-identical),
  reason `bpmn.event-subprocess.scope-interrupted`; a **non-interrupting** escalation activation runs alongside
  (repeated/concurrent activations are independent). Body completion is intercepted before behavior dispatch
  (the element has no flows): the activation token is consumed (`EventSubprocessCompleted`) and nothing routes;
  a body fault rides the ordinary composite-fault path (no self-catching). Compensation/conditional triggers,
  error-code matching, nested event subprocesses inside a body, and Studio authoring are stated cuts.
  **Error trigger** (spec 132): an error-triggered event subprocess absorbs the host's child fault via **seam B**
  (the incident resolves) and then activates its body — a scheduled child, so the fault evaluation **defers**.
  Executable since the runtime deferred-seam-B **metadata-leak fix (#989)** landed (`WorkflowParentActivityCompletionSchedulerWorkHandler`
  no longer inherits the fault-evaluation work item's fault-scoped `CommandMetadata` onto derived child schedules /
  upward completions). It is **always interrupting** (per BPMN) and **catch-all** (no error-code matching this
  slice); a scope carries **≤1**. `AbsorbChildFaultThroughErrorEventSubprocess` drops the faulted child's record,
  mints the scope activation token, stops all other live work, and schedules the body; the incident records
  `bpmn.event-subprocess.error-absorbed`. A non-interrupting error start is rejected as a malformed shape (error
  events are always interrupting). Error-code matching remains a stated cut.
  **Message/signal/timer triggers** (spec 134, tier 2): unlike the dormant escalation/error catchers, these need a
  **scope listener** — a suspending catch child (the spec-116 `Event`/`Delay` machinery) armed at scope start that
  waits for an external stimulus while the rest of the scope runs, and that must **never block completion**. Two
  additive fields carry it: `BpmnToken.Kind` (nullable `Listener`|`Activation`, stamped at the listener/activation
  mints, `null` everywhere else — an additive **role** field, not a token status), and `BpmnElement.ListenerNodeId`
  (a second bound-child channel referencing the synthesized `Delay`/`Event` node — **required** on a message/signal/
  timer event subprocess, **forbidden** on escalation/error; the exactly-one-binding accounting binds a node as
  either a `ChildNodeId` or a `ListenerNodeId`, never both). **Arming** is two-phase at every `StartAsync` seeding
  path (deterministic element-id ordinal): the listener **tokens** are minted before the seed propagates (so an
  interrupting activation raised by that propagation drains them as ordinary live tokens), and their suspending
  **children** are scheduled after, only when real work remains (the runtime forbids scheduling children in a
  terminal evaluation — a scope whose seed completes synchronously completes with no listener ever armed). **Firing**
  is intercepted in `OnChildCompletedAsync` before the tier-1 body-completion check (both tokens sit at the same
  element — discriminate on `Kind == Listener`): **non-interrupting** consumes the listener, **re-arms** a fresh one
  (deterministic `Sequence` ids; timer repetition falls out of the re-arm loop), then activates the body alongside
  untouched work; **interrupting** consumes the listener and activates, `StopOtherLiveWork` draining sibling
  listeners with no re-arm. **Completion is never blocked** (`FinishEvaluation`): both the clean-completion check and
  the join-deadlock detector compute liveness over **non-listener** tokens and children (patched together — excluding
  listeners from completion alone would misfire the deadlock detector on a listener-plus-real-join state); when only
  listeners remain, they are torn down (`CancelTokenAndChild`, reason
  `bpmn.event-subprocess.listener-superseded-by-completion`, token-id ordinal) and the scope completes normally. The
  unfiltered teardown sites (`CancelLiveWork`, `StopOtherLiveWork`) leave listeners to die with the scope. Diagnostics
  `ScopeListenerArmed`/`ScopeListenerFired`/`ScopeListenerRetired`. `timeCycle`/`timeDate`/cron event-subprocess
  timers, correlation-scoped delivery beyond the shipped `Event` stimulus, and re-arm throttling are stated cuts.
- **Call activities** (spec 133) let a process invoke a **separately published Elsa workflow** by binding a
  `Elsa.DispatchWorkflow` child. A `callActivity` element resolves to the **task family** (behaviors stay
  semantics-unaware; `TaskBehavior` unchanged), is a boundary host, and may carry multi-instance loop
  characteristics. The child is authored `WaitForCompletion=true` by convention: the waited path
  (`ScheduleChild → AwaitingChild → bookmark → resume → OnChildCompleted`) is the shipped dispatch machinery,
  so the engine needs **no** wait changes. The one new engine piece is **failure-outcome translation** (D3): a
  faulted/cancelled/dispatch-failed called workflow **COMPLETES** the DispatchWorkflow child with an outcome
  (`Faulted`/`DispatchFailed`/`Cancelled`) — it never faults, produces no incident, so it cannot ride seam B.
  `OnChildCompletedAsync` intercepts these outcome completions on a `callActivity`-bound child **before** normal
  routing (joining the MI/compensation/transaction/event-subprocess interception ladder) and routes the
  error-catcher ladder **directly, with no seam B**: (1) a host-attached **error boundary** mints an `Active`
  token at the boundary (inheriting the interrupt target's iteration key) and propagates; (2) else the scope's
  **error event subprocess** activates interrupting (spec-128 path); (3) else the process faults deterministically
  (`bpmn.call-activity.faulted` / `.dispatch-failed` / `.cancelled`). `Completed`/`Dispatched` are untouched
  (normal task-flow routing — a `conditionOutcome` flow can still discriminate them). A multi-instance instance
  failure rides this per instance, composed with the spec-121 coordinator cascade (the interrupt target is the
  loop coordinator, so firing a catcher interrupts every remaining instance); the diagnostic is
  `CallActivityFailureRouted`. Fire-and-forget (`WaitForCompletion=false`) is a documented non-standard Elsa
  extension: the `Dispatched` outcome routes normal outbound immediately. **Stated cuts** (D6): mid-process
  teardown of a waited call activity does not cancel the dispatched child instance (whole-parent-cancel does);
  child-outputs→process-variables capture (pending the ADR-0046 leaf output-capture wiring); BPMN
  `ioSpecification`/data associations; MI output aggregation; `calledElement` version-selection/cross-tenant
  semantics; callActivity-specific Studio UX.
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
multi-instance (see `specs/121-bpmn-multi-instance/` and `specs/123-runtime-scoped-variable-read/`),
cyclic-sequence-flow (see `specs/122-bpmn-cyclic-flows/`), compensation (see
`specs/124-bpmn-compensation/`), and transactions (see `specs/125-bpmn-transactions/`) slices:
none/timer/message/signal start events, none/terminate/**compensate**/**cancel** end events,
timer/message/signal intermediate catch events, **compensate intermediate throw events**,
timer/message/signal/error/**compensation**/**cancel** **boundary events**, **transaction** subprocesses,
compensation **handler** activities, cardinality and collection **multi-instance** (sequential + parallel) loops, task
family, embedded subprocess, and exclusive/parallel/inclusive/event-based gateways over **cyclic or acyclic**
graphs — loop-back sequence flows are executable via token iteration keys (spec 122). The interchange
importer/exporter round-trips timer/message/signal event definitions on event-defined start and intermediate
catch events (see `specs/118-bpmn-interchange-event-definitions/`) and round-trips the event-based gateway,
boundary event, cardinality **and collection** multi-instance elements (collection variables carry as
`elsa:collection`/`elsa:itemVariable` plus `elsa:variable` declarations), and **compensation** (boundary +
`compensateEventDefinition` + `<association>` to an `isForCompensation` handler; compensate throw/end with an
optional `activityRef`) and **transactions** (`<transaction>` + `cancelEventDefinition` on an end event inside
a transaction and on a boundary attached to a transaction host) and **call activities** (`<callActivity>` with
an `elsa:workflowDefinitionId` extension attribute → a bound `DispatchWorkflow` child, honoring
`elsa:waitForCompletion="false"`; a plain `calledElement` imports unbound with an Info finding and a
`bpmn.calledElement` passthrough — see `specs/133-bpmn-call-activity/`), so these constructs can now be authored
from XML; a cyclic document imports clean. Later units add a loop-iteration variable surface, escalation
boundaries, and compensation event subprocesses.

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
