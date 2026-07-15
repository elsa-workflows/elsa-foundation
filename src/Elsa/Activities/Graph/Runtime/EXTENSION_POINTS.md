# Extension points — activity graph Runtime consumer

This project owns the Runtime consumer for compiled reusable activity graphs. The canonical behavior and boundary are specified by [spec 092](../../../../../../specs/092-reusable-activity-definitions/spec.md) and its [provider/runtime contract](../../../../../../specs/092-reusable-activity-definitions/contracts/provider-runtime-seams.md).

## Registration inventory

| Contribution | Stable identity | Registration | Status |
|---|---|---|---|
| Graph activity Runtime consumer | `elsa.graph-activity` | Future Runtime consumer contribution from `GraphActivitiesRuntimeFeature` | Planned by T041 and T047 |
| Runtime descriptor schema | `1` | Consumer-owned construction and activation | Planned by T010–T013 and T041 |

The Runtime consumer may reference only Runtime/Core contracts. It must not reference Activity Design, Workflow Design, Publishing, the graph Design provider, Elsa 3 compatibility projects, or concrete persistence providers.

## Implementable contributor interfaces

The stable consumer interfaces land in `Elsa.Activities.Runtime.Core` during the foundational contract slice. This catalog will name their exact signatures when those contracts exist.
