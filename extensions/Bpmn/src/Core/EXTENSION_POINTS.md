# BPMN hosting contract extension points

## Authored-document failure boundary

`BpmnDocumentSourceException` is the public Core refusal for malformed authored document source,
not a registration or runtime-model capability. Design's JSON/XML parsers preserve the original
parser exception in `InnerException` and identify the physical node/process or projected node/scope.
It derives from `InvalidOperationException` so generic Design/Publishing refusal handling remains
compatible. The existing runtime `BpmnModelValidationException` contract is unchanged.

[`IBpmnModelRuntime`](IBpmnModelRuntime.cs) is a single-provider scoped replacement port. The archived `ActivitiesBpmnHostingFeature` registers its internal `BpmnModelRuntime` with `TryAddScoped`. Its shell initialization requires exactly one scoped registration. [`BpmnModelDefinition` and `BpmnModelProtocol`](BpmnModelDefinition.cs) describe the immutable direct profile separately from the frozen embedded authoring format.

The [archived BPMN API](https://github.com/elsa-workflows/elsa-foundation/blob/ef70e5acd7fa8b07cfad4a6c0ca9a229a039f20a/extensions/Bpmn/src/Api/EXTENSION_POINTS.md) consumes this port for publication, start, work/completion, cancellation, service-incident inspection/retry and execution/command inspection. Optional members default for existing replacements that do not implement them: `AdmitNewExecutions` defaults to `false`; `PublishFromSourceAsync` (authored provenance), `InspectAsync`, `CancelAsync`, `ListIncidentsAsync`, `RetryServiceAsync`, `ClaimHumanAsync` and `CompleteClaimedHumanAsync` refuse with `NotSupportedException`. The optional `ActivitiesBpmnDirectPublishing` feature in `Elsa.Activities.Bpmn.Interchange` also consumes `PublishFromSourceAsync`. The API advertises `elsa.api.bpmn-claims` whenever it is composed, so a replacement without claims answers those routes with a refusal. Replacements must preserve explicit persistence scope/tenant checks, live-source/full-pin authority, retained conflict/replay outcomes and model-owned invocation/attempt identity. They must use the shared runtime's fenced durable admission/checkpoint authority and the selected released semantics rather than inventing another store or interpreter. The built-in implementation refuses actor providers outside its qualified single-node profile. [Declared formats](README.md#declared-model-formats) identify the admitted bindings.
