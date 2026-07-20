# Elsa.Activities.Bpmn.Interchange Extension Points

## Replaceable service contracts

### `IBpmnDocumentImporter`

- **Kind:** Replacement contract
- **Contract:** `Elsa.Activities.Bpmn.Interchange.Contracts.IBpmnDocumentImporter`
- **Default:** `BpmnDocumentImporter` *(intra-domain — default)*
- **Registration:** Singleton via `ActivitiesBpmnInterchangeFeature`; replace by registering another
  implementation after the feature.

### `IBpmnDocumentExporter`

- **Kind:** Replacement contract
- **Contract:** `Elsa.Activities.Bpmn.Interchange.Contracts.IBpmnDocumentExporter`
- **Default:** `BpmnDocumentExporter` *(intra-domain — default)*
- **Registration:** Singleton via `ActivitiesBpmnInterchangeFeature`; replace by registering another
  implementation after the feature.

## Owned interchange contracts

- `elsa:conditionOutcome` attribute (namespace `https://elsa-workflows.io/schemas/bpmn`) on
  `sequenceFlow` — the lossless XML carrier for outcome-matched flow conditions.
- The authored `diagram` payload shape (`shapes: {id: {x,y,width,height}}`,
  `edges: {id: {waypoints: [{x,y}]}}`) mirrors BPMNDI and is treated as opaque by the runtime.

## Owned HTTP endpoints

- `POST interchange/bpmn/analyze` / `import` / `export` (`Endpoints/BpmnInterchangeEndpoints.cs`),
  permission-gated by `bpmn-interchange.read` / `bpmn-interchange.manage`, discovered by the
  FastEndpoints assembly scan via `FastEndpointsFeatureBase`.

## Consumed contracts

- `Elsa.Activities.Bpmn` models (`BpmnAuthoredStructure`, `BpmnElement`, `BpmnSequenceFlow`, …) and
  the `elsa.bpmn.structure` kind/schema constants on `BpmnProcess`.
- `Elsa.Workflows.Design.Core` `ActivityNode` / `ActivityNodeStructure` as the import/export unit.
