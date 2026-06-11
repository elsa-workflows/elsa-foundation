# Feature Specification: Runtime Storage Driver Boundary

**Feature Branch**: `codex/runtime-storage-driver-boundary`
**Created**: 2026-06-11
**Status**: Draft
**Input**: Continue runtime execution seam cleanup after durable value state exists. The legacy `IStorageDriver`/`IStorageDriverContext` contracts model variable-style object reads and writes and depend on expression variables, which conflicts with the locked durable-value boundary.

## Scenarios & Tests

1. Given runtime code needs durable cross-suspension values, when contracts are inspected, then Runtime exposes `DurableValueState` and `IDurableValueStateStore` instead of storage-driver read/write/delete contracts.
2. Given runtime API composition is built, when runtime services are registered, then durable value state storage remains available and no storage-driver service is registered.
3. Given the solution is loaded, when runtime projects are enumerated, then the dead `Runtime.StorageDrivers` project is not part of the solution.

## Requirements

- **FR-001**: Runtime.Core MUST remove the legacy `IStorageDriver` contract.
- **FR-002**: Runtime.Core MUST remove the legacy `IStorageDriverContext` contract and its expression-variable dependency.
- **FR-003**: Runtime MUST treat `DurableValueState` and `IDurableValueStateStore` as the runtime durable-value continuation boundary.
- **FR-004**: Runtime API composition MUST NOT register storage-driver contracts.
- **FR-005**: The dead in-memory storage-driver implementation project MUST be removed from the solution.
- **FR-006**: Removal MUST NOT introduce Design-owned execution dependencies or Elsa 3 live-instance resume compatibility.

## Non-Goals

- Full durable value persistence provider implementation.
- Elsa 3 storage-driver compatibility mapping.
- Runtime input/output binding behavior changes.
- Design model storage-driver field cleanup.

## Acceptance Criteria

- No production source reference to `IStorageDriver` or `IStorageDriverContext` remains.
- Runtime tests prove the durable value store remains the durability seam.
- Runtime API feature tests prove no storage-driver registration is introduced.
- Runtime and architecture validation pass.
