# DispatchWorkflow design module

This module owns DispatchWorkflow authoring-time integration. It supplies definition dropdown options and resolves a statically authored workflow definition to the exact accessible live Published executable pin embedded in the parent executable.

## Cross-domain contributions

`DispatchPinSource` is a DispatchWorkflow-owned implementation of Publishing's generic `IExecutableCompilationSource` contract. It revalidates the publication tenant, requires one upgraded Published child artifact, validates statically knowable child inputs, and contributes both the node pin metadata and exact child artifact ID/hash dependency edge. `DispatchWorkflowDesignFeature` registers the source; it does not register an event handler or alter the generic compiler.

Publishing owns the named `ExecutableCompilationCollecting` event, its single `CollectExecutableCompilation` aggregating handler, deterministic source ordering, ownership stamping, and conflict validation. This keeps DispatchWorkflow-specific resolution in this module while preserving Publishing as the sole owner of compilation fan-in and canonical dependency hashing. See the [Publishing extension catalog](../../../Workflows/Publishing/Api/EXTENSION_POINTS.md#executable-compilation-fan-in).

The design module references the DispatchWorkflow runtime contract assembly. The runtime module does not reference Design.

Workflow-definition construction activities and Studio editors are intentionally outside this module's #677 scope. Studio support is tracked in a separate task.
