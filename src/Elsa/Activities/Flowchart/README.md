# Elsa.Activities.Flowchart

The Flowchart activity is a scoped composite activity. `FlowchartExecutionEngine` schedules child activities through Flowchart-owned execution state that tracks execution scopes, execution paths, arrivals, active children, and diagnostics.

## Scoped execution model

- A Flowchart run starts with a root execution scope and root execution path.
- Scheduled child activities receive generic `flowchart.executionPathId` and `flowchart.executionScopeId` metadata.
- Multi-inbound nodes use an implicit activation-aware join by default: the target waits only for inbound branches that are still active and can still arrive.
- Loopbacks create loop-iteration scopes so arrivals from one iteration cannot satisfy joins in another iteration.
- Branch faults are fault-aware (#308): a Flowchart join requires every inbound branch, so a faulted inbound branch can never let the join fire. Rather than hang, the Flowchart's runtime structural fault callback returns a fault decision for the composite (surfacing a composite incident), mirroring the `Parallel` fork/join composite; the faulted leaf keeps its own blocking incident.
- Flowchart decisions are recorded as diagnostics for scheduling, waiting, joining, loop iteration creation, policy failures, faults, and completion.

`FlowchartExecutionEngine` is the scoped execution seam: it owns runtime mutation, validates policy commands, records arrivals, evaluates implicit joins, creates loop/race scopes, and persists Flowchart state before deferring composite completion.

`Flowchart` participates in the engine-only `IRuntimeStructuralActivity` protocol. Its initial execution and
its child completion/fault callbacks each return one immutable `RuntimeStructuralContinuation` decision;
the runtime alone applies that decision to complete, defer, fault, or cancel the composite.

## Policy contract extension point

Gateway behavior is extensible through the public `IFlowchartPolicy` policy contract. Policies receive a read-only `IFlowchartPolicyContext` and return `FlowchartPolicyDecision` commands. `FlowchartExecutionEngine` validates and applies those commands, keeping mutation and scheduling authority inside the Flowchart runtime.

Built-in policy kinds are defined in `FlowchartPolicyKinds` and registered by `ActivitiesFlowchartFeature`.
