# Tasks: Cancel Waited Dispatches on Subtree Teardown

**Input**: Design documents from `specs/137-dispatch-cancel-on-subtree/`

**Prerequisites**: `plan.md`, `spec.md`, `research.md`, `data-model.md`, `contracts/dispatch-cancel-on-subtree.md`, `quickstart.md`

**Tests**: Required by FR-010 and the constitution's branch-coverage discipline. Write focused tests before implementation and observe the new local-cancellation test fail.

**Organization**: Tasks are grouped by user story. The implementation is one shared enricher; User Story 2 hardens its selection boundary after the P1 lifecycle path is green.

## Phase 1: Setup and Baseline

**Purpose**: Confirm the shipped cancellation baseline and exact edit surface.

- [x] T001 Run the existing cancellation suite in `tests/Elsa/Activities/DispatchWorkflow/Tests/Elsa.Activities.DispatchWorkflow.Tests.csproj` and review `src/Elsa/Activities/DispatchWorkflow/Runtime/Services/WorkflowDispatchCancellationEnricher.cs` for unchanged replay/paging invariants

---

## Phase 2: User Story 1 - Reclaim the Dispatched Child (Priority: P1) 🎯 MVP

**Goal**: Locally cancelling the exact activity owner commits one deterministic child-cancellation responsibility while the parent remains running.

**Independent Test**: A Running parent with an exact cancelled activity owner produces one canonical request/intent; replay and combined whole-parent/local cancellation remain deduplicated.

### Tests for User Story 1

- [x] T002 [US1] Add failing local-owner, replay, and combined-trigger cancellation tests in `tests/Elsa/Activities/DispatchWorkflow/Tests/WorkflowDispatchCancellationTests.cs`

### Implementation for User Story 1

- [x] T003 [US1] Extend trigger and exact-owner selection while preserving existing work construction in `src/Elsa/Activities/DispatchWorkflow/Runtime/Services/WorkflowDispatchCancellationEnricher.cs`
- [x] T004 [US1] Run the focused `WorkflowDispatchCancellationTests` cases from `tests/Elsa/Activities/DispatchWorkflow/Tests/Elsa.Activities.DispatchWorkflow.Tests.csproj`

**Checkpoint**: User Story 1 independently proves the orphaned-child lifecycle leak is closed.

---

## Phase 3: User Story 2 - Preserve Dispatch Isolation (Priority: P2)

**Goal**: Local cancellation affects only exact eligible dispatch owners across all provider pages.

**Independent Test**: Sibling-owner, detached, opted-out, and terminal records produce no new work; a matching late-page record still produces one responsibility.

### Tests for User Story 2

- [x] T005 [US2] Add sibling-owner, mode/policy, terminal, committed-replay, and late-page selection tests in `tests/Elsa/Activities/DispatchWorkflow/Tests/WorkflowDispatchCancellationTests.cs`

### Implementation for User Story 2

- [x] T006 [US2] Refine exact-owner filtering or shared test setup as required without changing dispatch paging/recovery semantics in `src/Elsa/Activities/DispatchWorkflow/Runtime/Services/WorkflowDispatchCancellationEnricher.cs` and `tests/Elsa/Activities/DispatchWorkflow/Tests/WorkflowDispatchCancellationTests.cs`
- [x] T007 [US2] Run the complete cancellation test class from `tests/Elsa/Activities/DispatchWorkflow/Tests/Elsa.Activities.DispatchWorkflow.Tests.csproj`

**Checkpoint**: Both user stories are independently covered and the entire existing cancellation suite remains green.

---

## Phase 4: Validation, Review, and Delivery

**Purpose**: Verify cross-cutting runtime compatibility and synchronize delivery evidence.

- [x] T008 Run every command in `specs/137-dispatch-cancel-on-subtree/quickstart.md`
- [x] T009 Verify task completion, contract coverage, and absence of unresolved placeholders across `specs/137-dispatch-cancel-on-subtree/`
- [x] T010 Run up to five self-review/fix iterations over `src/Elsa/Activities/DispatchWorkflow/Runtime/Services/WorkflowDispatchCancellationEnricher.cs`, `tests/Elsa/Activities/DispatchWorkflow/Tests/WorkflowDispatchCancellationTests.cs`, and `specs/137-dispatch-cancel-on-subtree/`
- [ ] T011 Commit the completed work unit on `codex/998-seam-a-dispatch-cancellation`, push to `origin`, and open a draft PR whose body contains `Closes #998`
- [ ] T012 Add the PR link and validation evidence to GitHub issue #998 and record the in-review state in `docs/program-goals/bpmn-engine.md`

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: Starts immediately.
- **User Story 1 (Phase 2)**: Depends on T001 and supplies the MVP implementation.
- **User Story 2 (Phase 3)**: Depends on T003 because it hardens the same selection logic.
- **Validation and Delivery (Phase 4)**: Depends on both stories.

### User Story Dependencies

- **User Story 1 (P1)**: No dependency beyond baseline verification.
- **User Story 2 (P2)**: Uses the P1 trigger implementation but remains independently verifiable through negative and paging scenarios.

### Parallel Opportunities

- No code tasks are safely parallel because both stories touch the same implementation and test files.
- After implementation, independent validation projects from `quickstart.md` may run in parallel when host resources permit.
- GitHub tracking updates follow the successful commit/push/PR sequence and remain serialized.

---

## Implementation Strategy

### MVP First

1. Run T001.
2. Add and observe the T002 local-owner regression fail.
3. Implement T003 and pass T004.
4. Validate the exact-owner lifecycle outcome before broadening coverage.

### Incremental Hardening

1. Add T005 isolation and paging regressions.
2. Refine only if a new test exposes a gap.
3. Run full quickstart validation and bounded self-review.
4. Commit, push, open the linked PR, and synchronize issue/program-goal evidence.

## Format Validation

All 12 tasks use the required checkbox, sequential task ID, optional story label, concrete action, and exact file or project path.
