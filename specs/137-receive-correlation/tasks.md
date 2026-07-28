# Tasks: Receive Event Correlation

**Input**: Design artifacts from [`specs/137-receive-correlation/`](.)
**Prerequisites**: [`plan.md`](plan.md), [`spec.md`](spec.md), [`research.md`](research.md), [`data-model.md`](data-model.md), and [`quickstart.md`](quickstart.md)

**Tests**: Required. The implementation workflow is TDD: add the focused Event-registration tests and observe their deliberate failure before changing production code. Existing lookup/router tests are regression evidence for the unchanged generic reader and start paths.

**Scope guard**: Only `Event` wait-registration metadata changes. Do not modify the router, bookmark lookup, persistence pipeline, workflow-start selection, BPMN authoring/import/export, or non-Event wait types.

## Phase 1: Setup

**Purpose**: Establish the feature's focused baseline and use the documented validation commands.

- [x] T001 Run the focused baseline commands in `specs/137-receive-correlation/quickstart.md` for `EventTriggerStimulusProviderTests`, `GlobalBookmarkStimulusLookupTests`, and `StimulusRouterTests`; record any pre-existing failure before changing `src/Elsa/Activities/Primitives/Activities/Event.cs`.

---

## Phase 2: Foundational

**Purpose**: No shared production scaffolding is needed. The existing immutable registration-metadata channel, bookmark metadata propagation, and exact-match lookup are the foundation and must remain unchanged.

**Checkpoint**: Proceed only with focused Event tests; do not add a new contract, bookmark field, index, or pipeline service.

---

## Phase 3: User Story 1 - Resume the Correct Correlated Event Wait (Priority: P1) 🎯 MVP

**Goal**: A nonblank authored `Event.CorrelationId` is retained on the Event wait registration so the existing correlated resume lookup can select matching waits.

**Independent Test**: Execute an Event as a wait with `CorrelationId = " order-7 "` and assert the single registration contains `RuntimeMetadataKeys.CorrelationId = "order-7"`; the existing lookup/router suites then demonstrate equal-value fan-in and unequal-value exclusion.

### Tests for User Story 1 (write first and observe failure)

- [x] T002 [US1] Add focused failing suspension tests for a nonblank and a surrounding-whitespace `Event.CorrelationId` in `tests/Elsa/Activities/Runtime/Tests/EventTriggerStimulusProviderTests.cs`; assert the generated `ActivityTriggerRegistration<EventReceived>.Metadata` contains only the trimmed `RuntimeMetadataKeys.CorrelationId` value needed for correlated resume.
- [x] T003 [US1] Run the focused Event test filter from `specs/137-receive-correlation/quickstart.md` and confirm the new metadata-retention assertions fail before production changes in `src/Elsa/Activities/Primitives/Activities/Event.cs`.

### Implementation for User Story 1

- [x] T004 [US1] In `src/Elsa/Activities/Primitives/Activities/Event.cs`, normalize a wait-side `CorrelationId` as null when blank and otherwise `Trim()` it, and pass an immutable metadata map containing `RuntimeMetadataKeys.CorrelationId` only when the normalized value exists to the existing `ActivityTriggerRegistration<EventReceived>`; leave start completion, trigger-provider scope, and routing untouched.
- [x] T005 [US1] Re-run the focused Event filter specified in `specs/137-receive-correlation/quickstart.md` and confirm all new and existing Event activity assertions pass.
- [x] T006 [US1] Add an end-to-end acceptance test in `tests/Elsa/Activities/Runtime/Tests/EventCorrelationRoutingTests.cs` that creates two real Event waits with the same name and distinct correlations through `WorkflowExecutionHarness`, reads their persisted bookmarks, routes one correlation through the real `StimulusRouter`/global lookup with execution-dispatch adapters, and proves only the matching workflow resumes while the other remains waiting.
- [x] T007 [US1] Run the new `EventCorrelationRoutingTests` filter and confirm the actual Event-registration → durable-bookmark → global-lookup → router/resume path passes.

**Checkpoint**: A correlated Event wait now emits the existing metadata key; no runtime reader, router, storage model, or schema has changed.

---

## Phase 4: User Story 2 - Preserve Unscoped Event Delivery (Priority: P2)

**Goal**: Null, empty, and whitespace-only Event correlation input remains unscoped, preserving broadcast compatibility.

**Independent Test**: Execute Event waits with null, empty, and whitespace-only correlation inputs and verify each registration omits the reserved correlation key; verify the existing lookup continues to broadcast an unscoped delivery and excludes an uncorrelated bookmark from a correlated delivery.

### Tests for User Story 2

- [x] T008 [US2] Add Event-suspension regression cases for null, empty, and whitespace-only `CorrelationId` values in `tests/Elsa/Activities/Runtime/Tests/EventTriggerStimulusProviderTests.cs`; assert each registration omits `RuntimeMetadataKeys.CorrelationId` rather than retaining a blank or whitespace value.
- [x] T009 [US2] Run the focused Event and global-lookup filters documented in `specs/137-receive-correlation/quickstart.md`; verify the new unscoped-registration assertions and the existing unscoped-broadcast/correlated-exclusion lookup assertions pass without modifying `src/Elsa/Workflows/Runtime/Services/GlobalBookmarkStimulusLookup.cs`.

