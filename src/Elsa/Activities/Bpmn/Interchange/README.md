# Elsa.Activities.Bpmn.Interchange

Design-time BPMN 2.0 XML + BPMNDI import/export for the `BpmnProcess` composite
(`Elsa.Activities.Bpmn`). The runtime module carries no XML machinery; this module converts between
the native `elsa.bpmn.structure` authored payload and interchange XML so diagrams round-trip with
Camunda Modeler and other BPMN tools.

## Import (analyze-then-commit)

`IBpmnDocumentImporter.Analyze` inventories a document (process ids, element counts, issues) without
creating anything; `Import` produces a `BpmnProcess` `ActivityNode` with the authored structure.
Mirroring the Elsa3 reusable-import flow, callers show the analysis before committing.

Mapping rules for this slice:

- Phase 1 elements (none start/end events, terminate end events, the task family, subprocesses, and
  exclusive/parallel/inclusive gateways with `default` flows) import cleanly. Lanes map to the lane
  model and stamp `laneId` on their referenced elements.
- An embedded `subProcess` becomes a nested `BpmnProcess` activity node bound by the subprocess
  element — the same composition the runtime module executes.
- **Event-defined start events** (spec 117) with exactly one `timer`/`message`/`signal` definition
  import as pure elements carrying a populated `BpmnEventDefinition`: `messageRef`/`signalRef` resolve
  through the root `<message>`/`<signal>` declarations to the `name` property; a start `<timeCycle>`
  maps to the `cron` property (a cron expression) or the `interval` property (a `P`/`R`-prefixed
  ISO-8601 duration, with any leading `R…/` repetition stripped). An unresolvable start (unresolvable
  ref/blank name, multiple/unsupported definitions, or a non-recurring `<timeDuration>`/`<timeDate>`)
  degrades to a none start with a reported issue.
- **Intermediate catch events** (spec 116) with exactly one `timer`/`message`/`signal` definition
  import with a populated `BpmnEventDefinition` **plus** a synthesized bound suspending child in the
  `Bpmn.Activities` slot (`node-{id}`): a `Delay` (`Duration` literal) for a `<timeDuration>` catch
  timer, an `Event` (`EventName` literal + `CanStartWorkflow = false`) for a message/signal catch. A
  catch with zero/multiple/unsupported definitions, an unresolvable ref, or a recurring/absolute
  (`<timeCycle>`/`<timeDate>`) timer is dropped with an issue (its sequence flows cascade-drop).
- **Boundary events** (spec 120) resolve in a second pass (after all hosts are known, so attachment is
  document-order-independent): a timer `<timeDuration>` boundary synthesizes a bound `Delay` listener child,
  a message/signal boundary an `Event` listener child, and an `errorEventDefinition` an error boundary (no
  child). `cancelActivity` (absent → `true`) carries the interrupting flag; `attachedToRef` names the host.
  A boundary attached to a missing element, a non-host family, or a **childless** host — or a
  non-interrupting error boundary, or a `<timeCycle>`/`<timeDate>` boundary timer — is dropped with an issue
  (validate-representable: the importer never emits a boundary the graph validator would reject; its
  sequence flows cascade-drop). `errorRef` is recorded verbatim into the element properties for future
  error-code matching (not read this slice).
- **Multi-instance loops** (spec 121/123) on a task/subprocess host: a `<multiInstanceLoopCharacteristics
  isSequential="…">` with an integer literal `<loopCardinality>` imports as a cardinality
  `BpmnLoopCharacteristics` (`isSequential` absent → `false`). **Collection mode** (via the elsa-namespaced
  `elsa:collection`/`elsa:itemVariable` attributes) imports as a real collection-mode loop **when
  `elsa:collection` names a container-scoped variable declared on the process** (via an
  `<extensionElements><elsa:variable name="…"/></extensionElements>` declaration) and the item variable is
  not the reserved `loopIndex`; an undeclared/empty collection name, a reserved item variable, a
  non-integer/missing cardinality, a `standardLoopCharacteristics`, or a host that binds no child on import (a
  plain task) **degrade** to a host WITHOUT loop characteristics with a finding (validate-representable — the
  importer never emits a loop the graph validator would reject).
- **Compensation** (spec 124): a `boundaryEvent` with a `compensateEventDefinition` resolves its handler via a
  container-level `<association>` (either direction) to an importable task-family/`subProcess` that binds a
  child, setting `CompensationHandlerElementId` and marking the handler `IsForCompensation` (its
  `isForCompensation="true"` attribute is also honored); `cancelActivity` is imported but ignored. A
  compensation boundary with no resolvable association is dropped with a finding (no flow cascade — it has no
  flows), and an `isForCompensation` activity referenced by no compensation boundary is dropped with a finding
  (it cannot ride normal flow). An `intermediateThrowEvent`/`endEvent` with a `compensateEventDefinition` imports
  as a compensate throw/end (carrying the optional `activityRef` property); an unresolvable `activityRef` (naming
  no element with an attached compensation boundary) drops the throw with a finding (its flows cascade-drop) and
  degrades the end to a plain none end event with a finding, so the importer never emits a graph the validator
  rejects. An `intermediateThrowEvent` with any other (or no) definition stays dropped.
