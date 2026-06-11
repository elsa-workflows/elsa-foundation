# Feature Specification: Runtime Durable Value State Projection

**Feature Branch**: `codex/runtime-durable-value-state-projection`
**Created**: 2026-06-11
**Status**: Draft
**Input**: Continue the Runtime Execution Seam after bookmark state projection. Checkpoint commits already carry durable value state changes; the default in-memory writer should project those changes into a durable value state store without implementing full durable value storage providers.

## Scenarios & Tests

1. Given a checkpoint commit with a durable value state upsert, when the in-memory checkpoint writer accepts the commit, then the durable value state is saved in the durable value state store.
2. Given a later checkpoint commit for the same durable value with a delete operation, when it is accepted, then the durable value state is removed from the store.
3. Given the same commit ID is replayed with conflicting durable value state, then projection remains idempotent and does not overwrite the first accepted state.
4. Given an unsupported durable value state operation is supplied for projection, then the writer rejects the commit before recording it.

## Requirements

- **FR-001**: Runtime.Core MUST define `IDurableValueStateStore` as the minimal continuation-state boundary for declared durable values.
- **FR-002**: Runtime.Core MUST provide an in-memory durable value state store for the current default runtime composition.
- **FR-003**: The default in-memory checkpoint writer MUST project durable value state upserts and deletes into `IDurableValueStateStore`.
- **FR-004**: Durable value state projection MUST remain commit-ID idempotent.
- **FR-005**: Durable value state projection MUST validate operation, `StateId`, and checkpoint workflow execution ID before recording a write.
- **FR-006**: Runtime execution projects MUST remain free of Design-owned authored workflow model dependencies.

## Non-Goals

- Durable value storage drivers/providers beyond `DurableValueState` continuation state.
- Input binding resolution changes.
- Activity output capture middleware.
- History/audit value snapshots.
- Full checkpoint replay/recovery scanner.

## Acceptance Criteria

- `InMemoryRuntimeCheckpointWriter` can project durable value state upserts and deletes into `IDurableValueStateStore`.
- Duplicate commit IDs do not reapply conflicting durable value state.
- Unsupported or mismatched durable value state changes are rejected before write recording.
- The API feature registers the in-memory durable value store before resolving the checkpoint writer.
- Focused runtime and architecture tests pass.
