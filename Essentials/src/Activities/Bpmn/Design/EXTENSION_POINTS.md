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
  Both complete-document and view compilation refuse pending complete-content admission and derived lowering.
- Repeated feature composition retains one implementation per schema. Runtime-only registration supplies no handler.

## Physical scope-view rejection

- Owning contract: Workflows Design Validations Core `IDraftValidator`.
- Contributor: internal `BpmnScopeViewSourceValidator`, scoped enumerable registration by `ActivitiesBpmnDesignFeature`.
- Classification: structural source refusal through the existing inline `DraftValidating` gate. It visits physical
  native ownership positions without traversing generated BPMN scope views or arbitrary literal/metadata JSON.
  The host's ordinary `ExecuteValidations` aggregation propagates the rejection before writes. It does not
  protect raw stores that bypass the gate or admit these views for execution.

## Published model references

- Owning contract: Publishing Core `IExecutableCompilationSource`.
- Contributor: internal `BpmnEmbeddingPinSource`, scoped enumerable registration by `ActivitiesBpmnDesignFeature`.
- Classification: cross-domain contribution through Publishing's existing aggregation. It requires a
  literal accessible live model source, validates the complete qualified material identity, and contributes
  the retained encoded pin and executable dependency. It does not execute a model or read a mutable draft.
- Publication/refusal semantics and metadata bytes are retained from the existing implementation;
  runtime readers share the private Core codec and frozen key. The runtime hosting feature supplies no publisher contribution.

See [authoring migration](README.md) and the [runtime responsibilities](../Elsa.Activities.Bpmn/EXTENSION_POINTS.md).
