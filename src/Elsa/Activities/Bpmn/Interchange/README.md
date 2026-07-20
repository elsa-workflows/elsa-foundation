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
- Event-defined start events degrade to none start events (reported); expression flow conditions
  import as unconditional flows (reported); unsupported flow nodes (boundary events, intermediate
  events, call activities, …) are dropped with an issue. Cyclic graphs import but are flagged as not
  executable in this slice.
- BPMNDI shapes/edges are preserved verbatim on the authored `diagram` payload.

## Export

`IBpmnDocumentExporter.Export` emits BPMN 2.0 XML + BPMNDI. Outcome-matched flow conditions have no
standard BPMN representation, so they export as `elsa:conditionOutcome` attributes (namespace
`https://elsa-workflows.io/schemas/bpmn`) that the importer reads back for lossless round-trips; a
human-readable `conditionExpression` is emitted alongside for other modelers. Layout comes from the
authored `diagram` payload; a simple grid is synthesized for elements without one.

## HTTP surface

Ingestion-style endpoints (no API capability, mirroring the Elsa3 reusable-import surface); all
three are pure conversions — nothing is persisted server-side:

- `POST interchange/bpmn/analyze` (`bpmn-interchange.read`) — inventory a document before import.
- `POST interchange/bpmn/import` (`bpmn-interchange.manage`) — document → `BpmnProcess` node + analysis.
- `POST interchange/bpmn/export` (`bpmn-interchange.read`) — `BpmnProcess` node → BPMN XML + DI.

## Not yet in this slice

The Studio import/export UX, `extensionElements` preservation for third-party vendor attributes,
collaboration/pool export, and the BPMN MIWG conformance corpus.
