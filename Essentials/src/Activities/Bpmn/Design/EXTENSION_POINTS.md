# BPMN authoring contributions

## Authored structure

- Owning contract: Workflows Design Core `IActivityStructureHandler`.
- Contributors: internal stateless `BpmnStructureHandler` and `BpmnDocumentStructureHandler`, singleton enumerable
  registrations by `ActivitiesBpmnDesignFeature`; each holds only process-wide schema policy, with no scoped state.
- Classification: cross-domain contributions, keyed by structure kind and exact schema. The frozen `1.0.0`
  handler projects bound children, scoped variables and typed Decision outcomes, then lowers/remaps the graph.
  The internal `2.0.0` handler retains canonical XML, disjoint native children/bindings and metadata; it projects
  direct native children/scoped variables and refuses executable compilation pending complete-content admission
  and derived lowering. Standard nested-scope projection remains unqualified.
- Repeated feature composition retains one implementation per schema. Runtime-only registration supplies no handler.

## Published model references

- Owning contract: Publishing Core `IExecutableCompilationSource`.
- Contributor: internal `BpmnEmbeddingPinSource`, scoped enumerable registration by `ActivitiesBpmnDesignFeature`.
- Classification: cross-domain contribution through Publishing's existing aggregation. It requires a
  literal accessible live model source, validates the complete qualified material identity, and contributes
  the retained encoded pin and executable dependency. It does not execute a model or read a mutable draft.
- Publication/refusal semantics and metadata bytes are retained from the existing implementation;
  runtime readers share the private Core codec and frozen key. The runtime hosting feature supplies no publisher contribution.

See [authoring migration](README.md) and the [runtime responsibilities](../Elsa.Activities.Bpmn/EXTENSION_POINTS.md).