- **Transactions** (spec 125): a `<transaction>` imports exactly like a `<subProcess>` (a nested `BpmnProcess`
  activity node bound by the element) plus `IsTransaction = true` on **both** the element and the nested
  authored structure. A `cancelEventDefinition` on an `endEvent` **inside** a transaction imports as a cancel
  end event; **outside** a transaction it degrades to a none end event with a finding (the validator would
  reject it). A `cancelEventDefinition` on a `boundaryEvent` attached to a transaction host imports as a cancel
  boundary; attached to a non-transaction host, or a second one on the same transaction, it drops with a
  finding (its flows cascade-drop). Inner spec-124 compensation constructs inside a transaction resolve as
  usual (the transaction is imported recursively).
- **Escalation** (spec 127): root `<escalation id name escalationCode>` declarations index by id (the
  `<message>`/`<signal>` precedent); an `escalationEventDefinition`'s `escalationRef` resolves to the matching
  code via the fallback chain `escalationCode → declaration name → ref id`. On an `intermediateThrowEvent` →
  escalation throw, on an `endEvent` → escalation end (both carry the resolved `code` + optional `name`); a
  **ref-less** throw is **dropped** with a finding (its flows cascade-drop), a **ref-less end** degrades to a
  none end event (no flows to cascade). On a `boundaryEvent` attached to a **subprocess** host → escalation
  boundary (ref-less = code-less catch-all); attached to a **task-family** host (childless on import), a
  **colliding code**, or a **second catch-all** on one host → dropped with a finding (validate-representable).
