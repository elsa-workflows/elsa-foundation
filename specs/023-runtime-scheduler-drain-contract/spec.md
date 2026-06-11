# Feature Specification: Runtime Scheduler Drain Contract

**Feature Branch**: `codex/runtime-scheduler-drain-contract`
**Created**: 2026-06-11
**Status**: Draft
**Input**: Runtime Execution Seam next slice after scheduler work queue.

## Scenarios & Tests

1. Given scheduler work is queued for a workflow execution, when the drain service runs, then it dispatches work in FIFO order to a matching handler.
2. Given a drain limit, when more work exists than the limit, then only that number of work items is dispatched.
3. Given a handler faults, when the drain service records the result, then the drain stops and reports the fault without continuing unrelated work.
4. Given no custom handler is registered, when the default handler runs, then the work item is acknowledged without executing workflow/activity behavior.

## Requirements

- **FR-001**: Runtime.Core MUST expose a scheduler drain service contract keyed by `WorkflowExecutionId`.
- **FR-002**: Runtime.Core MUST expose a scheduler work handler contract for dispatching drained `RuntimeSchedulerWorkItem`s.
- **FR-003**: Runtime.Core MUST provide a default drain service that reads from `IWorkflowSchedulerWorkQueue`.
- **FR-004**: Runtime.Core MUST provide a default no-op scheduler work handler so recorded work can be drained in the current contract-only runtime.
- **FR-005**: Drain results MUST include workflow execution ID, drained count, per-item status, command kind, handler name, start time, completion time, and error text when present.
- **FR-006**: The drain service MUST stop on handler faults by default.
- **FR-007**: This slice MUST NOT execute activities, evaluate input bindings, write checkpoints, process bookmarks, or implement durable retry.
- **FR-008**: Runtime.Core MUST remain free of Design-owned authored workflow model dependencies.

## Acceptance Criteria

- Runtime tests prove FIFO drain ordering.
- Runtime tests prove drain limits.
- Runtime tests prove handler fault stops the drain and returns fault details.
- Runtime tests prove runtime feature registration resolves the drain service and default handler.
- Architecture/runtime dependency checks remain Design-free and actor-framework-free.
