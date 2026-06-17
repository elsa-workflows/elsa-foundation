# Elsa.Activities.Flowchart

The Flowchart activity is a scoped composite activity. It schedules child activities through Flowchart-owned execution state that tracks execution scopes, execution paths, arrivals, active children, and diagnostics.

## Scoped execution model

- A Flowchart run starts with a root execution scope and root execution path.
- Scheduled child activities receive generic `flowchart.executionPathId` and `flowchart.executionScopeId` metadata.
- Multi-inbound nodes use an implicit activation-aware join by default: the target waits only for inbound branches that are still active and can still arrive.
- Loopbacks create loop-iteration scopes so arrivals from one iteration cannot satisfy joins in another iteration.
- Flowchart decisions are recorded as diagnostics for scheduling, waiting, joining, loop iteration creation, policy failures, and completion.

## Policy extension point

Gateway behavior is extensible through `IFlowchartPolicy`. Policies receive a read-only `IFlowchartPolicyContext` and return `FlowchartPolicyDecision` commands. The Flowchart execution engine validates and applies those commands, keeping mutation and scheduling authority inside the Flowchart runtime.

Built-in policy kinds are defined in `FlowchartPolicyKinds` and registered by `ActivitiesFlowchartFeature`.
