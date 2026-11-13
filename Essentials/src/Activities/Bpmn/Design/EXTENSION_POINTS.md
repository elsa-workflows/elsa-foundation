# BPMN authoring contributions

## Authored structure

- Owning contract: Workflows Design Core `IActivityStructureHandler`.
- Contributor: internal `BpmnStructureHandler`, singleton enumerable registration by `ActivitiesBpmnDesignFeature`.
- Classification: cross-domain contribution, keyed by the frozen structure kind/schema. It projects
  bound children, scoped variables and typed Decision outcomes, then lowers/remaps the compiled graph.
- `AssessChildResult` and `ChildrenCompletedBefore` supply order for a single fully connected,
  acyclic, unconditional ordinary start → bound `task` chain → ordinary end. They use sequence-flow
  order, not the authored activity array. Such a chain runs each task once, so it states
  `EarlierProducerRunCanReachConsumer = false`; unproven shapes leave the fact unset, which also refuses a
  scope-free result read inside a child the process may run again. Shared source
  analysis and mandatory headless publication consume these facts. Other task families, branches, multiple entries, events/listeners, loops,
  compensation, transactions and subprocess/call boundaries remain unknown; no generic admission
  rule or runtime occurrence semantics are relaxed.
- Repeated feature composition retains one implementation per exact kind/schema. Runtime-only feature registration supplies no handler.

## Complete-document retention and source validation

- Owning contracts: Workflows Design Core `IActivityStructureHandler` and Validations Core `IDraftValidator`.
- Contributors: internal singleton `BpmnDocumentStructureHandler` for `elsa.bpmn.structure@2.0.0`,
  internal singleton `BpmnDocumentScopeViewHandler` for transient `elsa.bpmn.scope-view@1.0.0`, and
  scoped enumerable `BpmnScopeViewSourceValidator` / `BpmnDocumentAdmissionValidator`, registered by `ActivitiesBpmnDesignFeature`.
- Classification: cross-domain projection and validation through existing owning contracts. XML is retained
  once on the physical source alongside its disjoint native forest and compact scope headers. Views are pure,
  scope-addressed query projections; replacement folds only owned native configuration back into the physical source.
- The source validator rejects physically authored derived views and validates complete-document ownership
  before generic traversal. Opaque literal JSON is not treated as activity source. The admission validator
  reports content-derived execution errors without preventing draft retention through ordinary create/update commands.
- Exact kind/schema matching exposes the document retention payload; the scope-view handler has no authorable
  payload type. Context-free compilation remains closed. Explicit-context compilation admits complete source before lowering
  and requires exact resolved native roles. Raw-source chain facts remain conservative outside the proven subset;
  schema 1 is unchanged and no shared Publishing/Graph admission authority is replaced.

## Optional complete-source Direct assessment

- Private BPMN-owned contributor: `IBpmnDocumentDirectAdmission.CanPromote(WorkflowDefinitionState)`.
- Consumer: `BpmnDocumentAdmissionValidator`; the optional Interchange Direct publishing feature
  registers `BpmnDirectDocumentAdmission` as an enumerable scoped contributor. Ordinary composition has none.
- It assesses the entire physical closed workflow using the same strict projection as Direct publication.
  This content assessment suppresses only that validator's document execution diagnostic. Native
  compilation, other validators, source authorization and immutable pin authorization remain independent.
- Profiles remain the existing human formats 1/6 and service format 3; unsupported mixed content refuses.

## Published model references

- Owning contract: Publishing Core `IExecutableCompilationSource`.
- Contributor: internal `BpmnEmbeddingPinSource`, scoped enumerable registration by `ActivitiesBpmnDesignFeature`.
- Classification: cross-domain contribution through Publishing's existing aggregation. It requires a
  literal accessible live model source, validates the complete qualified material identity, and contributes
  the retained encoded pin and executable dependency. It does not execute a model or read a mutable draft.
- Publication/refusal semantics and metadata bytes are retained from the existing implementation;
  runtime readers share the private Core codec and frozen key. The runtime hosting feature supplies no publisher contribution.

See [authoring migration](README.md) and the [runtime responsibilities](../Elsa.Activities.Bpmn/EXTENSION_POINTS.md).

## Complete-source compilation verification

- `BpmnDocumentCompilationSource` is a scoped `IExecutableCompilationSource` contribution registered by
  `ActivitiesBpmnDesignFeature`. It checks exact process/Decision roles and the complete admitted scope
  semantics against the final executable using exactly one canonical `IExecutableStructureNormalizer`.
- A provider label, source marker or reusable boundary cannot authorize a native process role. Physical
  source ownership, exact invocation/header configuration and inline placement are checked before reduction.
