# BPMN authoring contributions

## Authored structure

- Owning contract: Workflows Design Core `IActivityStructureHandler`.
- Contributor: internal `BpmnStructureHandler`, singleton enumerable registration by `ActivitiesBpmnDesignFeature`.
- Classification: cross-domain contribution, keyed by the frozen structure kind/schema. It projects
  bound children, scoped variables and typed Decision outcomes, then lowers/remaps the compiled graph.
- Repeated feature composition retains one implementation. Runtime-only feature registration supplies no handler.

## Published model references

- Owning contract: Publishing Core `IExecutableCompilationSource`.
- Contributor: internal `BpmnEmbeddingPinSource`, scoped enumerable registration by `ActivitiesBpmnDesignFeature`.
- Classification: cross-domain contribution through Publishing's existing aggregation. It requires a
  literal accessible live model source, validates the complete qualified material identity, and contributes
  the retained encoded pin and executable dependency. It does not execute a model or read a mutable draft.
- Publication/refusal semantics and metadata bytes are retained from the existing implementation;
  runtime readers share the private Core codec and frozen key. The runtime hosting feature supplies no publisher contribution.

See [authoring migration](README.md) and the [runtime responsibilities](../Elsa.Activities.Bpmn/EXTENSION_POINTS.md).
