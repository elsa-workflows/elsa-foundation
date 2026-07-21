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
- Expression flow conditions import as unconditional flows (reported); other unsupported flow nodes
  (boundary events, event-based gateways, call activities, …) are dropped with an issue. Cyclic graphs
  import but are flagged as not executable in this slice.
- BPMNDI shapes/edges are preserved verbatim on the authored `diagram` payload.

## Export

`IBpmnDocumentExporter.Export` emits BPMN 2.0 XML + BPMNDI. Outcome-matched flow conditions have no
standard BPMN representation, so they export as `elsa:conditionOutcome` attributes (namespace
`https://elsa-workflows.io/schemas/bpmn`) that the importer reads back for lossless round-trips; a
human-readable `conditionExpression` is emitted alongside for other modelers. Event-defined start and
catch elements emit their `timer`/`message`/`signal` definitions from the populated `BpmnEventDefinition`
properties (start timers as `<timeCycle>`, catch timers as `<timeDuration>`); each distinct message/signal
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
is mapped — the synthesized `Event` catch child leaves `CorrelationId` unset); absolute (`<timeDate>`)
and non-recurring start timers; boundary events, event-based gateways, and event subprocesses (still
dropped); `extensionElements` preservation for third-party vendor attributes; collaboration/pool export;
and the BPMN MIWG conformance corpus.
