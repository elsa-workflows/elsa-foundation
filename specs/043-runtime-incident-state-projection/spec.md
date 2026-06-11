# Feature Specification: Runtime Incident State Projection

**Feature Branch**: `codex/runtime-incident-state-projection`
**Created**: 2026-06-11
**Status**: Draft
**Input**: Continue the Runtime Execution Seam after durable value state projection. Checkpoint commits already carry incident state changes; the default in-memory writer should project those changes into an incident state store without implementing incident strategy execution or history persistence.

## Scenarios & Tests

1. Given a checkpoint commit with an incident state append, when the in-memory checkpoint writer accepts the commit, then the incident state is saved in the incident state store.
2. Given a later checkpoint commit with an incident state upsert for the same incident, when it is accepted, then the store reflects the later terminal incident status.
3. Given the same commit ID is replayed with conflicting incident state, then projection remains idempotent and does not overwrite the first accepted state.
4. Given an unsupported incident state operation is supplied for projection, then the writer rejects the commit before recording it.

## Requirements

- **FR-001**: Runtime.Core MUST define `IIncidentStateStore` as the minimal continuation-state boundary for execution-affecting incidents.
- **FR-002**: Runtime.Core MUST provide an in-memory incident state store for the current default runtime composition.
- **FR-003**: The default in-memory checkpoint writer MUST project incident state appends and upserts into `IIncidentStateStore`.
- **FR-004**: Incident state projection MUST remain commit-ID idempotent.
- **FR-005**: Incident state projection MUST validate operation, `StateId`, and checkpoint workflow execution ID before recording a write.
- **FR-006**: Runtime.Core MUST keep incident history projection outside continuation state.
- **FR-007**: Runtime execution projects MUST remain free of Design-owned authored workflow model dependencies.

## Non-Goals

- Incident strategy execution.
- Incident retry, compensation, or intervention workflows.
- Incident history persistence/query provider.
- Audit payload capture or redaction engine.
- Full checkpoint replay/recovery scanner.

## Acceptance Criteria

- `InMemoryRuntimeCheckpointWriter` can project incident state append/upsert changes into `IIncidentStateStore`.
- Blocking incidents can be listed directly from the incident state store.
- Duplicate commit IDs do not reapply conflicting incident state.
- Unsupported or mismatched incident state changes are rejected before write recording.
- The API feature registers the in-memory incident store before resolving the checkpoint writer.
- Focused runtime and architecture tests pass.
