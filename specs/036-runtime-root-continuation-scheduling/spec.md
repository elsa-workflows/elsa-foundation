# Feature Specification: Runtime Root Continuation Scheduling

**Feature Branch**: `codex/runtime-root-continuation-scheduling`
**Created**: 2026-06-11
**Status**: Draft
**Input**: Continue deterministic activity completion propagation after downstream scheduling exists.

This slice intentionally supersedes the earlier slice-032 deferral where root activity completion still stopped before continuation scheduling. Child completion with a parent still follows the parent-evaluation path first.

## Scenarios & Tests

1. Given `ActivityCompleted` work for a root activity with no parent, when Workflows Runtime handles it, then it enqueues deterministic `ContinuationScheduling` work for that same activity execution instead of returning.
2. Given root continuation scheduling reaches the existing downstream scheduling step, when executable outgoing edges match the completed activity outcomes, then downstream `ScheduleActivity` post-commit intents are created after the activity-completed checkpoint.
3. Given root activity completion has no parent, when inspected, then no `ParentCompletionEvaluation` work is created.

## Requirements

- **FR-001**: Root activity completion MUST enter continuation scheduling directly.
- **FR-002**: Root activity completion MUST NOT enqueue parent-completion-evaluation work.
- **FR-003**: Root continuation-scheduling work MUST preserve pinned executable identity, executable node ID, activity execution ID, branch ID, and outcome names from the completed root activity.
- **FR-004**: Root continuation-scheduling work MUST be deterministically named from the completed activity work item and subject activity execution ID.
- **FR-005**: Existing child completion behavior MUST remain unchanged: child completion with a parent still enqueues parent-completion-evaluation work.
- **FR-006**: This slice MUST NOT implement workflow completion, joins, branch merge policy, durable providers, bookmark behavior, outbox delivery state, retry policy, or activity invocation providers.
- **FR-007**: Runtime execution projects MUST remain free of Design-owned authored workflow model dependencies.

## Non-Goals

- Workflow completion when terminal root continuation has no matching outgoing edge.
- Join or branch synchronization semantics.
- Durable checkpoint or scheduler providers.
- Activity body invocation or provider selection.

## Acceptance Criteria

- Root activity completion queues continuation scheduling work directly.
- Root completion still creates no parent-evaluation work.
- Root continuation scheduling can produce downstream post-commit scheduler intents through the existing pinned executable edge traversal.
- Focused runtime and architecture tests pass.
