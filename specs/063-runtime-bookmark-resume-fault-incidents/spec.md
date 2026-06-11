# Feature Specification: Runtime Bookmark Resume Fault Incidents

**Feature Branch**: `codex/runtime-bookmark-resume-fault-incidents`
**Created**: 2026-06-11
**Status**: Draft
**Input**: Continue Runtime Execution Seam Slice 7 after invocation faults record incidents. Bookmark resume faults still mutate activity state directly and should use the same minimal runtime incident continuation state.

## Scenarios & Tests

1. Given bookmark resume input materialization fails, when resume handling catches the failure, then runtime commits an `IncidentRecorded` checkpoint containing both the faulted `ActivityExecutionState` and an `IncidentState`.
2. Given a resume target is missing, invalid, or throws, when resume handling catches the failure, then runtime commits an `IncidentRecorded` checkpoint and the faulted activity state references the incident ID.
3. Given bookmark resume succeeds or sees an already completed activity, then no fault incident is recorded.

## Requirements

- **FR-001**: Bookmark resume faults MUST create `IncidentState` with workflow execution ID, activity execution ID, executable node ID, failure type, message, and blocking status.
- **FR-002**: Faulted `ActivityExecutionState` MUST reference the incident ID in `IncidentIds`.
- **FR-003**: Fault state and incident state MUST be committed through `RuntimeCheckpointNames.IncidentRecorded`.
- **FR-004**: Incident metadata MUST include scheduler work item ID, command ID, fault sub-status, activity execution ID, executable node ID, bookmark ID, and resume target ID.
- **FR-005**: Existing resume fault metadata on `ActivityExecutionState` MUST remain available for the current narrow behavior.
- **FR-006**: Invocation and resume fault paths SHOULD share incident commit logic instead of duplicating checkpoint construction.
- **FR-007**: Incident recording MUST NOT consume or delete the bookmark, emit history/audit continuation state, or introduce Design-owned model dependencies.

## Non-Goals

- User-facing incident APIs.
- Workflow-level fault policy.
- Domain retry policy execution.
- Resume target discovery redesign.
- Rich incident history projection persistence.

## Acceptance Criteria

- Tests prove bookmark resume target faults create an incident checkpoint and projected incident state.
- Tests prove bookmark resume input materialization faults create an incident checkpoint and preserve the bookmark.
- Existing successful resume, invocation fault incident, activity runtime, and architecture tests pass.
