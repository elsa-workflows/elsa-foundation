# Elsa3.Mapping

Object mappings for converting Elsa3 model types to Elsa4 counterparts. Consumed by the Elsa3 import pipeline.

## Cross-domain contributions

This feature implements contributor interfaces from other domains:

- **`IObjectMapping<TSource, TTarget>`** *(Core — `Elsa.Mapping.Core`)* — multiple mappings convert Elsa3 models to Elsa4 models. Each mapping is resolved per type-pair by `ObjectMapper` in `Elsa.Mapping`.
  - Known impls:
    - `Elsa3ActivityToState` — `IObjectMapping<Elsa3Activity, ActivityNode>`
    - `Elsa3ArgumentDefinitionToInputOutput` — `IObjectMapping<Elsa3WorkflowArgumentDefinition, InputDefinition>` and `IObjectMapping<Elsa3WorkflowArgumentDefinition, OutputDefinition>`
    - `Elsa3WorkflowDefinitionToState` — `IObjectMapping<Elsa3WorkflowDefinition, WorkflowDefinitionState>`
    - `Elsa3WorkflowDefinitionToWorkflowDefinitionVersion` — `IObjectMapping<Elsa3WorkflowDefinition, IWorkflowDefinitionVersion>`
  - Catalog: [`Elsa.Mapping/EXTENSION_POINTS.md`](../Elsa.Mapping/EXTENSION_POINTS.md)
