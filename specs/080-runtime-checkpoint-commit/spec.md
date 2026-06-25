# Feature Specification: Runtime Checkpoint Commit

**Feature Branch**: `080-runtime-checkpoint-commit`

**Created**: 2026-06-25

**Status**: Draft

**Input**: User description: "Deepen runtime checkpoint commit so it records post-commit work without inline delivery, replaces IRuntimeCheckpointWriter with IRuntimeCheckpointCommitStore, and keeps post-commit delivery in the outbox processor."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Commit runtime state and pending delivery work atomically (Priority: P1)

A runtime maintainer can rely on runtime checkpoint commit as the single durable operation that applies checkpoint state changes and records any post-commit delivery work, without also performing delivery side effects.

**Why this priority**: Runtime continuation must not advance state without preserving the follow-up work needed to continue scheduling, and checkpoint commit tests should verify one deep module interface instead of faking writer, dispatcher, outbox, policy, and clock seams.

**Independent Test**: Can be tested by committing a checkpoint with post-commit intents and confirming that the commit result reports the persistence decision and pending outbox item identities while no delivery dispatcher is invoked.

**Acceptance Scenarios**:

1. **Given** a checkpoint commit with state changes and two post-commit intents, **When** runtime checkpoint commit succeeds, **Then** the related state changes and two pending delivery items are recorded through one commit path and the result lists both pending item identities.
2. **Given** a checkpoint commit with post-commit intents, **When** runtime checkpoint commit succeeds, **Then** no post-commit intent is dispatched inline.
3. **Given** a checkpoint commit with no post-commit intents, **When** runtime checkpoint commit succeeds, **Then** the result reports no pending delivery items and no delivery attempt.

---

### User Story 2 - Deliver recorded post-commit work separately (Priority: P2)

A runtime operator can process previously recorded post-commit work through the post-commit delivery module, with delivery retry and result recording kept outside checkpoint commit.

**Why this priority**: Delivery is a different lifecycle phase than checkpoint commit. Keeping it separate makes retry behavior testable without making checkpoint commit responsible for scheduling side effects.

**Independent Test**: Can be tested by creating deliverable post-commit work and confirming the delivery module queries it, dispatches it, and records delivery outcomes without using checkpoint commit.

**Acceptance Scenarios**:

1. **Given** pending post-commit work exists, **When** post-commit delivery runs, **Then** deliverable work is queried and dispatched by the delivery module.
2. **Given** delivery succeeds, **When** the delivery module records the outcome, **Then** the work is marked delivered.
3. **Given** delivery fails with a retryable failure, **When** the delivery module records the outcome, **Then** retry state is updated without involving runtime checkpoint commit.

---

### User Story 3 - Detect skipped commits that would drop delivery work (Priority: P3)

A runtime maintainer gets a clear failed commit result when checkpoint policy skips persistence for a commit that contains post-commit work, instead of silently dropping the work or dispatching it as a fallback.

**Why this priority**: A skipped checkpoint with delivery work is a policy contradiction. Silent success would make runtime behavior nondeterministic and hard to recover.

**Independent Test**: Can be tested by applying a skip persistence decision to a checkpoint commit that contains post-commit intents and confirming the result has a deterministic failure code and no persisted state, pending work, or delivery attempt.

**Acceptance Scenarios**:

1. **Given** checkpoint policy returns Skip and the commit has no post-commit intents, **When** runtime checkpoint commit runs, **Then** no state or pending work is recorded and the result reports the skip decision.
2. **Given** checkpoint policy returns Skip and the commit has post-commit intents, **When** runtime checkpoint commit runs, **Then** the result fails with `runtime.checkpoint.skip_has_post_commit_work`.
3. **Given** checkpoint policy returns Skip and the commit has post-commit intents, **When** runtime checkpoint commit fails, **Then** no inline dispatch fallback is attempted.

---

### User Story 4 - Remove the shallow writer seam without compatibility layering (Priority: P4)

A runtime maintainer can reason about one provider-facing checkpoint commit adapter instead of preserving the previous writer/outbox split for compatibility.

**Why this priority**: The work intentionally accepts breaking changes to get a clean architecture and implementation. Keeping a compatibility layer would preserve the shallow module under a new name.

**Independent Test**: Can be tested by verifying runtime core no longer exposes or consumes the old checkpoint writer seam and that checkpoint commit storage is represented by the new commit-store seam.

**Acceptance Scenarios**:

1. **Given** runtime checkpoint commit storage is registered, **When** runtime checkpoint commit needs durable persistence, **Then** it uses the provider-facing checkpoint commit store.
2. **Given** post-commit delivery needs deliverable work, **When** delivery runs, **Then** it uses the delivery-facing outbox store only for querying and result recording.
3. **Given** callers or tests search runtime core for the old checkpoint writer seam, **When** the first implementation slice is complete, **Then** no runtime-core production code depends on it.

### Edge Cases

