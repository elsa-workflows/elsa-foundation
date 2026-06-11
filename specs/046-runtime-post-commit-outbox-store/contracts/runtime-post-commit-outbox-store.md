# Contract: Runtime Post-Commit Outbox Store

`InMemoryRuntimePostCommitOutboxStore` implements `IRuntimePostCommitOutboxStore` for the default single-node runtime composition.

## Store Boundary

The store owns delivery state for already committed post-commit intents. It does not dispatch intents, claim work, or replace the checkpoint writer.

## Save Pending

`SavePendingAsync(RuntimePostCommitOutboxItem item)` accepts only `RuntimePostCommitOutboxStatus.Pending` items.

Duplicate saves with the same outbox item ID and same intent identity are idempotent while the existing item is still pending. Conflicting duplicate IDs are rejected.

## Query Deliverable

`GetDeliverableAsync(RuntimePostCommitOutboxQuery query)` returns at most `query.Limit` items that:

- are pending or failed-retryable;
- have no `AvailableAt` or have `AvailableAt <= query.Now`;
- match `WorkflowExecutionId` when the query provides one;
- are ordered by `AvailableAt`, then `RecordedAt`, then `OutboxItemId`.

Delivered, final-failed, cancelled, and active delivering items are not deliverable.

## Delivery Results

`RecordDeliveryResultAsync(RuntimePostCommitOutboxDeliveryResult result)` updates the matching item to the recorded result status, increments delivery attempt count, clears active delivery ownership, and preserves retry delay for failed-retryable outcomes.

## Non-Scope

- Processor loops.
- Delivery ownership/claiming.
- Durable transaction provider behavior.
- Runtime scheduler dispatch semantics.
