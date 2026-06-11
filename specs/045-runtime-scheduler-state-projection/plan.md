# Implementation Plan: Runtime Scheduler State Projection

**Branch**: `codex/runtime-scheduler-state-projection` | **Date**: 2026-06-11 | **Spec**: [spec.md](./spec.md)

## Summary

Extend checkpoint-state projection to scheduler continuation snapshots. Add `ISchedulerStateStore`, an in-memory default implementation, and projection from `RuntimeCheckpointCommit.StateChanges.Scheduler` under the same serialized checkpoint writer gate used for the previous split-state stores.

## Technical Context

- `SchedulerState` already models the single-writer scheduler continuation snapshot for a workflow execution.
- `RuntimeCheckpointCommit.StateChanges.Scheduler` already carries a typed scheduler state change.
- `IWorkflowSchedulerWorkQueue` stores accepted scheduler work commands and remains separate from scheduler continuation-state snapshots.

## Constitution Check

| Gate | Status | Notes |
| --- | --- | --- |
| Runtime must not depend on Design | PASS | Uses Runtime.Core scheduler contracts only. |
| Runtime state remains split | PASS | Scheduler state receives its own store boundary. |
| Scheduler state stays single-writer | PASS | Store is a projection target; it does not implement concurrent scheduler mutation. |
| Scope control | PASS | No scheduler behavior, queue replacement, recovery scanner, or durable provider. |

## Implementation Steps

1. Add `ISchedulerStateStore` and `InMemorySchedulerStateStore`.
2. Register the in-memory scheduler state store in the runtime API feature.
3. Extend the in-memory checkpoint writer to accept an optional scheduler state store.
4. Validate scheduler projection operation and identities before recording writes.
5. Project scheduler upserts under the writer gate.
6. Add focused store, writer, DI, and architecture validation tests.
7. Update active Speckit pointers and run validation.

## Risks

- The in-memory writer is not a durable transaction provider. Durable providers still need to implement atomic commit semantics across split state stores.
- This slice deliberately does not replace the scheduler work queue or implement full scheduler behavior.
