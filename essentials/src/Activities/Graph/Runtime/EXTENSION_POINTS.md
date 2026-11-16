# Extension points — activity graph Runtime consumer

This project owns the Runtime consumer for compiled reusable activity graphs. The canonical behavior and boundary are specified by [spec 221](../../../../../specs/221-reusable-activity-definitions/spec.md) and its [provider/runtime contract](../../../../../specs/221-reusable-activity-definitions/contracts/provider-runtime-seams.md).

## Registration inventory

| Contribution | Stable identity | Registration | Status |
|---|---|---|---|
| Graph activity Runtime consumer | `elsa.graph-activity` | `IRuntimeActivityConsumerCapability` from `GraphActivitiesRuntimeFeature` | Implemented |
| Transient graph activation | `elsa.graph-activity` / `1` | Scoped `IActivityActivationStrategy` from `GraphActivitiesRuntimeFeature` | Implemented |
| Runtime descriptor schema | `1` | `GraphActivityDescriptor` parsed from the pinned executable node; optional `completeOutcomes` (spec 237) | Implemented |
| Complete activity | `Elsa.Activities.Graph.Runtime.Activities.Complete` | CLR catalog activity (input `outcome`); stages the `elsa.graph.complete` ancestor notification to the nearest `elsa.graph-activity` boundary ([spec 237](../../../../../specs/237-graph-boundary-completion/contracts/complete-activity.md)) | Implemented |

The Runtime consumer may reference only Activity/Workflow Runtime contracts and models. It must not reference Activity Design, Workflow Design, Publishing, the graph Design provider, Elsa 3 compatibility projects, or concrete persistence providers.

## Implementable contributor interfaces

- `IRuntimeActivityConsumerCapability` advertises the stable consumer/schema pair used by publication preflight.
- `IActivityActivationStrategy` creates one fresh graph boundary and owned dependency scope per attempt.
- `IRuntimeStructuralActivity` and the child completion/fault protocols return immutable structural continuation decisions.
- `IRuntimeActivityChildNotificationHandler`: `GraphActivity` handles the `elsa.graph.complete` notification from a `Complete` node at any depth, selects the declared boundary outcome, cancels its entry subtree durably, and completes.
- `IRuntimeActivityCheckpointParticipant` stages graph boundary Durable Value changes for the scheduler-owned checkpoint.
