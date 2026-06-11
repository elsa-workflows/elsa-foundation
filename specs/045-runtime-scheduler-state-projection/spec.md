# Feature Specification: Runtime Scheduler State Projection

**Feature Branch**: `codex/runtime-scheduler-state-projection`
**Created**: 2026-06-11
**Status**: Draft
**Input**: Continue the Runtime Execution Seam after operational state projection. Checkpoint commits already carry scheduler state changes; the default in-memory writer should project those changes into a scheduler state store without conflating that store with the scheduler work queue.

## Scenarios & Tests

1. Given a checkpoint commit with a scheduler state upsert, when the in-memory checkpoint writer accepts the commit, then the scheduler state is saved in the scheduler state store.
2. Given a later checkpoint commit with a scheduler state upsert for the same workflow execution, when it is accepted, then the store reflects the later scheduler version.
3. Given the same commit ID is replayed with conflicting scheduler state, then projection remains idempotent and does not overwrite the first accepted state.
4. Given an unsupported scheduler state operation is supplied for projection, then the writer rejects the commit before recording it.

## Requirements

- **FR-001**: Runtime.Core MUST define `ISchedulerStateStore` as the minimal continuation-state boundary for scheduler snapshots.
- **FR-002**: Runtime.Core MUST provide an in-memory scheduler state store for the current default runtime composition.
- **FR-003**: The default in-memory checkpoint writer MUST project scheduler state upserts into `ISchedulerStateStore`.
- **FR-004**: Scheduler state projection MUST remain commit-ID idempotent.
- **FR-005**: Scheduler state projection MUST validate operation, `StateId`, and checkpoint workflow execution ID before recording a write.
- **FR-006**: Scheduler state storage MUST remain distinct from `IWorkflowSchedulerWorkQueue`.
- **FR-007**: Runtime execution projects MUST remain free of Design-owned authored workflow model dependencies.

## Non-Goals

- Full scheduler behavior.
- Durable provider implementation.
- Queue/drainer replacement.
- Recovery scanner or actor-provider implementation.

## Acceptance Criteria

- `InMemoryRuntimeCheckpointWriter` can project scheduler state upserts into `ISchedulerStateStore`.
- Scheduler state can be queried by workflow execution ID.
- Duplicate commit IDs do not reapply conflicting scheduler state.
- Unsupported or mismatched scheduler state changes are rejected before write recording.
- The API feature registers the in-memory scheduler state store before resolving the checkpoint writer.
- Focused runtime and architecture tests pass.
