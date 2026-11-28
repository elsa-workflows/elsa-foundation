# DispatchWorkflow activity

This package supplies the authored `DispatchWorkflow` CLR contract. The
[Design feature](../Design/DispatchWorkflowDesignFeature.cs) provides the Published
child choices and contributes the exact child executable and provenance pin during
publication. Actor Foundation executes the pinned child through the Native engine.

Use the [classic compatibility instructions](../../../../../docs/contributing/workbench-compatibility-tests.md#classic-dispatch-and-called-workflow-control)
for the previous checkpoint and resumption runner. The [archived runtime guide](https://github.com/elsa-workflows/elsa-foundation/blob/ef70e5acd7fa8b07cfad4a6c0ca9a229a039f20a/essentials/src/Activities/DispatchWorkflow/Runtime/README.md)
documents the classic feature, delivery policy and recovery behavior.