- Checkpoint policy returns Skip for a commit that has post-commit intents.
- Checkpoint commit has zero post-commit intents.
- Provider commit fails before any durable state is persisted.
- Provider commit fails after some durable state may have been persisted but pending post-commit work may not have been recorded.
- Delivery processing succeeds after pending work was recorded by checkpoint commit.
- Delivery processing fails and must record retryable or final failure state.
- Existing tests expect inline dispatch from checkpoint commit.
- Multiple post-commit intents are present in one checkpoint commit.
- Post-commit work identity must remain deterministic for commit replay and idempotency.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Runtime checkpoint commit MUST apply checkpoint persistence policy before attempting durable checkpoint storage.
- **FR-002**: Runtime checkpoint commit MUST persist checkpoint state changes and pending post-commit work through one provider-facing commit path for non-skipped commits.
- **FR-003**: Runtime checkpoint commit MUST NOT dispatch post-commit intents inline.
- **FR-004**: Runtime checkpoint commit MUST return a result that includes the persistence decision, commit identity, workflow execution identity, and pending post-commit work identities recorded by the commit.
- **FR-005**: Runtime checkpoint commit MUST report a successful skip result without recording state or pending work when policy returns Skip and the commit has no post-commit intents.
- **FR-006**: Runtime checkpoint commit MUST report failed result code `runtime.checkpoint.skip_has_post_commit_work` when policy returns Skip and the commit has post-commit intents.
- **FR-007**: Expected policy contradictions MUST be represented as failed commit results rather than exceptions.
- **FR-008**: Provider commit failures MUST remain exceptional infrastructure failures.
- **FR-009**: Failures that may leave checkpoint state persisted without pending post-commit work MUST be represented as explicit inconsistent-durability failures.
- **FR-010**: Runtime core MUST replace the old checkpoint writer seam with `IRuntimeCheckpointCommitStore`.
- **FR-011**: `IRuntimeCheckpointCommitStore` MUST represent storage of a full runtime checkpoint commit, including checkpoint state changes and pending post-commit work.
- **FR-012**: Runtime core MUST NOT preserve the old checkpoint writer seam as a compatibility layer.
- **FR-013**: Post-commit delivery MUST own post-commit intent dispatch, retry classification, delivery result recording, and dispatcher adapters.
- **FR-014**: Runtime checkpoint commit MUST NOT depend on the post-commit intent dispatcher.
- **FR-015**: The delivery-facing outbox store MUST support querying deliverable work and recording delivery outcomes.
- **FR-016**: The delivery-facing outbox store MUST NOT expose pending post-commit work creation.
- **FR-017**: Runtime checkpoint commit tests MUST focus on commit policy, provider commit behavior, pending work recording, skip contradictions, and failure classification.
- **FR-018**: Post-commit delivery tests MUST cover dispatch success, dispatch failure, retryable/final delivery outcomes, and delivery result recording.
- **FR-019**: The first implementation slice MUST be limited to runtime core behavior and tests unless current compile or test obligations require a narrow adjacent update.
- **FR-020**: Provider-specific durable persistence implementations are out of scope for the first slice unless needed to keep existing runtime-core tests meaningful.

### Key Entities *(include if feature involves data)*

- **Runtime Checkpoint Commit**: The atomic runtime operation that applies a named checkpoint's state changes and records any post-commit delivery work.
- **Checkpoint Commit Result**: Outcome returned by runtime checkpoint commit, including persistence decision, commit identity, workflow execution identity, pending work identities, and expected failure codes.
- **Runtime Checkpoint Commit Store**: Provider-facing adapter that stores a full runtime checkpoint commit, including state changes and pending post-commit work.
- **Post-Commit Work**: Durable delivery work produced by runtime checkpoint commit and delivered later by the post-commit delivery module.
- **Post-Commit Delivery Module**: Runtime module that queries deliverable post-commit work, dispatches it, and records delivery outcomes.
- **Delivery-Facing Outbox Store**: Store surface used by post-commit delivery for deliverable-work queries and delivery result recording.
- **Inconsistent-Durability Failure**: Infrastructure failure state where checkpoint state may have advanced without corresponding pending post-commit work being durably recorded.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A checkpoint commit with two post-commit intents returns a successful result listing two pending post-commit work identities and records zero inline dispatch attempts.
- **SC-002**: A skipped checkpoint commit with post-commit intents returns failed result code `runtime.checkpoint.skip_has_post_commit_work` and records zero state changes, zero pending work items, and zero inline dispatch attempts.
- **SC-003**: Runtime-core production code has no remaining dependency on `IRuntimeCheckpointWriter`.
- **SC-004**: The delivery-facing outbox store exposes no operation for creating pending post-commit work.
- **SC-005**: Runtime checkpoint commit tests no longer need a fake post-commit dispatcher to verify commit behavior.
- **SC-006**: Post-commit delivery tests still verify successful delivery, retryable failure recording, final failure recording, and delivery cancellation behavior.
- **SC-007**: The first implementation slice can be verified by runtime-core unit tests without requiring provider-specific persistence changes.

## Assumptions

- Breaking runtime checkpoint adapter changes are acceptable for this work unit.
- The accepted decision source is [ADR 0020](../../docs/adr/0020-runtime-checkpoint-commit-post-commit-work.md).
- The canonical domain term is `Runtime checkpoint commit` in [docs/glossary/elsa.md](../../docs/glossary/elsa.md).
- The work belongs to the [Runtime Execution Seam](../../docs/program-goals/runtime-execution-seam.md) program-goal bucket.
- Runtime checkpoint commit remains a runtime-core concern in the first slice.
- Post-commit delivery remains responsible for dispatcher adapters and retry behavior.
