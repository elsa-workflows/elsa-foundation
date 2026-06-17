# Elsa.Activities.Flowchart

The Flowchart activity is a scoped composite activity. `FlowchartExecutionEngine` schedules child activities through Flowchart-owned execution state that tracks execution scopes, execution paths, arrivals, active children, and diagnostics.

## Scoped execution model

- A Flowchart run starts with a root execution scope and root execution path.
- Scheduled child activities receive generic `flowchart.executionPathId` and `flowchart.executionScopeId` metadata.
- Multi-inbound nodes use an implicit activation-aware join by default: the target waits only for inbound branches that are still active and can still arrive.
- Loopbacks create loop-iteration scopes so arrivals from one iteration cannot satisfy joins in another iteration.
- Flowchart decisions are recorded as diagnostics for scheduling, waiting, joining, loop iteration creation, policy failures, and completion.

`FlowchartExecutionEngine` is the scoped execution seam: it owns runtime mutation, validates policy commands, records arrivals, evaluates implicit joins, creates loop/race scopes, and persists Flowchart state before deferring composite completion.

## Policy contract extension point

Gateway behavior is extensible through the public `IFlowchartPolicy` policy contract. Policies receive a read-only `IFlowchartPolicyContext` and return `FlowchartPolicyDecision` commands. `FlowchartExecutionEngine` validates and applies those commands, keeping mutation and scheduling authority inside the Flowchart runtime.

Built-in policy kinds are defined in `FlowchartPolicyKinds` and registered by `ActivitiesFlowchartFeature`.
