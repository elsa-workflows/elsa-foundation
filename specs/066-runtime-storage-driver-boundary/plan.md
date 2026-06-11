# Implementation Plan: Runtime Storage Driver Boundary

**Branch**: `codex/runtime-storage-driver-boundary` | **Date**: 2026-06-11 | **Spec**: [spec.md](./spec.md)

## Summary

Remove the legacy runtime storage-driver contracts and dead memory-driver project so runtime durable values remain represented by `DurableValueState` and `IDurableValueStateStore`.

## Technical Context

- `IStorageDriver` exposes generic object read/write/delete methods and does not carry durable value lifecycle, storage mode, artifact pinning, checkpoint, or activity execution identity.
- `IStorageDriverContext` depends on expression variables, which makes the runtime durable-value boundary depend on authored/expression variable shape.
- Runtime durable value contracts already model lifecycle, inline/external storage, source activity execution, and checkpoint projection.

## Constitution Check

| Gate | Status | Notes |
| --- | --- | --- |
| Runtime state split | PASS | Durable value state remains its own continuation-state contract. |
| Runtime executes pinned artifacts | PASS | Removal avoids a generic value driver that is not tied to pinned runtime execution state. |
| Runtime must not depend on Design | PASS | The slice removes an expression-variable dependency from runtime storage contracts. |
| Elsa 3 compatibility bounded | PASS | Does not preserve Elsa 3 storage-driver behavior as runtime continuation state. |

## Implementation Steps

1. Add slice artifacts and update active Speckit pointers.
2. Mark previous slice PR-loop task complete.
3. Remove `IStorageDriver`, `IStorageDriverContext`, and the dead in-memory storage-driver project.
4. Remove the storage-driver project from the solution.
5. Add/adjust regression tests proving durable value state/store remain the runtime durability seam and storage-driver registrations are absent.
6. Refresh generated maps if project inputs changed.
7. Run focused validation and self-review.

## Risks

- External consumers may have referenced the legacy storage-driver contract. The contract was not registered by runtime API composition, and durable runtime persistence should flow through durable value state stores and checkpoint providers.
