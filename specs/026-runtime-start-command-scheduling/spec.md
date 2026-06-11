# Runtime Start Command Scheduling

> Supersession note (2026-06-11): requirements that schedule executable artifact start nodes are
> superseded by
> [070-workflow-root-activity-contract](../070-workflow-root-activity-contract/spec.md). Runtime
> start schedules the executable root activity only.

**Feature Branch**: `codex/runtime-start-command-scheduling`
**Created**: 2026-06-11
**Input**: Runtime Execution Seam next slice after Runtime API start dispatch.

## User Scenarios & Testing

1. Given a workflow execution agent accepts a `Start` command, when scheduler work is drained, then the start command schedules runtime work for the executable artifact start nodes.
2. Given the `Start` command payload pins an executable artifact identity, when the scheduler handler runs, then it validates the loaded artifact matches the pinned identity before scheduling start-node work.
3. Given a pinned artifact is missing or mismatched, when the `Start` scheduler work item is handled, then deterministic runtime diagnostics are raised instead of loading authored workflow documents.
4. Given a runtime composition installs custom scheduler handlers, when start scheduling is active, then the default start handler participates as a normal contributor ahead of fallback handlers.

## Requirements

- **FR-001**: Runtime.Core MUST expose a default scheduler work handler for `WorkflowExecutionCommandKind.Start`.
- **FR-002**: The handler MUST deserialize `WorkflowExecutionStartCommandPayload`, load the runtime-owned `WorkflowExecutable`, and validate the executable identity against the pinned payload.
- **FR-003**: The handler MUST enqueue `ScheduleActivity` scheduler work for each start executable node declared by the artifact.
- **FR-004**: Scheduled start-node work MUST reference executable node IDs and workflow execution IDs only; it MUST NOT reference Design-owned authored workflow models.
- **FR-005**: Missing, malformed, or mismatched start payload/artifact state MUST fault deterministically with runtime-scoped exceptions or messages.
- **FR-006**: Runtime API composition MUST register the default start scheduler handler as an ordinary `IWorkflowSchedulerWorkHandler`.
- **FR-007**: This slice MUST NOT execute activity bodies, implement full scheduler graph traversal, write checkpoints, process bookmarks, or add durable persistence.

## Success Criteria

- Runtime tests prove `Start` scheduler work enqueues `ScheduleActivity` work for executable start nodes.
- Runtime tests prove pinned executable identity mismatches are rejected before start-node scheduling.
- Runtime tests prove malformed start payloads fault deterministically.
- Runtime feature registration tests prove the start handler is registered ahead of the fallback no-op handler.
