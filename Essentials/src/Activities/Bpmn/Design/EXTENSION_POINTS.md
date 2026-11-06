# BPMN authoring contributions

## Authored structure

- Owning contract: Workflows Design Core `IActivityStructureHandler`.
- Contributors: internal stateless `BpmnStructureHandler`, `BpmnDocumentStructureHandler` and `BpmnDocumentScopeViewHandler`, singleton enumerable
  registrations by `ActivitiesBpmnDesignFeature`; each holds only process-wide schema policy, with no scoped state.
- Classification: cross-domain contributions, keyed by structure kind and exact schema. The frozen `1.0.0`
  handler projects bound children, scoped variables and typed Decision outcomes, then lowers/remaps the graph.
  The internal `2.0.0` handler retains canonical XML, disjoint native children/bindings and metadata; it projects
  native children/scoped variables and pure nested standard-scope views from compact configuration headers.
  The internal view handler folds only owned scope edits into canonical source; no snapshot is persisted.
  Complete-document/view compilation assesses the entire source, then refuses execution until every actual
  compiler route is qualified. Internal schema1 lowering remains a bounded proof; unsupported content retains
  source-specific diagnostics. This stage is unqualified pending offloaded checks/runtime proof.
- Repeated feature composition retains one implementation per schema. Runtime-only registration supplies no handler.

## Physical scope-view rejection

- Owning contract: Workflows Design Validations Core `IDraftValidator`.
- Contributor: internal `BpmnScopeViewSourceValidator`, scoped enumerable registration by `ActivitiesBpmnDesignFeature`.
- Classification: structural source refusal through the existing inline `DraftValidating` gate. It visits physical
  native ownership positions without traversing generated BPMN scope views or arbitrary literal/metadata JSON.
  The host's ordinary `ExecuteValidations` aggregation propagates the rejection before writes. It does not
  protect raw stores that bypass the gate or admit these views for execution.

## Complete-source execution admission

- Owning contract: Workflows Design Validations Core `IDraftValidator`.
- Contributor: internal `BpmnDocumentAdmissionValidator`, scoped enumerable registration. It returns derived
  `Bpmn/ExecutionCapability` errors; it does not throw away or rewrite unsupported source while saving a draft.
- Owning contract: Publishing Core `IExecutableCompilationSource`.
- Contributor: internal `BpmnDocumentCompilationSource`, scoped enumerable registration. The existing compilation
  workflow compilation fan-in receives original source plus compiled nodes and can verify actual process/Decision
  types and the complete admitted semantic payload, including variable policy/defaults and transaction state.
  It rejects a physical derived-view root/native child and requires exactly one Publishing Core
  `IExecutableStructureNormalizer` to compare canonical variable/default representations.
- Owning contract: Workflows Design Core `IActivityStructureHandler.CompileExecutableStructure(activity, context)`.
  The explicit `ActivityStructureCompilationContext` carries the physical source, captured child projections and
  exact resolved runtime facts. Workflow compilation supplies catalog/actual template facts; reusable-template
  coordination supplies facts from its exact loaded templates to the Graph provider before placement.
  BPMN's contextual handlers require exact native roles and source ownership before complete-source lowering.
  The old one-argument BPMN source2/view entrypoints remain closed. Unknown versions of a known handled kind
  refuse on contextual dispatch; custom handlers retain their existing one-argument behavior by default.
  Syntax admission runs before reduction independently of the workflow-start flag. No runtime algorithm changes.
  These new routes remain unqualified until offloaded actual compiler/coordinator controls pass.

## Published model references

- Owning contract: Publishing Core `IExecutableCompilationSource`.
- Contributor: internal `BpmnEmbeddingPinSource`, scoped enumerable registration by `ActivitiesBpmnDesignFeature`.
- Classification: cross-domain contribution through Publishing's existing aggregation. It requires a
  literal accessible live model source, validates the complete qualified material identity, and contributes
  the retained encoded pin and executable dependency. It does not execute a model or read a mutable draft.
- Publication/refusal semantics and metadata bytes are retained from the existing implementation;
  runtime readers share the private Core codec and frozen key. The runtime hosting feature supplies no publisher contribution.

See [authoring migration](README.md) and the [runtime responsibilities](../Elsa.Activities.Bpmn/EXTENSION_POINTS.md).
