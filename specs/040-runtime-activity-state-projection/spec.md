# Feature Specification: Runtime Activity Execution State Projection

**Feature Branch**: `codex/runtime-activity-state-projection`
**Created**: 2026-06-11
**Status**: Draft
**Input**: Continue the Runtime Execution Seam after workflow execution state projection. Checkpoint commits already carry activity execution state changes; the default in-memory writer should project those changes into the existing activity execution state store.

## Scenarios & Tests

1. Given a checkpoint commit with an activity execution state upsert, when the in-memory checkpoint writer accepts the commit, then the activity execution state is saved in the activity execution state store.
2. Given a later checkpoint commit for the same activity execution, when it is accepted, then the state store reflects the later activity lifecycle state.
3. Given the same commit ID is replayed with conflicting activity state, then projection remains idempotent and does not overwrite the first accepted state.
4. Given an unsupported activity state operation is supplied for projection, then the writer rejects the commit before recording it.

## Requirements

- **FR-001**: The default in-memory checkpoint writer MUST project activity execution state upserts into `IActivityExecutionStateStore`.
- **FR-002**: Activity state projection MUST remain commit-ID idempotent.
- **FR-003**: Activity state projection MUST validate `StateId`, activity execution ID, and checkpoint workflow execution ID before recording a write.
- **FR-004**: Activity state projection MUST use the same serialized write/projection gate as workflow execution state projection.
- **FR-005**: Runtime execution projects MUST remain free of Design-owned authored workflow model dependencies.

## Non-Goals

- Moving scheduler activity lifecycle transitions behind new checkpoint boundaries.
- Durable database/provider implementation.
- Applying scheduler, bookmark, durable value, incident, or operational state changes.
- Full checkpoint replay/recovery scanner.

## Acceptance Criteria

- `InMemoryRuntimeCheckpointWriter` can project activity execution state upserts into `IActivityExecutionStateStore`.
- Duplicate commit IDs do not reapply conflicting activity state.
- Unsupported or mismatched activity state changes are rejected before write recording.
- Focused runtime and architecture tests pass.
