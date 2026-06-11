# Contract: Runtime Post-Commit Outbox Processor

`IRuntimePostCommitOutboxProcessor` performs one bounded delivery pass over deliverable post-commit outbox items.

## Processing Rule

1. Query `IRuntimePostCommitOutboxStore.GetDeliverableAsync` with current runtime time, limit, and optional workflow execution filter.
2. For each item in store-provided order, dispatch `item.Intent`.
3. On successful dispatch, record `RuntimePostCommitOutboxStatus.Delivered`.
4. On failed dispatch, record `RuntimePostCommitOutboxStatus.FailedRetryable`; store implementations normalize exhausted retry policies.
5. If failure-result recording also fails, report a processor exception that keeps dispatch failure primary and exposes the recording failure separately.
6. Processor results report the requested delivery result status; the store remains authoritative for the final persisted outbox item status.

## Non-Scope

- Delivery claiming.
- Background polling.
- Durable provider storage.
- Wait-dependent post-commit intent activation.
