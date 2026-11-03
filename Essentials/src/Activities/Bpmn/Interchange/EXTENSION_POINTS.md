# Elsa.Activities.Bpmn.Interchange Extension Points

## Replaceable service contracts

### `IBpmnDocumentImporter`

- **Kind:** Replacement contract
- **Contract:** `Elsa.Activities.Bpmn.Interchange.Contracts.IBpmnDocumentImporter`
- **Default:** `BpmnDocumentImporter` *(intra-domain — default)*
- **Signature:** [IBpmnDocumentImporter](Contracts/IBpmnDocumentImporter.cs): Import/Analyze perform
  pure conversion; ImportAsync/AnalyzeAsync also resolve references against the visible activity catalog.
- **Registration:** Scoped via `ActivitiesBpmnInterchangeFeature`; register one replacement before the
  feature. Shell startup rejects missing or competing registrations.
- **Override when:** the host replaces document ingestion, retaining the authored contract and explicit
  degradation/availability reporting. Changing only activity mapping uses IBpmnWorkBinder instead.

### `IBpmnDocumentExporter`

- **Kind:** Replacement contract
- **Contract:** `Elsa.Activities.Bpmn.Interchange.Contracts.IBpmnDocumentExporter`
- **Default:** `BpmnDocumentExporter` *(intra-domain — default)*
- **Signature:** [IBpmnDocumentExporter](Contracts/IBpmnDocumentExporter.cs): Export returns BPMN XML.
- **Registration:** Scoped; one replacement before the feature, with the same startup exclusivity check.
- **Override when:** the host replaces export while retaining the authored/runtime compatibility boundary.

### `IBpmnWorkBinder`

- **Kind:** Scoped single-implementation replacement contract.
- **Contract / signature:** [IBpmnWorkBinder](Contracts/IBpmnWorkBinder.cs), Bind(BpmnWorkBindingRequest)
  returns BpmnWorkBindingResult with the activity and explicit availability/reason.
- **Default / owner:** internal `BpmnWorkBinder`, in `Elsa.Activities.Bpmn.Interchange`.
- **Registration:** TryAddScoped; an explicit host policy before the feature is preserved. Shell startup
  rejects duplicates. Importer/exporter/binder registration has positive and removal controls.
- **Override when:** a host supplies different concrete work activities. Preserve exact binding identity,
  nested/listener CanStartWorkflow=false, unavailable/unknown reporting and catalog validation; token
  routing remains in the released interpreter.

Synchronous conversion retains legacy placeholder version references and reports unverified mappings
as Unknown. The registered HTTP endpoints use the asynchronous catalog path: it resolves visible,
active recommended 1.0.0 profile versions (including build metadata) to their real catalog IDs, preserving the existing contracts
instead of silently selecting a newer version. Missing inventory or recommendation cannot qualify CanExecute; normal
publication validation remains required. The timer binding converts ISO duration text to an invariant
TimeSpan literal for Delay; the authored event descriptor retains the XML text for export. This check is distinct from runtime/Studio qualification.

## Owned interchange contracts

- `elsa:conditionOutcome` attribute (namespace `https://elsa-workflows.io/schemas/bpmn`) on
  `sequenceFlow` — the lossless XML carrier for outcome-matched flow conditions.
- The authored `diagram` payload shape (`shapes: {id: {x,y,width,height}}`,
  `edges: {id: {waypoints: [{x,y}]}}`) mirrors BPMNDI and is treated as opaque by the runtime.
- **Event-definition mapping (spec 118)** between BPMN XML and `BpmnEventDefinition` properties:
  `messageEventDefinition`/`signalEventDefinition` `messageRef`/`signalRef` ↔ root `<message>`/`<signal>`
  declaration `name` ↔ `BpmnEventDefinitionProperties.Name`; start `timerEventDefinition` `<timeCycle>` ↔
  `Cron`/`Interval` (the `P`/`R` discriminator); catch `timerEventDefinition` `<timeDuration>` ↔
  `Interval`. The root declaration id is the deterministic `message-{name}` / `signal-{name}`. A timer
  catch event synthesizes a bound `Delay` child (`Duration` literal); a message/signal catch event
  synthesizes a bound `Event` child (`EventName` literal + `CanStartWorkflow = false`) — these carry the
  placeholder activity version ids `BpmnDocumentImporter.DefaultDelayActivityVersionId` (`"Elsa.Delay"`)
  and `DefaultEventActivityVersionId` (`"Elsa.Event"`), which hosts resolve to real catalog rows.

## Owned HTTP endpoints

- Optional `ActivitiesBpmnDirectPublishing`: `POST interchange/bpmn/versions/{versionId}/publish-direct`, capability `elsa.api.bpmn-publishing` with relations `bpmn-authored-publish`, `bpmn-authored-service-publish` and `bpmn-authored-claim-publish` on the same route. Requires both `bpmn:publish` and existing workflow publication management permission for the actual source version, and `workflows/runtime:read` on each selected service source reference. Its keyed `BpmnDirectPublishingFaultRenderer` is a cross-domain `IEndpointFaultRenderer` contribution. Reads only immutable Design versions; runtime commands use the BPMN Core replacement port and existing store fencing. No event handler, background task or scheduler is added.

- `POST interchange/bpmn/analyze` / `import` / `export` (`Endpoints/BpmnInterchangeEndpoints.cs`),
  permission-gated by `bpmn-interchange:read` / `bpmn-interchange:manage`, mapped explicitly by the
  owner's Minimal API mapper.

## Consumed contracts

- `Elsa.Activities.Bpmn` models (`BpmnAuthoredStructure`, `BpmnElement`, `BpmnSequenceFlow`, …) and
  the `elsa.bpmn.structure` kind/schema constants on `BpmnProcess`.
- `Elsa.Workflows.Design.Core` `ActivityNode` / `ActivityNodeStructure` as the import/export unit.
