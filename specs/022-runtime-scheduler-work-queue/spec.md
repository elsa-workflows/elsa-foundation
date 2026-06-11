# Feature Specification: Runtime Scheduler Work Queue

**Feature Branch**: `codex/runtime-scheduler-work-queue`
**Created**: 2026-06-11
**Status**: Draft
**Input**: Runtime Execution Seam next slice after in-process execution agent provider.

## Scenarios & Tests

1. Given an accepted workflow execution command, when the command processor runs inside an agent mailbox, then a scheduler work item is recorded for that workflow execution.
2. Given multiple commands for the same workflow execution, when they are processed, then scheduler work preserves enqueue order.
3. Given duplicate scheduler work item IDs, when they are enqueued directly into the queue, then the queue remains idempotent.
4. Given one workflow has queued work, when another workflow is queried or dequeued, then the queue keeps workflow execution boundaries separate.

## Requirements

- **FR-001**: Runtime.Core MUST expose a scheduler work queue contract that stores scheduler work by `WorkflowExecutionId`.
- **FR-002**: Runtime.Core MUST provide an in-memory scheduler work queue default for the current single-node runtime.
- **FR-003**: Runtime.Core MUST provide an `IWorkflowExecutionCommandProcessor` default that converts accepted command envelopes into scheduler work items.
- **FR-004**: Scheduler work items MUST carry workflow execution ID, command ID, command kind, envelope ID, idempotency key, enqueue time, optional sequence, payload, command metadata, and envelope metadata.
- **FR-005**: The queue MUST preserve per-workflow insertion order for listed and dequeued work.
- **FR-006**: The queue MUST be idempotent by scheduler work item ID within each workflow execution.
- **FR-007**: This slice MUST NOT implement activity execution, scheduler drain behavior, durable persistence, distributed queue placement, or checkpoint commits.
- **FR-008**: Runtime.Core MUST remain free of Design-owned authored workflow model dependencies.

## Acceptance Criteria

- Runtime tests prove accepted agent commands are queued as scheduler work.
- Runtime tests prove queue ordering and idempotency.
- Runtime tests prove workflow execution queue isolation.
- Runtime feature registration resolves the scheduler queue and queuing command processor by default.
- Architecture/runtime dependency checks remain Design-free and actor-framework-free.
