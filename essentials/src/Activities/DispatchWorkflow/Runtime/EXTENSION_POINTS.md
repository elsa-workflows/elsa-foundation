# Extension points for DispatchWorkflow

The [DispatchWorkflow Design feature](../Design/DispatchWorkflowDesignFeature.cs)
registers the workflow options and the publication pin contribution. The authored
CLR class retains its input, result and outcome contracts. Actor child delivery is
owned by the [workflow kernel host](../../../Workflows/Runtime/KernelHost/EXTENSION_POINTS.md).

The [pinned classic extension catalog](https://github.com/elsa-workflows/elsa-foundation/blob/ef70e5acd7fa8b07cfad4a6c0ca9a229a039f20a/essentials/src/Activities/DispatchWorkflow/Runtime/EXTENSION_POINTS.md) documents the older
checkpoint, post-commit delivery and called-workflow control contracts.
