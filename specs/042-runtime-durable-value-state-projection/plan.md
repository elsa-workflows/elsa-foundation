# Implementation Plan: Runtime Durable Value State Projection

**Branch**: `codex/runtime-durable-value-state-projection` | **Date**: 2026-06-11 | **Spec**: [spec.md](./spec.md)

## Summary

Extend checkpoint-state projection to declared durable values. Add `IDurableValueStateStore`, an in-memory default implementation, and projection from `RuntimeCheckpointCommit.StateChanges.DurableValues` under the same serialized checkpoint writer gate used for workflow, activity, and bookmark state.

## Technical Context

- `DurableValueState` already models declared durable runtime value state.
- `RuntimeCheckpointCommit.StateChanges.DurableValues` already carries durable value state changes.
- `InMemoryRuntimeCheckpointWriter` already serializes projection and commit recording with an async gate.
- Runtime input binding can read durable value state from a context; this slice only adds the store/projection boundary and does not change binding resolution.

## Constitution Check

| Gate | Status | Notes |
| --- | --- | --- |
| Runtime must not depend on Design | PASS | Uses Runtime.Core durable value contracts only. |
| Runtime state remains split | PASS | Durable values receive their own store boundary. |
| Checkpoint-driven state | PASS | Projection happens from accepted checkpoint commits. |
| Scope control | PASS | No storage driver/provider, capture middleware, history projection, or recovery scanner. |

## Implementation Steps

1. Add `IDurableValueStateStore` and `InMemoryDurableValueStateStore`.
2. Register the in-memory durable value store in the runtime API feature.
3. Extend the in-memory checkpoint writer to accept an optional durable value state store.
4. Validate durable value projection operations and identities before recording writes.
5. Project durable value upserts/deletes under the writer gate.
6. Add focused store, writer, DI, and architecture validation tests.
7. Update active Speckit pointers and run validation.

## Risks

- The in-memory writer is not a durable transaction provider. Durable providers still need to implement atomic commit semantics across split state stores.
- This slice deliberately does not implement value storage drivers or output capture middleware.
