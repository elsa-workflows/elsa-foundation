# Feature Specification: Runtime Diagnostics History And Incidents

**Feature Branch**: `codex/runtime-diagnostics-history-incidents`
**Created**: 2026-06-10
**Input**: Slice 7 from `docs/reports/elsa-4-runtime-execution-action-plan.md`

## User Scenarios & Testing

### User Story 1 - History Is Observability Only (Priority: P1)

Runtime can emit workflow, activity, bookmark, value, incident, scheduler, and operational history categories without making history part of continuation state.

**Independent Test**: Inspect runtime checkpoint state-change contracts and assert no history/audit category or state collection is needed to continue execution.

### User Story 2 - Incidents Are Queryable Runtime State (Priority: P1)

Runtime can persist minimal incident continuation state so blocking incidents are queryable without replaying history records.

**Independent Test**: Create blocking and resolved incidents, query blocking incidents directly, and assert richer diagnostic payload lives in an incident history projection.

### User Story 3 - Payload Capture Is Policy-Controlled (Priority: P1)

Runtime omits input/output snapshots by default and excludes sensitive payloads unless an explicit policy allows capture.

**Independent Test**: Use the default payload capture policy for workflow/activity input and output subjects and assert no payload is captured; assert incident diagnostics default to metadata-only.

## Requirements

- **FR-001**: Runtime.Core MUST define locked execution history event categories for workflow, activity, bookmark, value, incident, scheduler, and operational lifecycle observations.
- **FR-002**: Runtime.Core MUST define workflow and activity lifecycle history projection models using runtime execution identities only.
- **FR-003**: Runtime.Core MUST define `IncidentState` as continuation state separate from incident history projection payloads.
- **FR-004**: Checkpoint state-change envelopes MUST carry typed incident state and MUST NOT carry history/audit records as continuation state.
- **FR-005**: Runtime.Core MUST define a payload capture policy contract.
- **FR-006**: The default payload capture policy MUST omit workflow/activity input and output snapshots.
- **FR-007**: The default payload capture policy MUST exclude sensitive payloads.
- **FR-008**: Runtime diagnostics/history contracts MUST NOT introduce Design-owned workflow document dependencies.

## Out of Scope

- History persistence store/query provider.
- Full incident strategy execution.
- Full audit redaction/serialization engine.
- Operational recovery behavior.
- Runtime retry/compensation behavior.

## Success Criteria

- **SC-001**: Tests prove runtime state can continue without history/audit records.
- **SC-002**: Tests prove blocking incidents are represented as queryable runtime state.
- **SC-003**: Tests prove input/output payload snapshots are omitted by default.
- **SC-004**: Runtime and architecture dependency tests pass.
