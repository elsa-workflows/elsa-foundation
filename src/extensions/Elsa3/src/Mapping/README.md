# Elsa3.Mapping

Object mappings for converting Elsa3 model types to Elsa4 counterparts. Consumed by the Elsa3 import pipeline.

`Elsa3ActivityToState` keeps an Elsa 3 property as an input when its name matches a declared input's name (exactly,
else the only match ignoring case) and stores the binding under that input's `ReferenceKey`, the key design validation
and publication match ordinally; an output keeps the Elsa 3 property name; a property that names neither is dropped.
Two properties that bind one input, or a property that matches several declared inputs only ignoring case, refuse the
mapping with an `ArgumentException` naming the node, the properties and the input, never a value.

## Cross-domain contributions

This feature implements contributor interfaces from other domains:

- **`IObjectMapping<TSource, TTarget>`** *(Core — `Elsa.Mapping.Core`)* — multiple mappings convert Elsa3 models to Elsa4 models. Each mapping is resolved per type-pair by `ObjectMapper` in `Elsa.Mapping`.
  - Known impls:
    - `Elsa3ActivityToState` — `IObjectMapping<Elsa3Activity, ActivityNode>`
    - `Elsa3ArgumentDefinitionToInputOutput` — `IObjectMapping<Elsa3WorkflowArgumentDefinition, InputDefinition>` and `IObjectMapping<Elsa3WorkflowArgumentDefinition, OutputDefinition>`
    - `Elsa3WorkflowDefinitionToState` — `IObjectMapping<Elsa3WorkflowDefinition, WorkflowDefinitionState>`
    - `Elsa3WorkflowDefinitionToWorkflowDefinitionVersion` — `IObjectMapping<Elsa3WorkflowDefinition, IWorkflowDefinitionVersion>`
  - Catalog: [`Elsa.Mapping/EXTENSION_POINTS.md`](../Elsa.Mapping/EXTENSION_POINTS.md)
