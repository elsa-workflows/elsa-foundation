# DispatchWorkflow design module

This module owns DispatchWorkflow authoring-time integration. It supplies definition dropdown options and resolves a statically authored workflow definition to the exact live Published executable/source pin embedded in the parent executable.

## Cross-domain contributions

`DispatchPinSource` is a DispatchWorkflow-owned implementation of Publishing's generic `IExecutableNodeMetadataSource` contract. `DispatchWorkflowDesignFeature` registers the source; it does not register an event handler or alter the generic compiler.

Publishing owns the named `OnExecutableNodeMetadataCollecting` event, its single `CollectExecutableNodeMetadata` aggregating handler, deterministic source ordering, ownership stamping, and conflict validation. This keeps DispatchWorkflow-specific resolution in this module while preserving Publishing as the sole owner of executable-node metadata fan-in. See the [Publishing extension catalog](../../../Workflows/Publishing/Api/EXTENSION_POINTS.md#executable-node-metadata-fan-in).

The design module references the DispatchWorkflow runtime contract assembly. The runtime module does not reference Design.
