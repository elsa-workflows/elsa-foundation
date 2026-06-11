# Feature Specification: Runtime Activity Fault Incidents

**Feature Branch**: `codex/runtime-activity-fault-incidents`
**Created**: 2026-06-11
**Status**: Draft
**Input**: Continue Runtime Execution Seam Slice 7 after incident contracts exist. Activity invocation faults should create minimal runtime incident continuation state through a named checkpoint instead of only mutating activity state.

## Scenarios & Tests

1. Given activity input materialization fails, when invocation handles the failure, then runtime commits an `IncidentRecorded` checkpoint containing both the faulted `ActivityExecutionState` and an `IncidentState`.
2. Given activity execution throws, when invocation handles the failure, then runtime commits an `IncidentRecorded` checkpoint and the faulted activity state references the incident ID.
3. Given activity invocation completes successfully or suspends durably by bookmark, then no fault incident is recorded.

## Requirements

- **FR-001**: Activity invocation faults MUST create `IncidentState` with workflow execution ID, activity execution ID, executable node ID, failure type, message, and blocking status.
- **FR-002**: Faulted `ActivityExecutionState` MUST reference the incident ID in `IncidentIds`.
- **FR-003**: Fault state and incident state MUST be committed through `RuntimeCheckpointNames.IncidentRecorded`.
- **FR-004**: Incident metadata MUST include scheduler work item ID, command ID, fault sub-status, activity execution ID, and executable node ID.
- **FR-005**: Existing fault metadata on `ActivityExecutionState` MUST remain available for the current narrow behavior.
- **FR-006**: Incident recording MUST NOT introduce history/audit payload persistence or Design-owned model dependencies.

## Non-Goals

- Rich incident history projection persistence.
- Domain retry policy execution.
- Workflow-level fault policy.
- Bookmark resume fault incident recording.
- User-facing incident APIs.

## Acceptance Criteria

- Tests prove input materialization faults create an incident checkpoint and projected incident state.
- Tests prove activity execution faults create an incident checkpoint and activity state incident reference.
- Existing completion, bookmark suspension, and architecture tests pass.
