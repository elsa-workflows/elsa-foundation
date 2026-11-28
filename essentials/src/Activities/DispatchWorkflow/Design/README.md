# DispatchWorkflow design module

This module owns DispatchWorkflow authoring-time integration. It supplies definition dropdown options and resolves a statically authored workflow definition to the exact accessible live Published executable pin embedded in the parent executable. It also owns the design side of `BulkDispatchWorkflows` (spec 1242): its slot structure and the check on its `Dispatch` slot.

## Cross-domain contributions

`DispatchPinSource` is a DispatchWorkflow-owned implementation of Publishing's generic `IExecutableCompilationSource` contract. It revalidates the publication tenant, requires one upgraded Published child artifact, validates statically knowable child inputs, and contributes both the node pin metadata and exact child artifact ID/hash dependency edge. `DispatchWorkflowDesignFeature` registers the source; it does not register an event handler or alter the generic compiler.

Publishing owns the named `ExecutableCompilationCollecting` event, its single `CollectExecutableCompilation` aggregating handler, deterministic source ordering, ownership stamping, and conflict validation. This keeps DispatchWorkflow-specific resolution in this module while preserving Publishing as the sole owner of compilation fan-in and canonical dependency hashing. See the [Publishing extension catalog](../../../Workflows/Publishing/Api/EXTENSION_POINTS.md#executable-compilation-fan-in).

`BulkDispatchWorkflowsStructureHandler` is a DispatchWorkflow-owned implementation of Workflows Design's `IActivityStructureHandler` contract. It projects, replaces, compiles and remaps the `Dispatch`, `ChildCompleted` and `ChildFaulted` slots and declares `currentItem` and `currentIndex` as iteration variables. Its result and time facts say that the three children run concurrently across item passes, so publish refuses a result binding between two of them and warns about a variable written from them. A `DispatchWorkflow` inside the `Dispatch` slot is pinned by `DispatchPinSource` like any other.

`BulkDispatchSlotValidator` is a DispatchWorkflow-owned implementation of Design Validations' `IDraftValidator` contributor seam (fan-in event `DraftValidating`, aggregated by `ExecuteValidations`). It reports `BulkDispatch/DispatchSlot` when a bulk node's `Dispatch` slot is empty or holds anything other than one `DispatchWorkflow`; like every Draft error it does not block saving, and the promotion gate refuses to publish. `DispatchWorkflowDesignFeature` registers both.

The design module references the DispatchWorkflow runtime contract assembly. The runtime module does not reference Design.

Workflow-definition construction activities and Studio editors are intentionally outside this module's #677 scope. Studio support is tracked in a separate task.
