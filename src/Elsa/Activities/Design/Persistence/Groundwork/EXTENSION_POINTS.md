# Extension points — Activities.Design.Persistence.Groundwork

This feature supplies the current-only activity-design implementations over the public Groundwork v2
row, query, and store APIs. Domain contracts live in `Elsa.Activities.Design.Persistence.Core`.

## Replacement contracts

The registration replaces the following contracts with the indicated implementations:

| Contract | Implementation |
|---|---|
| `IActivityDefinitionStore` | `GroundworkActivityDefinitionStore` |
| `IActivityDefinitionVersionStore` | `GroundworkActivityDefinitionVersionStore` |
| `IAddActivityDefinitionCommand` | `GroundworkAddActivityDefinitionCommand` |
| `IAddActivityDefinitionVersionCommand` | `GroundworkAddActivityDefinitionVersionCommand` |
| `IActivityAvailabilitySettingsStore` | `GroundworkActivityAvailabilitySettingsStore` |
| `IActivityDefinitionManagementProjectionStore` | `GroundworkActivityDefinitionManagementProjectionStore` |
| `IActivityDefinitionAuthoringStore`, `IActivityDefinitionDraftStore`, `IActivityDefinitionVersionPublicationStore` | `GroundworkReusableActivityStores` |
| `IActivityDefinitionLayoutStore`, `IActivityDraftValidationStore`, `IActivityForkStore`, `IActivityDirectDependencyStore` | `GroundworkReusableActivityStores` |
| `IActivityDependencyProjectionStore`, `IActivityDependencyProjectionRebuilder` | `GroundworkActivityDependencyProjection` |
| `IRecommendedActivityDefinitionPickerStore` | `GroundworkRecommendedActivityDefinitionPickerStore` |
| `IActivityUpgradePlanStore`, `IActivityUpgradeApplyReceiptStore` | `GroundworkActivityUpgradePlanStore` |
| activity authoring, draft, fork, proposal, lifecycle, and recommendation commands | `GroundworkReusableActivityStores` |

`IActivityDefinitionLookup` remains the core composition service and resolves the registered definition
store.

## v2 storage boundary

`ActivitiesDesignStorageManifest` declares one scoped `StorageUnit` for each activity-design row family.
Units have stable identities, explicit projected fields, compound indexes, and optimistic concurrency.
`GroundworkV2ActivityDesignStore` maps activity-design predicates and orders to the public Groundwork
query AST, obtains sessions from `IGroundworkStorageSessionSource`, and uses only provider-owned
transactions for writes.

All query paths carry a deterministic tie-breaker and use bounded pages. Search and projection values
are length-bounded before they reach indexed columns; values are never silently truncated. The current
access context is authoritative for scope isolation, and privileged cross-scope access must be explicit.

## Atomic writer seam

`IDesignAtomicWriter` and `GroundworkDesignAtomicWrite` provide replay-safe multi-unit writes for the
activity-definition create commands. The operation marker is a v2 activity-design unit and is committed
with the mutated rows. A matching request replays the authoritative result; a different request using the
same operation identity is rejected as a conflict.

## Registration

`AddGroundworkActivitiesDesignStores()` admits every activity-design unit into the selected v2 target,
registers the v2 row store and atomic writer, then replaces the domain contracts above. This feature does
not register a legacy store, migration, fallback, alias, or dual-write path.
