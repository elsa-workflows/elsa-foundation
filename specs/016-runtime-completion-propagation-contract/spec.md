# Feature Specification: Runtime Completion Propagation Contract

**Feature Branch**: `codex/runtime-completion-propagation-contract`
**Created**: 2026-06-10
**Status**: Draft
**Input**: Locked Runtime Execution Seam addendum decision: activity completion propagation is deterministic scheduler work, not immediate recursive bubbling.

## Scenarios & Tests

1. Given an activity completes, when runtime state records propagation work, then the completion appears in a scheduler completion-drain lane separate from scheduled activity work and volatile continuations.
2. Given a parent activity must evaluate a completed child, when parent completion evaluation work is created, then both parent and completed child activity execution IDs are explicit.
3. Given completion propagation reaches continuation scheduling, when scheduler work is represented, then it is still ordered completion-drain work and not ordinary activity scheduling.
4. Given joins need completed branches before continuation, when completion evaluation work is modeled, then required completed activity execution IDs can be carried as deterministic contract data.

## Requirements

- **FR-001**: Runtime contracts MUST represent activity completion propagation as queued scheduler completion-drain work.
- **FR-002**: Completion-drain work MUST be structurally separate from ordinary scheduled activity work.
- **FR-003**: Completion-drain work MUST be structurally separate from volatile wait/internal continuation work.
- **FR-004**: Parent completion evaluation work MUST identify the parent activity execution and the completed child activity execution.
- **FR-005**: Completion work MUST support deterministic ordering through an explicit sequence.
- **FR-006**: Completion work MUST support branch/join evidence through required completed activity execution IDs.
- **FR-007**: Contract tests MUST prove invalid or ambiguous completion work is rejected.
- **FR-008**: Runtime execution projects MUST remain free of Design-owned authored workflow model dependencies.

## Non-Goals

- Implementing the scheduler drain loop.
- Implementing parent activity behavior, joins, cancellation, compensation, or incident interruption.
- Changing Elsa 3 activity completion handler behavior.
- Introducing a distributed actor provider.
- Implementing checkpoints beyond the existing checkpoint contract.

## Acceptance Criteria

- `SchedulerState` carries `PendingCompletionWork` separately from `PendingWork` and `PendingContinuations`.
- Completion work models the locked flow `ActivityCompleted -> ParentCompletionEvaluation -> ContinuationScheduling`.
- Focused tests cover separation, ordering fields, parent/child identity, join prerequisites, and invalid combinations.
- Focused runtime and architecture tests pass.
