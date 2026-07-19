# Tasks: Dispatch a Published Workflow Fire-and-Forget

**Input**: Design documents from `specs/096-dispatch-workflow-fire-and-forget/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Tests**: Required by #676. Add failing contract/integration tests before each implementation slice.

**Organization**: Tasks are grouped by user story. Shared publishing, checkpoint, identity, and start-lineage contracts land first so each story uses one authoritative seam.

## Phase 1: Setup and Baseline

- [x] T001 Run focused baseline tests for CLR activity reconciliation, publishing compilation, Runtime checkpoint/outbox/start delivery, and the in-memory execution harness
- [X] T002 [P] Create runtime and design project/test skeletons under `src/Elsa/Activities/DispatchWorkflow/` and `tests/Elsa/Activities/DispatchWorkflow/Tests/`, and add them to `Elsa.Server.slnx`
- [X] T003 [P] Add stable activity/input-options/metadata/intent/outcome constants and initial result contracts under `src/Elsa/Activities/DispatchWorkflow/Runtime/`

---

## Phase 2: Foundational Failing Contracts

- [X] T004 [P] Add failing activity schema/default/output/outcome/discovery tests in `tests/Elsa/Activities/DispatchWorkflow/Tests/DispatchWorkflowContractTests.cs`
- [X] T005 [P] Add failing live-Published options and ambiguous-source exclusion tests in `tests/Elsa/Activities/DispatchWorkflow/Tests/DispatchWorkflowDesignTests.cs`
- [X] T006 [P] Add failing executable-node pin metadata and deterministic metadata-source conflict tests in `tests/Elsa/Activities/DispatchWorkflow/Tests/DispatchWorkflowDesignTests.cs`
- [X] T007 [P] Add failing dispatch identity/model/state-change/in-memory projection tests in `tests/Elsa/Activities/DispatchWorkflow/Tests/WorkflowDispatchStateTests.cs`
- [X] T008 [P] Add failing explicit lineage/correlation/tenant/partition/authority/run-kind start tests in `tests/Elsa/Workflows/Runtime/Tests/WorkflowStartLineageTests.cs`
- [x] T009 Add a failing real activity-completed checkpoint guardrail in `tests/Elsa/Activities/DispatchWorkflow/Tests/DispatchWorkflowCheckpointTests.cs`

**Checkpoint**: New tests fail on the missing activity, publication metadata-source fan-in, dispatch state, and start-lineage seams.

---

## Phase 3: Shared Runtime and Publishing Foundations

- [X] T010 [P] Implement versioned canonical `WorkflowDispatchIdentity` derivations in `src/Elsa/Workflows/Runtime/Core/Models/WorkflowDispatchIdentity.cs`
- [X] T011 [P] Implement `WorkflowDispatchRecord`, lifecycle/mode enums, safe input descriptors, `WorkflowDispatchCheckpointRequest`, `WorkflowDispatchStartPayload`, and `WorkflowExecutionAuthoritySnapshot` in `src/Elsa/Workflows/Runtime/Core/Models/`
- [X] T012 Add `IWorkflowDispatchStore` and workflow-dispatch state changes/category validation in `src/Elsa/Workflows/Runtime/Core/Contracts/` and `src/Elsa/Workflows/Runtime/Core/Models/RuntimeCheckpointCommit.cs`
- [X] T013 Implement in-memory dispatch storage and atomic checkpoint projection/replay validation in `src/Elsa/Workflows/Runtime/Services/InMemoryWorkflowDispatchStore.cs` and `src/Elsa/Workflows/Runtime/Services/InMemoryRuntimeCheckpointCommitStore.cs`
- [X] T014 Make Groundwork reject unsupported non-empty workflow-dispatch changes with an actionable capability error in `src/Elsa/Persistence/Groundwork/Stores/GroundworkRuntimeCheckpointWriter.cs`
- [X] T015 Extend workflow start request, command/checkpoint payload, dispatcher, start/checkpoint handlers, and state with typed parent/correlation/tenant/partition/authority/run-kind plumbing in `src/Elsa/Workflows/Runtime/`
- [X] T016 Add the generic async executable-node metadata source contract and named fan-in event in `src/Elsa/Workflows/Publishing/Core/`, the single collecting handler in `src/Elsa/Workflows/Publishing/Api/Handlers/`, and deterministic enrichment in `src/Elsa/Workflows/Publishing/Api/Services/WorkflowExecutableCompiler.cs`
- [x] T017 Run foundational identity, state projection, Groundwork rejection, metadata-source fan-in, and start-lineage tests

---

## Phase 4: User Story 1 - Author a pinned workflow dispatch (Priority: P1) 🎯 Authoring MVP

**Goal**: Discover the full stable activity contract, list only unambiguous accessible live Published definitions, and pin the exact child artifact/source into the parent executable.

**Independent Test**: Catalog the activity, resolve options, compile a parent, and inspect pinned node metadata.

- [X] T018 [P] [US1] Implement `DispatchWorkflow` input/output/default/outcome surface with explicit wait-mode rejection in `src/Elsa/Activities/DispatchWorkflow/Runtime/Activities/DispatchWorkflow.cs`
- [X] T019 [P] [US1] Implement tenant-scoped unique-live-Published `WorkflowDefinitionOptionsProvider` in `src/Elsa/Activities/DispatchWorkflow/Design/Services/WorkflowDefinitionOptionsProvider.cs`
- [X] T020 [US1] Implement exact artifact/source `DispatchPinSource` in `src/Elsa/Activities/DispatchWorkflow/Design/Services/DispatchPinSource.cs`
- [X] T021 [US1] Register runtime/design shell features with correct dependencies and provider contributions in `src/Elsa/Activities/DispatchWorkflow/Runtime/DispatchWorkflowRuntimeFeature.cs` and `src/Elsa/Activities/DispatchWorkflow/Design/DispatchWorkflowDesignFeature.cs`
- [X] T022 [US1] Run activity discovery, generic dropdown, ambiguity, and publication-pin tests

**Checkpoint**: A published parent contains one exact child artifact/source pin without a Runtime → Design dependency.

---

## Phase 5: User Story 2 - Continue after durable dispatch responsibility (Priority: P1)

**Goal**: Complete the parent with `Dispatched` after one atomic checkpoint containing child ID, Pending record, and child-start intent, before child materialization.

**Independent Test**: Stop after the real parent checkpoint and inspect all state/outbox effects plus parent continuation.

- [x] T023 [US2] Add the compatibility-safe `IWorkflowDispatchStagingContext` capability and implement it on `SimpleActivityExecutionContext` in `src/Elsa/Workflows/Runtime/`
- [x] T024 [US2] Fold staged workflow-dispatch changes and start intents into the mandatory completion commit in `src/Elsa/Activities/Runtime/Services/WorkflowInvokeActivitySchedulerWorkHandler.cs`
- [x] T025 [US2] Implement fire-and-forget execution in `src/Elsa/Activities/DispatchWorkflow/Runtime/Activities/DispatchWorkflow.cs`, including parent-state inheritance, deterministic identities, safe descriptors, output, outcome, record, and intent
- [x] T026 [US2] Ensure duplicate checkpoint replay converges and different activity executions remain distinct in `tests/Elsa/Activities/DispatchWorkflow/Tests/DispatchWorkflowCheckpointTests.cs`
- [x] T027 [US2] Prove parent continuation and zero child materialization before global delivery in `tests/Elsa/Activities/DispatchWorkflow/Tests/DispatchWorkflowCheckpointTests.cs`
- [x] T028 [US2] Run the complete checkpoint/state/outbox user-story suite

**Checkpoint**: Durable responsibility is visible atomically and the parent no longer depends on child startup latency.

---

## Phase 6: User Story 3 - Start the child through existing runtime seams (Priority: P1)

**Goal**: Global post-commit delivery starts exactly the reserved child through the existing dispatcher and configured actor provider with inherited context.

**Independent Test**: Run parent and child end to end through real global resumption and actor execution.

- [x] T029 [P] [US3] Implement `ChildStartExecutor` over `IWorkflowStartDispatcher` in `src/Elsa/Activities/DispatchWorkflow/Runtime/Services/ChildStartExecutor.cs`
- [x] T030 [US3] Register the stable child-start kind through `AddRuntimePostCommitIntentHandler` in `src/Elsa/Activities/DispatchWorkflow/Runtime/DispatchWorkflowRuntimeFeature.cs`
- [x] T031 [US3] Add real start-dispatch/actor-provider assertions for inputs-only channeling, exact source selection, reserved child ID, and duplicate convergence in `tests/Elsa/Activities/DispatchWorkflow/Tests/DispatchWorkflowEndToEndTests.cs`
- [x] T032 [US3] Add correlation override/inheritance and parent linkage, tenant, partition, authority/root-initiator, and run-kind assertions in `tests/Elsa/Activities/DispatchWorkflow/Tests/DispatchWorkflowEndToEndTests.cs`
- [x] T033 [US3] Prove the parent advances before child mailbox processing, then the child executes exactly once after a global resumption sweep in `tests/Elsa/Activities/DispatchWorkflow/Tests/DispatchWorkflowEndToEndTests.cs`
- [x] T034 [US3] Document the in-memory asynchronous/non-crash-durable boundary in the dedicated runtime module README
- [x] T035 [US3] Run the complete in-memory end-to-end and Runtime start/resumption regression suites

**Checkpoint**: The first complete DispatchWorkflow tracer bullet works through existing Foundation start and actor seams.

---

## Phase 7: Documentation and Cross-Cutting QA

- [x] T036 [P] Document runtime/design feature composition and contribution surfaces in new module `EXTENSION_POINTS.md` files and update the relevant runtime/publishing catalogs
- [x] T037 [P] Add architecture guards for runtime/design direction, no Composition runtime reference, no broker/Studio dependency, and no construct-only workflow-definition activity change in `tests/Elsa/Architecture/ArchitectureGuardTests.cs`
- [x] T038 Regenerate the narrow feature-dependency and extension-point maps and inspect generated findings
- [x] T039 Run DispatchWorkflow, Runtime, Publishing.Api, relevant Design/Reconciliation, Resumption, and architecture test projects from `quickstart.md`
- [x] T040 Audit every #676 criterion, mark all tasks complete, run `git diff --check`, and verify no raw input values appear in dispatch operational state

---

## Dependencies & Execution Order

- Phase 1 establishes projects and baseline.
- Phase 2 captures expected failures before source implementation.
- Phase 3 blocks all user stories.
- US1 pinning blocks runtime activity execution because #676 never re-resolves authored definitions.
- US2 blocks US3 because no child-start intent may exist outside the parent checkpoint.
- Documentation, maps, and full QA follow all stories.

## Parallel Opportunities

- T002/T003 and T004–T008 touch separate projects/files.
- T010/T011 and T016 are separate runtime/publishing foundations.
- T018/T019 are separate runtime/design assemblies.
- T029 can begin after the payload contract while US2 checkpoint integration is finalized.
- T036/T037 touch documentation and architecture tests independently.

## Implementation Strategy

1. Lock the public activity, publication, state, and lineage contracts with failing tests.
2. Land generic shared seams and deterministic identities.
3. Deliver authoring/pinning, then the atomic parent checkpoint.
4. Complete the tracer bullet through global resumption, start dispatch, and actor execution.
5. Audit boundaries, maps, regressions, and in-memory durability wording before committing #676.

## Format Validation

All 40 tasks use required checkboxes, sequential IDs, optional parallel markers, user-story labels where applicable, and exact repository paths.