- **Event subprocesses** (spec 128, tier 1): a `<subProcess triggeredByEvent="true">` imports as a
  `TriggeredByEvent` element bound to a nested body; the body's single event-start declares the trigger — an
  `escalationEventDefinition` (with `escalationRef` resolving to a code = catch-all when ref-less) or an
  `errorEventDefinition`, its `isInterrupting` (default `true`) mapped onto the body start event's flag. The
  importer validates the body shape and per-scope uniqueness **before** emitting, so it never emits a
  validator-rejected graph: **dropped** with a specific finding when the body has no/multiple start events, an
  unsupported trigger (compensation/conditional/… → not a supported event-subprocess trigger), a colliding escalation
  code, a second catch-all in one scope, or a second / non-interrupting error event subprocess. An **error**-triggered
  event subprocess is **executable** since the runtime deferred-seam-B fix (#989, spec 132): it imports as an
  (always-interrupting, catch-all) error trigger; a scope carries **≤1**, and a non-interrupting error start
  degrades (Dropped + finding) as a malformed shape. **Message/signal/timer** triggers (spec 134): a body start
  carrying a `messageEventDefinition`/`signalEventDefinition` (root-index name resolution) or a `timerEventDefinition`
  with a one-shot `<timeDuration>` imports **with a synthesized scope-listener node** (`node-{id}-listener`, an
  `Event` wait for message/signal or a `Delay` for timer, reusing the spec-116/118 catch-child synthesis) and a
  `ListenerNodeId` on the element; a `<timeCycle>`/`<timeDate>`/cron timer degrades (the body start becomes a none
  start → the event subprocess drops). **Export**: `triggeredByEvent="true"` on the `<subProcess>` plus the body's
  event-start with its definition and `isInterrupting="false"` only when non-interrupting (a timer body start exports
  a `<timeDuration>`, not the recurring `<timeCycle>` a root timer start uses); the synthesized listener node is **not**
  exported (re-synthesized on import); escalation codes dedupe through the root `<escalation>` declarations. Round-trips
  hold for escalation (interrupting / non-interrupting / catch-all), error (always interrupting, catch-all), and
  message + signal + timer (interrupting + non-interrupting).
- **Call activities** (spec 133): a `<callActivity>` carrying the Elsa extension attribute
  `elsa:workflowDefinitionId` (our export convention) imports **bound** — a `DispatchWorkflow` child is
  synthesized (`WorkflowDefinitionId` literal + `WaitForCompletion`, honoring `elsa:waitForCompletion="false"`
  for fire-and-forget; the spec-118 synthesized-child pattern). A plain `<callActivity calledElement="…">` (a
  foreign BPMN process id with no guaranteed Elsa definition mapping) imports **unbound** with an **Info** finding
  ("imported unbound; bind a DispatchWorkflow activity to execute it" — the serviceTask precedent); the
  `calledElement` is recorded as `Properties["bpmn.calledElement"]` (the `bpmn.errorRef` passthrough precedent) in
  either case for authoring reference and lossless round-trip. The importer never emits a validator-rejected graph
  (a call activity validates as a task-family element, bound or childless). Publish-time pinning means import never
  needs to resolve the definition.
- Expression flow conditions import as unconditional flows (reported); other unsupported flow nodes are
  dropped with an issue. **Cyclic graphs import clean** — a
  loop-back sequence flow is executable (spec 122: token iteration keys), so the former cycle degradation
  finding is gone; the graph validator's structural rules still constrain where a loop-back may land.
- BPMNDI shapes/edges are preserved verbatim on the authored `diagram` payload.

## Export

`IBpmnDocumentExporter.Export` emits BPMN 2.0 XML + BPMNDI. Outcome-matched flow conditions have no
standard BPMN representation, so they export as `elsa:conditionOutcome` attributes (namespace
`https://elsa-workflows.io/schemas/bpmn`) that the importer reads back for lossless round-trips; a
human-readable `conditionExpression` is emitted alongside for other modelers. Event-defined start and
catch elements emit their `timer`/`message`/`signal` definitions from the populated `BpmnEventDefinition`
properties (start timers as `<timeCycle>`, catch/boundary timers as `<timeDuration>`); a boundary event
emits `attachedToRef`, `cancelActivity="false"` only when non-interrupting (omitted when interrupting, since
the importer defaults absent → true), and its `error`/`timer`/`message`/`signal` definition child. A
multi-instance host emits a `<multiInstanceLoopCharacteristics isSequential="…">` with its
`<loopCardinality>` (cardinality mode) or its `elsa:collection`/`elsa:itemVariable` attributes (collection
mode; `elsa:itemVariable` is emitted explicitly even at its default), and the process's container-scoped
variables emit as `<extensionElements><elsa:variable name="…"/></extensionElements>` so a collection loop's
declared variable survives the round-trip. A **compensation** boundary emits a `compensateEventDefinition`
plus a container-level `<association>` derived from its handler reference; the handler emits with
`isForCompensation="true"`; a compensate throw/end emits its `compensateEventDefinition` (with the optional
`activityRef`). Compensation boundary/throw/end shapes ride the existing 36×36 event DI bounds; the association
emits no BPMNEdge (this exporter emits no DI edges for connectors at all — a documented limitation, not
compensation-specific). A **transaction** element emits `<transaction>` (everything else identical to a
subprocess); a cancel end event emits `<endEvent><cancelEventDefinition/></endEvent>` and a cancel boundary
emits `<boundaryEvent attachedToRef="…"><cancelEventDefinition/></boundaryEvent>`, both riding the existing
event DI bounds (no association involved). An **escalation** throw/end/boundary emits an
`<escalationEventDefinition>` with an `escalationRef` pointing at a deduped root `<escalation
id="escalation-{code}" escalationCode="{code}" [name]>` declaration (message/signal precedent); a code-less
catch-all boundary emits a ref-less `<escalationEventDefinition/>` and contributes no root declaration. An
**event subprocess** emits `<subProcess triggeredByEvent="true">` with its body content, and the body's
event-start emits its `escalationEventDefinition`/`errorEventDefinition`/`messageEventDefinition`/
`signalEventDefinition`/`timerEventDefinition` (a message/signal/timer body start emits `isInterrupting="false"` when
non-interrupting, and a timer emits a one-shot `<timeDuration>`; its synthesized scope-listener node is not exported). A **call activity** emits `<callActivity>` with `calledElement` (the `bpmn.calledElement`
passthrough when present, else the bound child's authored `WorkflowDefinitionId`), the
`elsa:workflowDefinitionId` extension attribute when bound, and `elsa:waitForCompletion="false"` only when the
bound child is authored fire-and-forget (the waited-by-default convention omits it); the bound `DispatchWorkflow`
child node is an Elsa concern the importer re-establishes, so it is not inlined. Bound and unbound call activities
both round-trip. Each distinct message/signal
name emits one deduped root `<message>`/`<signal>` declaration (deterministic id `message-{name}` /
`signal-{name}`) that the element's `messageRef`/`signalRef` targets — a name that sanitizes to a
colliding id shares a declaration. A catch event's bound `Delay`/`Event` child is engine detail and is not
exported (the importer re-synthesizes it). Layout comes from the authored `diagram` payload; a simple grid
is synthesized for elements without one.

## HTTP surface

Ingestion-style endpoints (no API capability, mirroring the Elsa3 reusable-import surface); all
three are pure conversions — nothing is persisted server-side:

- `POST interchange/bpmn/analyze` (`bpmn-interchange.read`) — inventory a document before import.
- `POST interchange/bpmn/import` (`bpmn-interchange.manage`) — document → `BpmnProcess` node + analysis.
- `POST interchange/bpmn/export` (`bpmn-interchange.read`) — `BpmnProcess` node → BPMN XML + DI.

## Not yet in this slice

The Studio import/export UX; message/signal **payload and correlation** mapping (only the event `name`
is mapped — the synthesized `Event` catch/boundary child leaves `CorrelationId` unset); absolute
(`<timeDate>`) and non-recurring start timers; error-code (`errorRef`) matching on error boundaries;
`timeCycle`/`timeDate`/cron **event-subprocess** timers (spec 134 ships one-shot `<timeDuration>` message/signal/timer
event subprocesses; recurring shapes still degrade); connector (sequence-flow and
association) BPMNEdge DI; `extensionElements` preservation for third-party vendor attributes;
collaboration/pool export; and the BPMN MIWG conformance corpus.
