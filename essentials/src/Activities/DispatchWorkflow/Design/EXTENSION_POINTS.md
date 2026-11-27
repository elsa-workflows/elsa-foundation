# Extension points — DispatchWorkflow design

## `DispatchWorkflowDesignFeature`

The `ActivitiesDispatchWorkflowDesign` shell feature contributes these design-time services:

- `WorkflowDefinitionOptionsProvider` under `DispatchWorkflow.WorkflowDefinitions`. It exposes only tenant-visible definitions that resolve to exactly one live Published source reference.
- `DispatchPinSource` through Publishing's generic `IExecutableNodeMetadataSource` fan-in seam. Publishing's single `CollectExecutableNodeMetadata` handler invokes the source from the named `ExecutableNodeMetadataCollecting` event, stamps its ownership, and writes the exact executable identity plus authoritative source provenance into compiled node metadata.

- `BulkDispatchWorkflowsStructureHandler` through Workflows Design's `IActivityStructureHandler` contract (spec 1242). It projects, replaces, compiles and remaps the `Dispatch`, `ChildCompleted` and `ChildFaulted` slots, declares `currentItem` and `currentIndex` through `ProjectIterationVariables`, relates every child pair as `Unordered`, and answers `AssessChildResult` so a result binding between two different children is refused while bindings inside one slot keep their rules.
- `BulkDispatchSlotValidator` through Design Validations' `IDraftValidator` contributor seam. It reports `BulkDispatch/DispatchSlot` when a bulk node's `Dispatch` slot is empty or holds anything other than one `DispatchWorkflow`.

The design assembly references the DispatchWorkflow runtime contract assembly. The runtime assembly does not reference Design.

See the [module README](README.md#cross-domain-contributions) for the cross-domain ownership rule and the [Publishing extension catalog](../../../Workflows/Publishing/Api/EXTENSION_POINTS.md#executable-node-metadata-fan-in) for the host-side contract.
