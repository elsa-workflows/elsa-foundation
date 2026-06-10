# Feature Specification: Checkpoint Commit Envelope And Post-Commit Intent Boundary

**Feature Branch**: `codex/runtime-next-execution-slice`
**Created**: 2026-06-10
**Status**: Draft
**Input**: Slice 3 from `docs/reports/elsa-4-runtime-execution-action-plan.md`

## User Scenarios & Testing

### User Story 1 - Commit Runtime State At Named Boundaries (Priority: P1)

A runtime maintainer can package workflow, scheduler, activity, durable value, bookmark, incident, and operational state changes into one checkpoint commit envelope for a named runtime boundary.

**Independent Test**: Build a `RuntimeCheckpointCommit` for `WorkflowStarted`, `ActivityScheduled`, `ActivityStarted`, `ActivityCompleted`, `WorkflowSuspended`, `WorkflowCompleted`, and `IncidentRecorded` using Runtime.Core types only.

### User Story 2 - Keep Persistence Policy Separate From Semantics (Priority: P1)

A runtime composition can decide whether a checkpoint is flushed immediately or deferred without changing the checkpoint name or state-change payload.

**Independent Test**: Run the same checkpoint commit through immediate and deferred test policies and verify the writer receives the same checkpoint semantics with only the persistence decision changed.

### User Story 3 - Dispatch Post-Commit Intents Only After Commit (Priority: P1)

A runtime component can record outbound intents in the checkpoint envelope and deliver them only after the checkpoint writer succeeds.

**Independent Test**: Verify the dispatcher is called after a successful writer call and is not called when the writer fails.

## Requirements

- **FR-001**: Runtime.Core MUST define a `RuntimeCheckpointCommit` envelope that binds one `RuntimeCheckpoint` to atomic state changes and post-commit intents.
- **FR-002**: Runtime.Core MUST represent workflow execution, scheduler, activity execution, durable value, bookmark, incident, and operational state-change categories in the envelope.
- **FR-003**: Runtime.Core MUST keep checkpoint name semantics separate from `RuntimeCheckpointPersistenceDecision`.
- **FR-004**: Runtime.Core MUST define a post-commit intent placeholder contract without implementing a full outbox processor.
- **FR-005**: Runtime.Core MUST provide a commit orchestration service that writes the envelope before dispatching post-commit intents.
- **FR-006**: Runtime.Core MUST NOT dispatch post-commit intents when the checkpoint writer fails.
- **FR-007**: Runtime.Core MUST remain free of Design-owned authored workflow model dependencies.

## Out Of Scope

- Full scheduler execution behavior.
- Full bookmark store/index.
- Full distributed actor provider.
- Full outbox processor.
- Workflow-as-activity execution.
- Elsa 3 live instance resume compatibility.

## Success Criteria

- Runtime tests prove the required checkpoint names can produce checkpoint commits.
- Runtime tests prove immediate and deferred policy modes do not alter checkpoint semantics.
- Runtime tests prove post-commit intents are dispatched only after successful checkpoint commit.
