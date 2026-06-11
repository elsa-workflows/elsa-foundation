# Feature Specification: Runtime Downstream Scheduling

> Supersession note (2026-06-11): workflow-level executable edge traversal is superseded by
> [070-workflow-root-activity-contract](../070-workflow-root-activity-contract/spec.md). Downstream
> traversal belongs to composite activity runtime behavior, not `WorkflowExecutable`.

**Feature Branch**: `codex/runtime-downstream-scheduling`
**Created**: 2026-06-11
**Status**: Draft
**Input**: Continue deterministic activity completion propagation after checkpoint commit dispatch exists.

## Scenarios & Tests

1. Given completion continuation work names outcomes that match executable outgoing edges, when the activity-completed checkpoint commits, then Workflows Runtime enqueues `ScheduleActivity` work for each matching target node after the commit succeeds.
2. Given checkpoint writing fails, when checkpoint work has downstream scheduler intents, then no downstream scheduler work is enqueued.
3. Given a completion outcome does not match any outgoing executable edge, when continuation scheduling runs, then no downstream scheduler work intent is created.

## Requirements

- **FR-001**: Downstream activity scheduling MUST be represented as deterministic scheduler work, not recursive completion bubbling.
- **FR-002**: Downstream scheduler work MUST be delivered only after the `ActivityCompleted` checkpoint commit succeeds.
- **FR-003**: Continuation scheduling MUST traverse runtime-owned `ExecutableEdge` records from the pinned `WorkflowExecutable`, using completed activity outcome names as source ports.
- **FR-004**: Downstream `ScheduleActivity` work MUST pin the same executable artifact snapshot as the completion work.
- **FR-005**: Downstream scheduled activity payloads MUST use a new durable `ActivityExecutionId` and MUST record the completed activity execution as the scheduler.
- **FR-006**: Checkpoint command payloads MAY carry post-commit intents; the checkpoint handler MUST copy those intents into the `RuntimeCheckpointCommit`.
- **FR-007**: The default runtime composition MUST dispatch scheduler-work post-commit intents through the scheduler work queue after the checkpoint writer succeeds.
- **FR-008**: This slice MUST NOT implement workflow completion, joins, branch merge policy, durable providers, outbox delivery state, retry policy, bookmark behavior, or activity invocation providers.
- **FR-009**: Runtime execution projects MUST remain free of Design-owned authored workflow model dependencies.

## Non-Goals

- Workflow completion when no outgoing edges match.
- Join or branch synchronization semantics.
- Durable outbox storage.
- Durable checkpoint storage providers.
- Activity body invocation or provider selection.

## Acceptance Criteria

- Continuation scheduling includes post-commit scheduler intents for matching executable edges.
- Checkpoint commit dispatch enqueues scheduler work only after successful checkpoint write.
- Failed checkpoint writes do not enqueue downstream scheduler work.
- Focused runtime and architecture tests pass.
