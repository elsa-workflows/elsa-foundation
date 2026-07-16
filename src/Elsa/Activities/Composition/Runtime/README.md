# Elsa.Activities.Composition.Runtime

The Design-free runtime boundary reserved for workflow-as-activity composition.

This feature intentionally registers no descriptor constructor and exposes no dynamic input/output bag.
`WorkflowDefinitionActivity` is a typed placeholder carrying `WorkflowIdentity`; its actual nested-workflow
execution semantics remain a separately tracked runtime gap. A future implementation must lower the child
workflow request and result through explicit typed contracts and canonical bindings.

The discovery half lives in [../Design](../Design/README.md) and continues to publish workflow identity plus
declared input/output metadata. Runtime execution must not recreate the removed constructor/argument model.
