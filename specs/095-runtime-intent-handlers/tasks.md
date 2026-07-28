# Tasks: Contributed Runtime Intent Handlers

**Input**: Design documents from `specs/095-runtime-intent-handlers/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Tests**: Required by issue #675 and the feature specification. Write focused tests before each implementation slice.

**Organization**: Tasks are grouped by user story and ordered so the contribution seam lands before scheduler compatibility and unsupported-kind recovery validation.

## Phase 1: Setup and Baseline

**Purpose**: Establish the current scheduler/outbox/resumption baseline before changing the delivery seam.

- [x] T001 Run and record the focused baseline for scheduler post-commit, outbox, DI composition, and resumption behavior using `tests/Elsa/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj`

---

## Phase 2: Foundational Failing Tests

**Purpose**: Lock the public contribution and end-to-end delivery requirements before implementation.

- [x] T002 [P] Add failing idempotent-registration and deterministic-conflict tests in `tests/Elsa/Workflows/Runtime/Tests/RuntimePostCommitIntentContributionTests.cs`
- [x] T003 [P] Add failing composite dispatch and unsupported-kind tests in `tests/Elsa/Workflows/Runtime/Tests/RuntimePostCommitIntentDispatcherTests.cs`
- [x] T004 [P] Add a failing checkpoint → outbox → global-resumption marker guardrail in `tests/Elsa/Workflows/Runtime/Tests/RuntimeResumptionServiceTests.cs`

**Checkpoint**: All new #675 tests fail for the expected missing contribution/dispatch behavior.

---

## Phase 3: User Story 1 - Contribute a runtime intent handler (Priority: P1) 🎯 MVP

**Goal**: Modules register keyed handlers through one mechanism and the global pump invokes the selected handler once outside actor mailboxes.

**Independent Test**: The marker handler is committed through a real checkpoint/outbox and invoked exactly once by a global resumption sweep.

- [x] T005 [P] [US1] Define `IRuntimePostCommitIntentHandler` in `src/Elsa/Workflows/Runtime/Core/Contracts/IRuntimePostCommitIntentHandler.cs`
- [x] T006 [P] [US1] Define validated keyed contribution metadata in `src/Elsa/Workflows/Runtime/Core/Models/RuntimePostCommitIntentHandlerContribution.cs`
- [x] T007 [US1] Implement idempotent/conflict-safe `AddRuntimePostCommitIntentHandler<THandler>` in `src/Elsa/Workflows/Runtime/Extensions/RuntimePostCommitIntentHandlerServiceCollectionExtensions.cs`
- [x] T008 [US1] Implement the ordinal keyed aggregate dispatcher in `src/Elsa/Workflows/Runtime/Services/RuntimePostCommitIntentDispatcher.cs`
- [x] T009 [US1] Register the aggregate as the default dispatcher in `src/Elsa/Workflows/Runtime/Extensions/RuntimeCoreServiceCollectionExtensions.cs`
- [x] T010 [US1] Remove only the global intent-kind filter while retaining the local scheduler-only filter in `src/Elsa/Workflows/Runtime/Services/RuntimeResumptionService.cs`
- [x] T011 [US1] Run the US1 contribution and marker guardrail tests in `tests/Elsa/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj`

**Checkpoint**: Contributed marker work completes through the global runtime path with one invocation.

---

## Phase 4: User Story 2 - Preserve scheduler post-commit delivery (Priority: P1)

**Goal**: Scheduler work uses the contribution mechanism without changing identifiers, validation, queueing, or per-execution delivery.

**Independent Test**: Existing scheduler intent tests and an explicit before/after work-item parity assertion pass.

- [x] T012 [P] [US2] Add scheduler contribution and persisted work-item parity assertions in `tests/Elsa/Workflows/Runtime/Tests/RuntimeDownstreamSchedulingTests.cs`
- [x] T013 [US2] Adapt `RuntimeSchedulerPostCommitIntentDispatcher` to the handler contract without changing its dispatch body in `src/Elsa/Workflows/Runtime/Services/RuntimeSchedulerPostCommitIntentDispatcher.cs`
- [x] T014 [US2] Register scheduler delivery through `AddRuntimePostCommitIntentHandler` in `src/Elsa/Workflows/Runtime/Extensions/RuntimeCoreServiceCollectionExtensions.cs`
- [x] T015 [US2] Verify the per-execution scheduler filter remains unchanged in `src/Elsa/Workflows/Runtime/Services/WorkflowDrainOrchestrator.cs`
- [x] T016 [US2] Run scheduler, checkpoint, outbox, command-drain, and resumption regressions in `tests/Elsa/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj`

**Checkpoint**: Existing scheduler behavior is unchanged and built-ins use no privileged registration path.

---

## Phase 5: User Story 3 - Fail unsupported intent kinds safely (Priority: P2)

**Goal**: Unknown kinds and handler failures remain visible through the existing policy-selected safe outbox failure path and are never silently acknowledged.

**Independent Test**: A committed unknown kind is processed by the real outbox processor and persists the provider-normalized safe failure state without delivery.

- [x] T017 [P] [US3] Add outbox-level unsupported-kind and safe-diagnostic assertions in `tests/Elsa/Workflows/Runtime/Tests/RuntimePostCommitOutboxProcessorTests.cs`
- [x] T018 [US3] Harden deterministic validation/error context in `src/Elsa/Workflows/Runtime/Services/RuntimePostCommitIntentDispatcher.cs`
- [x] T019 [US3] Run unsupported-kind and handler-failure tests in `tests/Elsa/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj`

**Checkpoint**: Unsupported committed work remains observable and unacknowledged.

---

## Phase 6: Documentation and Cross-Cutting QA

- [x] T020 [P] Document the contribution kind, registration, conflicts, lifetime, and failure behavior in `src/Elsa/Workflows/Runtime/EXTENSION_POINTS.md`
- [x] T021 [P] Update global-versus-local post-commit delivery wording in `docs/runtime-durable-resumption.md`
- [x] T022 Run the complete Runtime and Runtime.Resumption test projects from `specs/095-runtime-intent-handlers/quickstart.md`
- [x] T023 Run architecture tests and audit the #675 diff for broker, Studio, Design, and WorkflowDefinitionActivity dependencies using `tests/Elsa/Architecture/`
- [x] T024 Mark every completed task and capture final verification evidence in `specs/095-runtime-intent-handlers/tasks.md`

## Verification Evidence

- Focused #675 contribution, dispatcher, checkpoint/outbox, scheduler, and failure-path suite: 44 passed.
- Complete Runtime suite: 930 passed.
- Runtime.Resumption suite: 15 passed.
- Groundwork checkpoint/outbox/resumption regressions: 27 passed.
- Architecture suite: 197 passed; the sole failure is the unrelated EF surface ratchet reporting widespread missing restore assets/stale baseline entries across projects outside this work unit.
- Extension-point map regenerated successfully.
- Final diff check and dependency audit found no broker, Studio, Design, or `WorkflowDefinitionActivity` dependency additions.

---

## Dependencies & Execution Order

### Phase Dependencies

- Phase 1 establishes the baseline.
- Phase 2 tests must fail before implementation.
- US1 implements the shared contribution seam and blocks US2/US3.
- US2 and US3 may proceed in parallel after US1, but they share the aggregate/DI files and must be integrated sequentially.
- Documentation and full QA follow all user stories.

### User Story Dependencies

- **US1**: starts after the failing-test foundation and has no story dependency.
- **US2**: depends on US1’s handler contract, registration extension, and aggregate dispatcher.
- **US3**: depends on US1’s aggregate dispatcher and existing outbox failure path.

### Parallel Opportunities

- T002, T003, and T004 touch separate test files.
- T005 and T006 touch separate Core files.
- T012 and T017 touch separate regression-test files after US1.
- T020 and T021 are independent documentation updates.

## Parallel Example: User Story 1

```text
Task T005: Define the public handler contract.
Task T006: Define the keyed contribution metadata.
```

## Implementation Strategy

1. Establish the baseline and failing guardrails.
2. Deliver US1 as the tracer bullet through the real checkpoint/outbox/global-resumption path.
3. Migrate scheduler delivery through the same contribution surface and run parity regressions.
4. Prove unsupported work remains safely failed and visible.
5. Update canonical extension documentation and run full proportional QA.

## Format Validation

All 24 tasks use the required checkbox, sequential task ID, optional parallel marker, story label where applicable, and an exact repository file or project path.
