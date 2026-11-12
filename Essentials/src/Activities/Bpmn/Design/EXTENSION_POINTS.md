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
  reports execution-closed errors without preventing draft retention through ordinary create/update commands.
- Exact kind/schema matching exposes the document retention payload; the scope-view handler has no authorable
  payload type. Both handlers retain Unknown child-result facts and refuse executable compilation. Schema 1's
  result-order proof is not reused, and no Publishing or Graph admission authority is replaced.

## Published model references

- Owning contract: Publishing Core `IExecutableCompilationSource`.
- Contributor: internal `BpmnEmbeddingPinSource`, scoped enumerable registration by `ActivitiesBpmnDesignFeature`.
- Classification: cross-domain contribution through Publishing's existing aggregation. It requires a
  literal accessible live model source, validates the complete qualified material identity, and contributes
  the retained encoded pin and executable dependency. It does not execute a model or read a mutable draft.
- Publication/refusal semantics and metadata bytes are retained from the existing implementation;
  runtime readers share the private Core codec and frozen key. The runtime hosting feature supplies no publisher contribution.

See [authoring migration](README.md) and the [runtime responsibilities](../Elsa.Activities.Bpmn/EXTENSION_POINTS.md).
