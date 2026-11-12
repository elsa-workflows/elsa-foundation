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
- **Signature:** [IBpmnDocumentExporter](Contracts/IBpmnDocumentExporter.cs): `Export` returns BPMN XML without any
  task binding; `ExportAsync` returns `BpmnExportResult` (the XML and one `BpmnExportOmission` per task binding left out).
- **Catalog:** the default resolves task bindings through `IActivityDefinitionLookup` when one is registered; without it
  every task binding is omitted as `catalog-unavailable`. A replacement must keep that fail-closed rule.
- **Registration:** Scoped; one replacement before the feature, with the same startup exclusivity check.
- **Override when:** the host replaces export while retaining the authored/runtime compatibility boundary.
- Complete-source schema 2 returns its canonical XML unchanged. `ExportAsync` reports the disjoint native,
  scope and process invocation configuration omitted from that XML. Process identity changes require an
  explicit reference transform. A legacy schema reader refuses complete-source authority fields before deserialization.

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
publication validation remains required. A retained task binding (`elsa:taskBinding`) is proposed on its capability
(`RetainedBinding`) and keeps the task `RequiresBinding`; only an element named in `BpmnImportOptions.ConfirmedBindings`
becomes a `RetainedBinding` child, and the catalog pass refuses it as Unsupported when it names a binding-profile placeholder,
when this host's version is missing or unreadable, or when the version does not declare a carried input or declares it
sensitive, credential or connection. A replacement importer must keep these rules: propose only, stay `RequiresBinding`
until confirmed, and check a confirmed binding against the importing host's own catalog. The timer binding converts ISO duration text to an invariant
TimeSpan literal for Delay; the authored event descriptor retains the XML text for export. This check is distinct from runtime/Studio qualification.

## Owned interchange contracts

- `elsa:conditionOutcome` attribute (namespace `https://elsa-workflows.io/schemas/bpmn`) on
  `sequenceFlow` — the lossless XML carrier for outcome-matched flow conditions.
- `elsa:taskBinding` attribute on a `task`, `userTask`, `serviceTask`, `scriptTask`, `manualTask` or `businessRuleTask`:
  JSON `{"activityVersionId": "...", "inputs": {"<referenceKey>": <literal JSON>}}`, at most 16 KiB UTF-8 and depth 32.
  Export writes it only when the visible catalog resolves the version and declares every bound input, the binding has
  no output captures, storage, evaluation or conversion settings, every input is a literal not marked sensitive on the
  binding, and no declaration is sensitive, a credential, a connection or a password hint. Omission codes: `catalog-unavailable`,
  `unresolvable-version`, `unresolvable-input`, `duplicate-input`, `sensitive-input`, `non-literal-input`, `unsupported-input-setting`,
  `unsupported-output`, `structured-child`, `oversized-binding` (beyond the limits import reads) and `ambiguous-element`
  (another element in any scope shares the task's element id). An unreadable value, or one on an element this host binds itself, is
  reported Degraded and never proposed, as is one that names a member or an input twice, one on an element with no work to
  bind, and one on an element id another element in the document shares (another pool's process or a subprocess body).
  A document that binds work under one element id twice is refused. An element carrying the attribute under both
  vendor namespace aliases is refused, as `elsa:directBinding` is.
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

- The optional Direct publishing feature also registers the private BPMN-owned
  `IBpmnDocumentDirectAdmission` contribution (`BpmnDirectDocumentAdmission`). It assesses the entire
  physical workflow through the same strict source projection as publication. Only that content
  assessment can suppress the ordinary document execution diagnostic; native compilation and other
  validators retain their guards, and publication still authorizes the immutable source and every pin.
  Complete-source Direct support advertises `bpmn-authored-document-publish` on the existing route.
  Existing human formats 1/6 and service format 3 remain separate profiles; mixed profiles refuse.

- `GET interchange/bpmn/authoring-capabilities` (`Endpoints/Documents/Capabilities`), requiring
  `bpmn-interchange:read`, reports the exact complete-source kind/schema supported by one uniquely
  registered `IActivityStructureHandler`. Missing or competing handlers refuse the response. This is
  draft-retention compatibility, independent of execution admission; schema-2 compilation remains closed
  until the owning lowering slice is qualified. No extensibility slot or execution permission is added.

- Optional `ActivitiesBpmnDirectPublishing`: `POST interchange/bpmn/versions/{versionId}/publish-direct`, capability `elsa.api.bpmn-publishing` with relations `bpmn-authored-publish`, `bpmn-authored-service-publish` and `bpmn-authored-claim-publish` on the same route. Requires both `bpmn:publish` and existing workflow publication management permission for the actual source version, and `workflows/runtime:read` on each selected service source reference. Its keyed `BpmnDirectPublishingFaultRenderer` is a cross-domain `IEndpointFaultRenderer` contribution. Reads only immutable Design versions; runtime commands use the BPMN Core replacement port and existing store fencing. No event handler, background task or scheduler is added.

- `POST interchange/bpmn/analyze` / `import` / `export` (`Endpoints/Documents/*`, mapped in
  `Endpoints/BpmnInterchangeApi.cs`), permission-gated by `bpmn-interchange:read` / `bpmn-interchange:manage`. Analyze and
  import accept `confirmedBindings` (element ids); export returns `omissions` beside the XML.

## Consumed contracts

- `Elsa.Activities.Bpmn` models (`BpmnAuthoredStructure`, `BpmnElement`, `BpmnSequenceFlow`, …) and
  the `elsa.bpmn.structure` kind/schema constants on `BpmnProcess`.
- `Elsa.Workflows.Design.Core` `ActivityNode` / `ActivityNodeStructure` as the import/export unit.
- `Elsa.Activities.Design.Core` `IActivityDefinitionLookup` (optional, for both export and import) and `InputDefinition`
  declarations, which decide whether a task binding may travel and whether a confirmed one runs here.
