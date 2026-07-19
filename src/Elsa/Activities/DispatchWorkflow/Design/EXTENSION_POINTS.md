# Extension points — DispatchWorkflow design

## `DispatchWorkflowDesignFeature`

The `ActivitiesDispatchWorkflowDesign` shell feature contributes two design-time services:

- `WorkflowDefinitionOptionsProvider` under `DispatchWorkflow.WorkflowDefinitions`. It exposes only tenant-visible definitions that resolve to exactly one live Published source reference.
- `DispatchPinSource` through Publishing's generic `IExecutableNodeMetadataSource` fan-in seam. Publishing's single `CollectExecutableNodeMetadata` handler invokes the source from the named `OnExecutableNodeMetadataCollecting` event, stamps its ownership, and writes the exact executable identity plus authoritative source provenance into compiled node metadata.

The design assembly references the DispatchWorkflow runtime contract assembly. The runtime assembly does not reference Design.

See the [module README](README.md#cross-domain-contributions) for the cross-domain ownership rule and the [Publishing extension catalog](../../../Workflows/Publishing/Api/EXTENSION_POINTS.md#executable-node-metadata-fan-in) for the host-side contract.
