# Elsa.Activities.Flowchart Extension Points

## Scoped execution seam

`FlowchartExecutionEngine` is the activity-owned scoped execution seam. It owns Flowchart runtime state mutation, child scheduling metadata, arrival recording, implicit join evaluation, loop/race scope creation, diagnostics, and deferred composite completion. Its durable snapshot is staged as one typed, versioned structural private-state document; it does not patch the activity metadata bag.

The scoped execution model is intentionally not an extension point directly: custom gateway behavior crosses the public policy contract below, and `FlowchartExecutionEngine` remains the authority that validates and applies policy commands.

## Implementable contributor interfaces

### `IFlowchartPolicy`

- **Kind:** Contributor (policy decision provider)
- **Contract:** `Elsa.Activities.Flowchart.Contracts.IFlowchartPolicy`
- **Policy contract:** policies receive `IFlowchartPolicyContext` and return `FlowchartPolicyDecision` commands for `FlowchartExecutionEngine` to validate and apply.
- **Registration:** Register one or more implementations with DI as `IFlowchartPolicy`.
- **Aggregation:** `IFlowchartPolicyRegistry` resolves all registered policy implementations by stable `PolicyKind`.
- **Selection:** Flowchart structure metadata can assign a policy kind to a node through `FlowchartStructure.NodeMetadata`.
- **Decision boundary:** Policies receive `IFlowchartPolicyContext`, which exposes read-only graph/state/trigger information. Policies return `FlowchartPolicyDecision` commands; `FlowchartExecutionEngine` validates and applies those commands.

Known implementations:

- `DirectContinuationFlowchartPolicy` *(intra-domain — default)*
- `ImplicitActivationJoinFlowchartPolicy` *(intra-domain — default)*
- `DecisionFlowchartPolicy` *(intra-domain — default)*
- `ParallelForkFlowchartPolicy` *(intra-domain — default)*
- `ParallelJoinFlowchartPolicy` *(intra-domain — default)*
- `InclusiveForkFlowchartPolicy` *(intra-domain — default)*
- `InclusiveJoinFlowchartPolicy` *(intra-domain — default)*
- `FirstWinsFlowchartPolicy` *(intra-domain — default)*
- `MergeFlowchartPolicy` *(intra-domain — default)*

## Activity-owned structure contracts

This module also exposes these activity-owned contracts:

- `Flowchart.Activities` child slot
- `elsa.flowchart.structure` structure payload with schema version `1.0.0`
- `FlowchartStructure.Connections` containing `FlowchartConnection[]`
- `FlowchartStructure.StartNodeId` optional start-node selection
- `FlowchartStructure.NodeMetadata` optional node policy metadata
- `FlowchartStructure.ConnectionMetadata` optional connection policy metadata
- `FlowchartStructure.Variables` optional container-scoped variable declarations (ADR 0027)

## Consumed runtime contracts

`Flowchart` implements the engine-only structural execution protocol (`Elsa.Workflows.Runtime.Core.Contracts`):

- `IRuntimeStructuralActivity` — starts the flowchart, schedules its initial children, and returns a `RuntimeStructuralContinuation` describing whether the runtime must complete, defer, fault, or cancel the composite.
- `IRuntimeActivityChildCompletionHandler` — invoked when a child completes; routes through `FlowchartExecutionEngine.OnChildCompletedAsync` to follow outbound connections, evaluate implicit/parallel joins, and return the next continuation decision.
- `IRuntimeActivityChildFaultHandler` — invoked when a child branch faults (#308); routes through `FlowchartExecutionEngine.OnChildFaultedAsync`. Because a flowchart join requires every inbound branch, a faulted inbound branch can never let the join fire, so the returned decision faults the flowchart deterministically (surfacing a composite incident) instead of hanging — mirroring the `Parallel` fork/join composite. The faulted leaf keeps its own blocking incident.

## Cross-domain contributions

- `FlowchartStructureHandler` implements `IActivityStructureHandler` (`Elsa.Workflows.Design.Core`) with `SupportsScopedVariables = true` and `ProjectScopedVariables` — a `Flowchart` is a container scope that can own container-scoped variables visible to its descendant activities, using the same generic scope semantics as `Sequence` (ADR 0027).
