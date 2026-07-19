# Data Model

## ConsumedSchedulerWorkItem (new)

Fence-carrying record folded into a checkpoint change set to consume (delete) one claimed scheduler work item inside the checkpoint commit's unit-of-work.

| Field | Type | Notes |
|---|---|---|
| `WorkflowExecutionId` | string | Isolation key; must equal the commit's workflow execution id. |
| `WorkItemId` | string | The claimed FIFO-head item to consume. |
| `ClaimOwnerId` | string | Fence: the claim owner. Stable across renewal. |
| `FencingToken` | long | Fence: the claim fencing token. Advances only on a successor reclaim. |

`FromClaim(RuntimeSchedulerWorkClaim)` projects the record. No revision field (see plan D1).

## RuntimeCheckpointStateChangeSet (extended)

Adds `IReadOnlyCollection<ConsumedSchedulerWorkItem> ConsumedSchedulerWorkItems` (defaults to empty) and `WithConsumedSchedulerWorkItems(...)`, mirroring `ActivityScopeCleanups` / `PostCommitOutbox`.

## RuntimeCheckpointCommitStoreResult (extended)

Adds `IReadOnlyCollection<string> ConsumedSchedulerWorkItemIds` — the work-item ids the store durably deleted (or, on replay, recorded in the marker).

## Durable replay markers (extended)

- Groundwork `CheckpointCommitMarker` records the consumed ids alongside `PendingPostCommitWorkIds`.
- In-memory `RuntimeCheckpointCommitRecord` records the consumed ids.

## Claim transport

`IRuntimeConsumedSchedulerWorkClaimAccessor` (scoped): `PendingConsume` (get), `WasConsumedDurably` (get), `Begin(ConsumedSchedulerWorkItem)` → `IDisposable`, `MarkConsumedDurably(workItemId)`.
