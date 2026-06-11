# Runtime Scheduler Command Drain Dispatch

**Feature Branch**: `codex/runtime-scheduler-handler-dispatch`
**Created**: 2026-06-11
**Input**: Runtime Execution Seam next slice after scheduler drain contract.

## User Scenarios & Testing

1. Given a workflow execution agent accepts a command, when the default command processor records scheduler work, then it drains scheduler work for that same workflow execution through the scheduler drain boundary.
2. Given a host needs to defer drain behavior, when it replaces the drain policy, then command processing can record scheduler work without running the drain boundary.
3. Given a drain produces item results, when command processing completes, then observers can inspect those results without making history or diagnostics continuation state.
4. Given a handler fault is represented by the drain result, when command processing returns, then mailbox acceptance remains separate from domain/runtime work outcome.

## Requirements

- **FR-001**: Runtime.Core MUST expose a scheduler drain policy contract used by command processing after scheduler work is recorded.
- **FR-002**: Runtime.Core MUST provide a default immediate drain policy for accepted scheduler commands.
- **FR-003**: Runtime.Core MUST expose a scheduler drain observer contract for observing drain results without adding continuation-state history.
- **FR-004**: `WorkflowSchedulerCommandProcessor` MUST enqueue a `RuntimeSchedulerWorkItem` before invoking the drain boundary.
- **FR-005**: The default Runtime API composition MUST use the drain-capable command processor path.
- **FR-006**: Command dispatch acceptance MUST remain a mailbox concern; drain item faults MUST be visible in drain results/observers without being reported as command dispatch rejection.
- **FR-007**: This slice MUST NOT execute activities, evaluate bindings, write checkpoints, process bookmarks, or implement durable retry.

## Success Criteria

- Runtime tests prove the default command processor enqueues before draining the same workflow execution.
- Runtime tests prove a replacement policy can skip draining.
- Runtime tests prove drain results are delivered to observers.
- Runtime tests prove in-process agent command acceptance can drain queued work without depending on Design-owned authored workflow models.
