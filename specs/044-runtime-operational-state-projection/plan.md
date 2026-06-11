# Implementation Plan: Runtime Operational State Projection

**Branch**: `codex/runtime-operational-state-projection` | **Date**: 2026-06-11 | **Spec**: [spec.md](./spec.md)

## Summary

Extend checkpoint-state projection to operational coordination state. Add `IOperationalStateStore`, an in-memory default implementation, and projection from `RuntimeCheckpointCommit.StateChanges.Operational` under the same serialized checkpoint writer gate used for the previous split-state stores.

## Technical Context

- `OperationalState` already models lease, heartbeat, drain, interruption, and pending post-commit intent references.
- `RuntimeCheckpointCommit.StateChanges.Operational` already carries typed operational state changes.
- Operational recovery and post-commit outbox contracts already exist separately from this projection slice.
- This slice projects upserts only; clearing operational child fields can be represented by replacing `OperationalState` with null child values.

## Constitution Check

| Gate | Status | Notes |
| --- | --- | --- |
| Runtime must not depend on Design | PASS | Uses Runtime.Core operational contracts only. |
| Runtime state remains split | PASS | Operational state receives its own store boundary. |
| Operational recovery is not domain retry | PASS | Store projection does not implement recovery scanner or retry policy behavior. |
| Scope control | PASS | No recovery scanner, outbox processor, lease enforcement, actor placement, or durable provider. |

## Implementation Steps

1. Add `IOperationalStateStore` and `InMemoryOperationalStateStore`.
2. Register the in-memory operational store in the runtime API feature.
3. Extend the in-memory checkpoint writer to accept an optional operational state store.
4. Validate operational projection operation and identities before recording writes.
5. Project operational upserts under the writer gate.
6. Add focused store, writer, DI, and architecture validation tests.
7. Update active Speckit pointers and run validation.

## Risks

- The in-memory writer is not a durable transaction provider. Durable providers still need to implement atomic commit semantics across split state stores.
- This slice deliberately does not execute operational recovery scans or persist post-commit outbox delivery state.
