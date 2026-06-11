# Feature Specification: Runtime Operational State Projection

**Feature Branch**: `codex/runtime-operational-state-projection`
**Created**: 2026-06-11
**Status**: Draft
**Input**: Continue the Runtime Execution Seam after incident state projection. Checkpoint commits already carry operational state changes; the default in-memory writer should project those changes into an operational state store without implementing recovery scanning, outbox delivery, or domain retry behavior.

## Scenarios & Tests

1. Given a checkpoint commit with an operational state upsert, when the in-memory checkpoint writer accepts the commit, then the operational state is saved in the operational state store.
2. Given a later checkpoint commit with an operational state upsert for the same operational state ID, when it is accepted, then the store reflects the later lease or heartbeat state.
3. Given the same commit ID is replayed with conflicting operational state, then projection remains idempotent and does not overwrite the first accepted state.
4. Given an unsupported operational state operation is supplied for projection, then the writer rejects the commit before recording it.

## Requirements

- **FR-001**: Runtime.Core MUST define `IOperationalStateStore` as the minimal continuation-state boundary for runtime operational coordination state.
- **FR-002**: Runtime.Core MUST provide an in-memory operational state store for the current default runtime composition.
- **FR-003**: The default in-memory checkpoint writer MUST project operational state upserts into `IOperationalStateStore`.
- **FR-004**: Operational state projection MUST remain commit-ID idempotent.
- **FR-005**: Operational state projection MUST validate operation, `StateId`, and checkpoint workflow execution ID before recording a write.
- **FR-006**: Operational recovery, outbox delivery, actor placement, and domain retry behavior MUST remain outside this store projection slice.
- **FR-007**: Runtime execution projects MUST remain free of Design-owned authored workflow model dependencies.

## Non-Goals

- Recovery scanner implementation.
- Post-commit outbox processor implementation.
- Actor-provider lease enforcement.
- Domain retry policy implementation.
- Durable persistence provider implementation.

## Acceptance Criteria

- `InMemoryRuntimeCheckpointWriter` can project operational state upserts into `IOperationalStateStore`.
- Operational states can be queried by workflow execution ID and operational state ID.
- Duplicate commit IDs do not reapply conflicting operational state.
- Unsupported or mismatched operational state changes are rejected before write recording.
- The API feature registers the in-memory operational store before resolving the checkpoint writer.
- Focused runtime and architecture tests pass.
