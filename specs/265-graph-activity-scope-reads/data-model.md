# Data Model: Graph activity body nodes read the activity's inputs, variables and results

Everything here is additive and optional, so absent means today's behaviour.

## Design time: the authored template (unchanged shape)

A body node keeps template-local identities:

| Reference | Authored form | Meaning inside a body |
|---|---|---|
| Graph input | `WorkflowRequest {memberKey}`, or Studio `Input {referenceKey}` | The graph input with that reference key |
| Graph variable | `Variable {referenceKey, declaringScopeId: "workflow"}` (legacy `"activity-graph"` is an alias) | The graph variable |
| Body container variable | `Variable {referenceKey, declaringScopeId: <body container node id>}` | That container's variable |
| Body result | `ActivityResult {producerNodeId, projectionKey, producerScopeId?}` with body node ids | That node's result in this invocation |
| Capture | node `outputs[] = {referenceKey, value: Variable{referenceKey, "workflow"}}` | Write into the graph variable |

## Compiled: activity template

- **`ActivityTemplateOccurrenceCompilation`** / **`ExecutableActivityTemplateOccurrenceOverlay`** gain
  `OutputBindings` (optional). These are the authored occurrence `outputs`, carried in the overlay so the placer can
  compile them.
- **Boundary descriptor payload** gains `scopeModel: "closed"`. The graph runtime does not read it; placement does.
- **Provider fingerprint**: `elsa.activity-graph/compiler/3.0.0`.

## Compiled: placed executable

- **Boundary node metadata** `runtime.activityExecutionScope = closed`. The placer sets it only on a boundary whose
  hashed descriptor has `scopeModel: closed`. Scheduling copies it into the boundary's activity state (plan A-1).
- **Remapped references** in body occurrence roots, all to placed ids:

  | Field | Remapped value |
  |---|---|
  | `RuntimeInputBinding.ActivityResult.ProducerExecutableNodeId` | Placed producer id |
  | `RuntimeInputBinding.ActivityResult.ProducerScopeId` | Placed id |
  | `VariableRead.DeclaringScopeId` | `"workflow"` → placed boundary id; container id → placed container id |
  | `VariableExpressionParameterBinding.DeclaringScopeNodeId` | As `DeclaringScopeId` |
  | `ActivityResultExpressionParameterBinding.ProducerNodeId` | Placed producer id |
  | `ExecutableNode.IntrinsicVariable.DeclaringScopeId` | As `DeclaringScopeId` |
- **Body output captures**: a `RuntimeOutputCapture` with `ValueId = variable:{key}` and metadata
  `runtime.captureTargetScopeId = <placed boundary id>`. The declared type and storage driver are pinned.

## Runtime: durable state (no new row kinds)

| Row | Id | Metadata | Change |
|---|---|---|---|
| Graph input | `ActivityExecutionScopeKeys.Input(boundaryExecId, referenceKey)` | `boundaryValueRole=input`, `boundaryReferenceKey`, `BoundaryInputName` | none |
| Graph variable | `ActivityExecutionScopeKeys.Variable(boundaryExecId, referenceKey)` | `boundaryValueRole=variable`, `boundaryReferenceKey`, `runtime.storageDriverKey` | **+ `runtime.boundaryVariableName`** |

**Writes to a graph variable row:**
- `GraphActivity` entry writes the initial value, as today.
- A body capture writes the row with the producer's completion commit. The new value has the same id and the same
  type id, kind and schema.
- A boundary retry gets a new execution id and re-initialises the variable.

## Runtime: resolution view for a node below a closed boundary

The nearest boundary is the first ancestor state (through `ParentActivityExecutionId`) whose state metadata has
`runtime.activityExecutionScope = closed`.

**`WorkflowInputEnvelopes`**: the boundary's input rows, keyed by `boundaryReferenceKey`.

**Visible frames**, in order:
1. A synthetic Root frame `{FrameId: graph:{boundaryExecId}, ScopeId: <placed boundary id>, Parent: null}`. Its
   values are the variable rows, and its names come from `boundaryVariableName`. It is read-only through frame APIs:
   captures write the rows.
2. Body container and iteration frames between the boundary and the node, parented as today. The top one is
   parented to the graph frame.

The caller's root, container and iteration frames are not visible.

**Activity results**: the producer must share the consumer's nearest boundary execution. Its declared scope is
compared with `flowchart.executionScopeId` metadata.

Nodes with no closed ancestor resolve exactly as today (FR-011).

## Validation rules (activity publication)

See [contracts/graph-scope-references.md](contracts/graph-scope-references.md).
