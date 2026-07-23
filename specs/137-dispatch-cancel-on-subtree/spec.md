# Feature Specification: Cancel Waited Dispatches on Subtree Teardown

**Feature Branch**: `codex/998-seam-a-dispatch-cancellation`

**Created**: 2026-07-23

**Status**: Draft

**Input**: GitHub issue #998: seam-A subtree teardown cancels the parent-side wait for a waited dispatched workflow but leaves the child workflow instance running.

## User Scenarios & Testing

### User Story 1 - Reclaim the Dispatched Child (Priority: P1)

As a workflow operator, I need a waited child workflow to be cancelled when its owning activity subtree is cancelled so that interrupted call activities do not leave orphaned child executions consuming resources.

**Why this priority**: This closes the lifecycle leak reported by BPMN call activity users while preserving the already-correct parent routing behavior.

**Independent Test**: Cancel the activity execution that owns an active waited dispatch while the parent workflow remains running, then verify that exactly one child-cancellation responsibility is committed for the matching child.

**Acceptance Scenarios**:

1. **Given** a running parent workflow with an active waited dispatch whose propagation policy is enabled, **When** the owning activity execution is cancelled by subtree teardown, **Then** the matching child workflow receives a deterministic cancellation request.
2. **Given** the same cancellation checkpoint is replayed, **When** the checkpoint is enriched again, **Then** the committed cancellation responsibility remains equivalent and no duplicate child cancellation is created.
3. **Given** both the parent workflow and the owning activity execution are cancelled in one checkpoint, **When** cancellation responsibilities are derived, **Then** exactly one equivalent request is committed for the child.

---

### User Story 2 - Preserve Dispatch Isolation (Priority: P2)

As a workflow author, I need local subtree cancellation to affect only the dispatch owned by the cancelled activity so that unrelated, detached, opted-out, or already-terminal dispatches preserve their authored lifecycle.

**Why this priority**: Broad cancellation would turn a targeted lifecycle fix into a behavioral regression for other dispatch modes and activities.

**Independent Test**: Commit local cancellation state for one activity while the same parent owns multiple dispatches, then verify that only the eligible waited dispatch owned by that activity produces cancellation work.

**Acceptance Scenarios**:

1. **Given** multiple dispatches under one running parent, **When** one owning activity is cancelled, **Then** dispatches owned by other activities are unchanged.
2. **Given** a fire-and-forget dispatch or a waited dispatch with propagation disabled, **When** its owning activity is cancelled, **Then** no child cancellation is created.
3. **Given** a terminal dispatch, **When** its owning activity is cancelled, **Then** no new child cancellation is created unless an equivalent committed responsibility must be replayed.
4. **Given** enough parent dispatches to require multiple result pages, **When** a matching owner appears on a later page, **Then** the matching dispatch is still cancelled.

### Edge Cases

- A cancellation state change for a different activity must not cancel the target dispatch.
- Duplicate or conflicting cancellation work must retain the existing deterministic conflict checks.
- Provider paging must continue until exhausted even when early pages contain no matching activity.
- A child that becomes terminal after enrichment must retain the existing committed-outbox replay behavior.
- Local activity cancellation remains effective when the whole parent workflow stays Running.

## Requirements

### Functional Requirements

- **FR-001**: The runtime MUST derive child-cancellation work when a checkpoint cancels the activity execution that owns an eligible waited workflow dispatch.
- **FR-002**: Local activity cancellation MUST match dispatches by exact parent activity-execution identity.
- **FR-003**: Whole-parent cancellation MUST retain its existing behavior for every eligible waited dispatch owned by that parent.
- **FR-004**: A checkpoint containing both whole-parent and matching local activity cancellation MUST produce exactly one equivalent cancellation request and one equivalent delivery responsibility per dispatch.
- **FR-005**: Fire-and-forget dispatches and waited dispatches with cancellation propagation disabled MUST remain unaffected.
- **FR-006**: Terminal dispatches MUST remain unaffected unless replay must recover an already-committed equivalent cancellation responsibility.
- **FR-007**: Cancellation request identity, delivery identity, ordering, timestamps, equivalence checks, conflict detection, retry behavior, and terminal convergence MUST remain deterministic and replay-safe.
- **FR-008**: Dispatch discovery MUST inspect every result page and MUST preserve the existing non-advancing-cursor rejection behavior.
- **FR-009**: The change MUST use the existing checkpoint enrichment, dispatch cancellation, and post-commit delivery contracts without adding a BPMN-specific runtime side channel or changing persisted schemas.
- **FR-010**: Existing whole-parent cancellation, dispatch lifecycle, subtree cancellation, and BPMN teardown tests MUST continue to pass.

### Key Entities

- **Activity execution cancellation**: The committed terminal state identifying the exact locally cancelled activity execution.
- **Workflow dispatch record**: The durable parent-activity-to-child-workflow lifecycle record, including mode, propagation policy, and terminal status.
- **Child cancellation responsibility**: The deterministic state change and post-commit delivery work that request cancellation of the dispatched child.

## Success Criteria

### Measurable Outcomes

- **SC-001**: Every eligible waited dispatch owned by a locally cancelled activity produces exactly one logical child-cancellation responsibility across initial execution and replay.
- **SC-002**: Zero cancellation responsibilities are produced for unrelated activity owners, detached dispatches, opted-out waited dispatches, or terminal dispatches without prior committed work.
- **SC-003**: A matching dispatch is found and cancelled regardless of which provider result page contains it.
- **SC-004**: All focused DispatchWorkflow cancellation tests and relevant Runtime/BPMN regression projects complete with zero failures.
- **SC-005**: No public contract, persisted schema, package dependency, or BPMN engine behavior changes beyond closing the orphaned-child lifecycle leak.

## Assumptions

- `ActivityExecutionStatus.Cancelled` is the runtime's authoritative signal that the local activity execution no longer owns live work; there is no separate intentional-detach cancellation status today.
- Existing `CancelChildOnParentCancellation` policy semantics apply equally when the owning activity is cancelled locally.
- Seam-A teardown already persists the cancelled activity-execution state in the same atomic checkpoint that is passed through checkpoint enrichers.
- Late child resume delivery remains safely absorbed by existing terminal-parent and cancelled-activity guards.
- The work belongs to the existing `bpmn-engine` program-goal bucket and resolves the tracked #998 follow-up without broadening the separate `CancelLiveWork`-through-seam-A effort.
