# Elsa3.Mapping

Object mappings for converting Elsa3 model types to Elsa4 counterparts. Consumed by the Elsa3 import pipeline.

`Elsa3ActivityToState` keeps an Elsa 3 property as an input when its name matches a declared input's name (exactly,
else the only match ignoring case) and stores the binding under that input's `ReferenceKey`, the key design validation
and publication match ordinally; an output keeps the Elsa 3 property name; a property that names neither is dropped.
Two properties that bind one input, or a property that matches several declared inputs only ignoring case, refuse the
mapping with an `ArgumentException` naming the node, the properties and the input, never a value.

## Mappings

`Elsa3ActivityToState`, `Elsa3ArgumentDefinitionToInputOutput`, `Elsa3WorkflowDefinitionToState` and
`Elsa3WorkflowDefinitionToWorkflowDefinitionVersion` are concrete scoped services. They inject one another, and the
importer and the reusable-activity materializer inject them directly. They implement no shared mapping contract, so
there is nothing to replace per type pair. The feature's replaceable contracts are in its
[extension-point catalog](EXTENSION_POINTS.md).
