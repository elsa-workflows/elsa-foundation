# Extension points — activity graph Runtime consumer

This project owns the Runtime consumer for compiled reusable activity graphs. The canonical behavior and boundary are specified by [spec 092](../../../../../../specs/092-reusable-activity-definitions/spec.md) and its [provider/runtime contract](../../../../../../specs/092-reusable-activity-definitions/contracts/provider-runtime-seams.md).

## Registration inventory

| Contribution | Stable identity | Registration | Status |
|---|---|---|---|
| Graph activity Runtime consumer | `elsa.graph-activity` | `IRuntimeActivityConsumerCapability` from `GraphActivitiesRuntimeFeature` | Implemented |
| Transient graph activation | `elsa.graph-activity` / `1` | Scoped `IActivityActivationStrategy` from `GraphActivitiesRuntimeFeature` | Implemented |
| Runtime descriptor schema | `1` | `GraphActivityDescriptor` parsed from the pinned executable node | Implemented |

The Runtime consumer may reference only Activity/Workflow Runtime contracts and models. It must not reference Activity Design, Workflow Design, Publishing, the graph Design provider, Elsa 3 compatibility projects, or concrete persistence providers.

## Implementable contributor interfaces

- `IRuntimeActivityConsumerCapability` advertises the stable consumer/schema pair used by publication preflight.
- `IActivityActivationStrategy` creates one fresh graph boundary and owned dependency scope per attempt.
- `IRuntimeStructuralActivity` and the child completion/fault protocols return immutable structural continuation decisions.
- `IRuntimeActivityCheckpointParticipant` stages graph boundary Durable Value changes for the scheduler-owned checkpoint.
