# Feature Specification: Runtime Post-Commit Outbox Recording

**Feature Branch**: `codex/runtime-post-commit-outbox-recording`
**Created**: 2026-06-11
**Status**: Draft
**Input**: Continue the Runtime Execution Seam after adding the in-memory post-commit outbox store. The committer should now record committed post-commit intents into the outbox store before immediate dispatch, without implementing a full processor.

## Scenarios & Tests

1. Given a checkpoint commit with post-commit intents, when checkpoint persistence succeeds, then all intents are saved as pending outbox items before any delivery is attempted.
2. Given post-commit dispatch succeeds, then the outbox item records a delivered result after delivery.
3. Given post-commit dispatch fails, then the failed item records a final failure before the committer reports the dispatch exception.
4. Given checkpoint persistence is skipped or fails, then no outbox item is recorded and no post-commit delivery is attempted.
5. Given pending outbox recording fails after checkpoint persistence succeeds, then immediate dispatch does not start because the recovery record boundary was not established.

## Requirements

- **FR-001**: `RuntimeCheckpointCommitter` MUST save pending outbox items only after `IRuntimeCheckpointWriter` succeeds.
- **FR-002**: The committer MUST save all pending post-commit intents before dispatching any intent.
- **FR-003**: The committer MUST record delivered outbox results after successful intent dispatch.
- **FR-004**: The committer MUST record failed-final outbox results when immediate intent dispatch fails.
- **FR-005**: Outbox item IDs MUST be deterministic for a checkpoint commit and intent.
- **FR-006**: Runtime execution projects MUST remain free of Design-owned authored workflow model dependencies.

## Non-Goals

- Full durable outbox processor.
- Delivery ownership/claiming.
- Retry policy selection.
- Background redelivery.
- Scheduler dispatch replacement.

## Acceptance Criteria

- Tests prove record-commit-deliver-mark-delivered ordering.
- Tests prove failed dispatch records failed-final delivery state.
- Tests prove skipped or failed checkpoint persistence does not record outbox items.
- Tests prove pending outbox recording failure prevents immediate dispatch.
- Focused runtime and architecture tests pass.