**Checkpoint**: Unscoped Events retain their prior broadcast behavior, and pre-existing bookmarks without the metadata key remain compatible.

---

## Phase 5: User Story 3 - Keep Correlation Scope Limited to Resumes (Priority: P3)

**Goal**: The new Event wait metadata must not alter published-trigger start fan-out.

**Independent Test**: Deliver a correlated named event against a published Event trigger binding whose existing `CorrelationScope` differs from the delivery value, then verify that the binding still starts while resume selection remains the separate correlation-scoped concern.

### Tests for User Story 3

- [x] T010 [US3] Add a start-fan-out regression test with a non-null, deliberately nonmatching `WorkflowTriggerBinding.CorrelationScope` in `tests/Elsa/Workflows/Runtime/Tests/StimulusRouterTests.cs`; assert a correlated `StimulusDispatchRequest` still starts the type/hash-matched binding and do not change `src/Elsa/Workflows/Runtime/Services/StimulusRouter.cs`.
- [x] T011 [US3] Run the `StimulusRouterTests` filter in `specs/137-receive-correlation/quickstart.md` and confirm the existing correlated-resume fan-in and the new non-filtered-start assertion both pass.

**Checkpoint**: Correlation narrows only already-waiting Event resumes; published trigger candidates retain existing type/hash fan-out.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Align durable documentation and program tracking with the delivered narrow behavior, then run the affected project suites.

- [x] T012 Update receive/send correlation wording in `src/Elsa/Activities/Primitives/README.md`, `src/Elsa/Activities/Primitives/Activities/PublishEvent.cs`, and `src/Elsa/Activities/Primitives/Services/PublishStimulusExecutor.cs` to state that nonblank `Event.CorrelationId` is retained on Event waits for resume narrowing, null/blank remains broadcast, and correlated delivery does not change start fan-out or add BPMN correlation authoring.
- [x] T013 Update the #1001 follow-up state in `docs/program-goals/bpmn-engine.md` only after the implementation and validations pass: mark receive-side Event correlation stamping delivered while leaving BPMN-specific correlation authoring as a separate stated cut.
- [x] T014 Run both full affected-project suites from `specs/137-receive-correlation/quickstart.md` — `Elsa.Activities.Runtime.Tests.csproj` and `Elsa.Workflows.Runtime.Tests.csproj` — and resolve any regression without expanding the feature beyond `src/Elsa/Activities/Primitives/Activities/Event.cs`.
- [x] T015 Review `git diff --check` and the acceptance matrix in `specs/137-receive-correlation/quickstart.md`; verify the only production behavior change is receive-side Event registration metadata and that no generated maps, schema migration, or persistence index update is needed.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: establishes baseline evidence before edits.
- **Foundational (Phase 2)**: is an explicit no-new-infrastructure gate.
- **US1 (Phase 3)**: provides the MVP and must complete before release validation.
- **US2 (Phase 4)**: depends on US1's registration behavior and proves compatible absence semantics.
- **US3 (Phase 5)**: is a regression guard independent of the Event source change, but must pass before completion.
- **Polish (Phase 6)**: depends on all implementation and focused test work.

### User Story Dependencies

- **US1 (P1)**: no feature dependency; delivers the only production code change.
- **US2 (P2)**: depends on US1 because it validates the null/blank branch of the same registration construction.
- **US3 (P3)**: can be prepared after the baseline and has no source dependency, but completes with the final regression suite.

### Within Each User Story

1. Add or extend the focused test first.
2. For US1, run it and observe the expected failing assertion before changing production code.
3. Make the smallest source change in `Event.cs` only.
4. Re-run the story's focused tests before advancing.

### Parallel Opportunities

No implementation tasks are marked `[P]`: US1 and US2 intentionally touch the same focused Event test file and the same single production file, so serial TDD minimizes merge and semantic risk. T010 may be prepared independently in `tests/Elsa/Workflows/Runtime/Tests/StimulusRouterTests.cs` after T001, but it must not alter the implementation scope.

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete T001–T007.
2. Confirm a nonblank Event wait produces the existing reserved metadata key and all focused Event tests pass.
3. Validate the existing lookup/router tests before considering documentation or program-goal state updates.

### Incremental Delivery

1. Deliver US1's opt-in registration metadata.
2. Add US2's absence/broadcast compatibility tests.
3. Add US3's explicit start-fan-out regression guard.
4. Update docs and the BPMN-engine program goal only after both affected project suites pass.

## Notes

- Every task follows the required checklist format with an ID, applicable user-story label, and exact file path.
- `RuntimeMetadataKeys.CorrelationId`, the metadata propagation pipeline, lookup predicate, and router are existing contracts to reuse, not targets for redesign.
- Existing persisted uncorrelated bookmarks are intentionally not backfilled: they resume on unscoped delivery and remain excluded from correlated delivery.
