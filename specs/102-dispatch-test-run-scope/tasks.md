# Tasks: Preserve Dispatch Test-Run Scope

**Input**: Design documents from `specs/102-dispatch-test-run-scope/`

**Prerequisites**: `plan.md`, `spec.md`, `research.md`, `data-model.md`, `contracts/`, `quickstart.md`

**Tests**: Required by #682. Contract, artifact pinning, propagation, admission/cleanup race, restart, isolation, parity, registration-conflict, and regression tests precede or accompany implementation.

## Phase 1: Setup and Baseline

- [x] T001 Verify the committed #681 baseline and focused DispatchWorkflow, Runtime, Publishing API, Groundwork, Resumption, and Architecture suites with `/usr/local/share/dotnet/dotnet`
- [x] T002 Record the authoritative issue #682 body/comments and the corrected detached-only scope semantics in `specs/102-dispatch-test-run-scope/`
- [x] T003 [P] Inventory test-run start/projection cleanup, root/child admission, cancellation delivery, and Groundwork transaction seams in `src/Elsa/`

## Phase 2: Foundational Scope Contracts

- [x] T004 [P] Add failing validation, immutable-context, lifecycle, expiry-equality, legacy-null, and serialization tests in `tests/Elsa/Workflows/Runtime/Tests/WorkflowTestScopeTests.cs`
- [x] T005 [P] Add failing replacement-contract registration and order-independent duplicate-conflict tests in `tests/Elsa/Workflows/Runtime/Tests/RuntimeFeatureTests.cs` and `tests/Elsa/Persistence/Groundwork/Tests/GroundworkRuntimeStoreRegistrationTests.cs`
- [x] T006 Add `WorkflowTestScope`, lifecycle, close request/result, page query/result, and cleanup result models in `src/Elsa/Workflows/Runtime/Core/Models/`
- [x] T007 Add documented replacement contracts for scope storage, admission assertion, and cleanup in `src/Elsa/Workflows/Runtime/Core/Contracts/`
- [x] T008 Implement one shared-fence in-memory scope store with idempotent create/close/complete and bounded expiry/closing queries in `src/Elsa/Workflows/Runtime/Services/`
- [x] T009 Register the in-memory replacement contracts with explicit duplicate-provider conflict detection in `src/Elsa/Workflows/Runtime/RuntimeFeature.cs`
- [x] T010 Run foundational Runtime contract and registration tests with `/usr/local/share/dotnet/dotnet`

## Phase 3: User Story 1 - Test Parents Run Published Children

### Tests

- [x] T011 [P] [US1] Replace the TestRun compile rejection with a draft-parent proof that only the live Published child source/artifact is pinned in `tests/Elsa/Activities/DispatchWorkflow/Tests/DispatchWorkflowDesignTests.cs`
- [x] T012 [P] [US1] Add start-request/command/checkpoint/execution-state scope validation and legacy JSON-default tests in `tests/Elsa/Workflows/Runtime/Tests/WorkflowStartDispatcherTests.cs`, `RuntimeCheckpointCommandPayloadTests.cs`, and `WorkflowCheckpointSchedulerWorkHandlerTests.cs`
- [x] T013 [P] [US1] Add DispatchWorkflow record/payload/nested-child scope and exact `TestRun`/`PublishedRun`/compatibility run-kind inheritance tests in `tests/Elsa/Activities/DispatchWorkflow/Tests/DispatchWorkflowContractTests.cs` and `ChildStartExecutorTests.cs`
- [x] T014 [P] [US1] Add parent/child lifecycle inspection run-kind tests before and after provider recreation in `tests/Elsa/Workflows/Runtime/Api/Tests/WorkflowDispatchInspectionTests.cs`

### Implementation

- [x] T015 [US1] Allow Published and TestRun parent compile scopes while resolving only live Published child references in `src/Elsa/Activities/DispatchWorkflow/Design/Services/DispatchPinSource.cs`
- [x] T016 [US1] Add additive optional scope propagation to `WorkflowExecutionStartDispatchRequest`, `WorkflowExecutionStartCommandPayload`, and `RuntimeCheckpointCommandPayload` in `src/Elsa/Workflows/Runtime/Core/Models/`
- [x] T017 [US1] Persist the optional scope snapshot in `WorkflowExecutionState` and preserve it through started/completed/fault/cancel checkpoint transitions in `src/Elsa/Workflows/Runtime/Services/`
- [x] T018 [US1] Add immutable optional scope to `WorkflowDispatchRecord` and `WorkflowDispatchStartPayload`, including lifecycle equality/transition/serialization validation in `src/Elsa/Workflows/Runtime/Core/Models/WorkflowDispatchRecord.cs`
- [x] T019 [US1] Inherit the parent scope and exact run kind through DispatchWorkflow and child start validation in `src/Elsa/Activities/DispatchWorkflow/Runtime/Activities/DispatchWorkflow.cs` and `src/Elsa/Activities/DispatchWorkflow/Runtime/Services/ChildStartExecutor.cs`
- [x] T020 [US1] Carry root test scope from publishing test-run start while keeping the existing test-run projection ID/expiry aligned in `src/Elsa/Workflows/Publishing/Api/Handlers/StartWorkflowTestRunRequestHandler.cs`
- [x] T021 [US1] Run the independent Published-pin, scope propagation, run-kind, nested-child, and inspection tests with `/usr/local/share/dotnet/dotnet`

## Phase 4: User Story 2 - Detached Children Live Until Scope Teardown

### Tests

- [x] T022 [P] [US2] Add in-memory root admission-versus-close and child registration-versus-close tests for both winners, claimed start replay, response loss, expiry equality, and terminal no-op in `tests/Elsa/Workflows/Runtime/Tests/WorkflowTestScopeAdmissionTests.cs`
- [x] T023 [P] [US2] Add detached parent-completion independence plus explicit/expiry cleanup before and after child admission in `tests/Elsa/Activities/DispatchWorkflow/Tests/DispatchWorkflowEndToEndTests.cs`
- [x] T024 [P] [US2] Add detached scope-cancel marker validation, deterministic cancel identity, production-detached rejection, and duplicate delivery tests in `tests/Elsa/Activities/DispatchWorkflow/Tests/ChildCancelExecutorTests.cs`
- [x] T025 [P] [US2] Add publishing projection cleanup coordination tests proving Runtime close precedes projection/source deletion in `tests/Elsa/Workflows/Publishing/Api/Tests/WorkflowTestRunTests.cs`
- [x] T026 [P] [US2] Add bounded expiry/Closing resumption sweep, restart, and safe progress tests in `tests/Elsa/Workflows/Runtime/Resumption/Tests/WorkflowTestScopeResumptionTests.cs`

### Implementation

- [x] T027 [US2] Assert Open scope for root start/checkpoint and child dispatch commit under the shared in-memory provider fence in `src/Elsa/Workflows/Runtime/Services/InMemoryRuntimeCheckpointCommitStore.cs`
- [x] T028 [US2] Extend workflow dispatch queries with exact optional scope routing without cross-tenant/partition scans in `src/Elsa/Workflows/Runtime/Core/Models/WorkflowDispatchQuery.cs` and `src/Elsa/Workflows/Runtime/Services/InMemoryWorkflowDispatchStore.cs`
- [x] T029 [US2] Implement atomic detached cleanup transitions for Pending, Started, terminal, and cleanup/admission races with durable cancellation responsibility in `src/Elsa/Workflows/Runtime/Services/InMemoryWorkflowTestScopeStore.cs`
- [x] T030 [US2] Add scope-cancellation metadata/state and deterministic scope cancel responsibility while preserving ordinary detached and waited semantics in `src/Elsa/Workflows/Runtime/Core/Models/WorkflowDispatchCancellation.cs` and `WorkflowDispatchRecord.cs`
- [x] T031 [US2] Bridge authoritative detached scope cancellation through the existing child actor Cancel delivery in `src/Elsa/Activities/DispatchWorkflow/Runtime/Services/ChildCancelExecutor.cs`
- [x] T032 [US2] Implement the bounded scope cleaner and close-to-Closed convergence in `src/Elsa/Activities/DispatchWorkflow/Runtime/Services/WorkflowTestScopeCleaner.cs`
- [x] T033 [US2] Coordinate existing Publishing test-run expiry cleanup with the internal Runtime close capability before projection/source cleanup in `src/Elsa/Workflows/Publishing/Api/Services/`
- [x] T034 [US2] Integrate expired/Closing scope cleanup into the global resumption task and DI in `src/Elsa/Workflows/Runtime/Resumption/`
- [x] T035 [US2] Run the independent admission-race, detached lifecycle, cancellation, Publishing coordination, and resumption tests with `/usr/local/share/dotnet/dotnet`

## Phase 5: User Story 3 - Waited Parity, Isolation, and Groundwork Restart

### Tests

- [x] T036 [P] [US3] Parameterize waited success/fault/cancel/delivery-failure outcomes across `TestRun` and `PublishedRun` in `tests/Elsa/Activities/DispatchWorkflow/Tests/DispatchWorkflowEndToEndTests.cs`
- [x] T037 [P] [US3] Add hostile in-memory cleanup tests for wrong scope, tenant, partition, production, legacy-null, waited, and nested descendants in `tests/Elsa/Workflows/Runtime/Tests/WorkflowTestScopeCleanupTests.cs`
- [x] T038 [P] [US3] Add the Groundwork scope document, expiry/lifecycle query, exact dispatch-scope index, and refresh current-only fixtures in `tests/Elsa/Persistence/Groundwork/Tests/GroundworkRuntimeDocumentFixtureTests.cs`
- [x] T039 [P] [US3] Add Groundwork atomic root/child admission-versus-close tests for both winners, already-claimed starts, response loss, concurrent cleaners, terminal races, and 100 duplicate teardown attempts in `tests/Elsa/Persistence/Groundwork/Tests/GroundworkWorkflowTestScopeTests.cs`
- [x] T040 [US3] Add Groundwork restart scenarios before/after materialization, cancel responsibility commit, response loss, expired claims, and nested detached descendants in `tests/Elsa/Persistence/Groundwork/Tests/DispatchWorkflowTestScopeCrashTests.cs`

### Implementation

- [x] T041 [US3] Add Groundwork scope document serialization, storage manifest kind, lifecycle/expiry routes, and exact dispatch-scope indexes in `src/Elsa/Persistence/Groundwork/`
- [x] T042 [US3] Implement Groundwork replacement scope store and bounded lifecycle queries with tenant/partition enforcement in `src/Elsa/Persistence/Groundwork/Stores/GroundworkWorkflowTestScopeStore.cs`
- [x] T043 [US3] Extend Groundwork checkpoint/root admission transactions with Open-scope assertion and immutable context comparison in `src/Elsa/Persistence/Groundwork/Stores/`
- [x] T044 [US3] Implement Groundwork provider-atomic detached cleanup/admission serialization and cancel outbox responsibility in `src/Elsa/Persistence/Groundwork/Stores/GroundworkTestScopeCleanupStore.cs`
- [x] T045 [US3] Register Groundwork replacement contracts, reject duplicate providers in either order, and update storage coverage evidence in `src/Elsa/Persistence/Groundwork/DependencyInjection/`, `src/Elsa/Persistence/Groundwork/RuntimeGroundworkStorageManifestSource.cs`, and `specs/094-harden-groundwork-stores/coverage-ledger.json`
- [x] T046 [US3] Run waited parity, hostile isolation, Groundwork contract, race, and restart tests with `/usr/local/share/dotnet/dotnet`

## Phase 6: Polish and Completion

- [x] T047 [P] Update Runtime, DispatchWorkflow, Publishing, Groundwork, and resumption extension-point/catalog documentation only where public replacement/composition surfaces changed
- [x] T048 Leave generated maps unchanged; map refresh is intentionally user-invoked for this program
- [x] T049 Run sensitive-data checks proving scope IDs/context are absent from unauthorized public surfaces and diagnostics
- [x] T050 Run full DispatchWorkflow, Runtime, Publishing API, Groundwork, Resumption, and Architecture projects from `quickstart.md`
- [x] T051 Audit every FR/SC and #682 acceptance criterion against source/tests and record evidence below
- [x] T052 Run Speckit cross-artifact analysis and independent five-axis code review; remediate every required finding
- [x] T053 Verify `git diff --check`, task/checklist completion, detached-only cleanup, no public teardown route, no #683/broker/Studio/WorkflowDefinitionActivity expansion, and create the required local #682 commit

## Dependencies and Parallel Opportunities

- Phase 2 blocks all stories. US1 propagation blocks scope cleanup. US2 establishes cleanup semantics before Groundwork. US3 closes provider restart/isolation and waited parity.
- Tasks marked `[P]` use distinct files and may be delegated. Delegated output remains subject to root review and full integration QA.
- No task authorizes GitHub mutation, push, PR creation, broker/Studio work, distributed placement/transport, or `WorkflowDefinitionActivity` changes.

## Completion Evidence

### Speckit Analysis

- Cross-artifact analysis found no unresolved placeholders, no constitution conflict, and no uncovered #682 FR/SC. Coverage maps across T011-T053 cover all 26 functional requirements and 8 success criteria.
- Requirements checklist: `requirements.md` 19/19 complete.
- Optional Speckit git hooks were not invoked separately; this work unit requires the local commit after completion.

### Verification Commands

- `/usr/local/share/dotnet/dotnet test tests/Elsa/Activities/DispatchWorkflow/Tests/Elsa.Activities.DispatchWorkflow.Tests.csproj --no-restore --nologo` -> 161 passed.
- `/usr/local/share/dotnet/dotnet test tests/Elsa/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj --no-restore --nologo` -> 1074 passed.
- `/usr/local/share/dotnet/dotnet test tests/Elsa/Workflows/Publishing/Api/Tests/Elsa.Workflows.Publishing.Api.Tests.csproj --no-restore --nologo` -> 168 passed.
- `/usr/local/share/dotnet/dotnet test tests/Elsa/Workflows/Runtime/Api/Tests/Elsa.Workflows.Runtime.Api.Tests.csproj --no-restore --nologo` -> 55 passed.
- `/usr/local/share/dotnet/dotnet test tests/Elsa/Workflows/Runtime/Resumption/Tests/Elsa.Workflows.Runtime.Resumption.Tests.csproj --no-restore --nologo` -> 16 passed.
- `/usr/local/share/dotnet/dotnet test tests/Elsa/Persistence/Groundwork/Tests/Elsa.Persistence.Groundwork.Tests.csproj --no-restore --nologo` -> 445 passed.
- `/usr/local/share/dotnet/dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj --no-restore --nologo` -> 201 passed.
- Focused Groundwork lifecycle slice after paging fixes: 75 passed.
- `git diff --check` -> passed.

### Acceptance Mapping

- #682.1 draft parent test run executes Published child artifact: covered by DispatchWorkflow design/runtime tests, `DispatchPinSource`, and child-start retained-pin validation.
- #682.2 child inherits parent run kind and inspection sees it: covered by DispatchWorkflow, Runtime API, and Groundwork recreation tests.
- #682.3 detached test child survives normal parent completion: covered by DispatchWorkflow end-to-end detached tests.
- #682.4 scope expiry/teardown idempotently cancels live detached children: covered by Runtime, DispatchWorkflow, Publishing API, Resumption, and Groundwork cleanup tests, including duplicate and concurrent cleanup.
- #682.5 waited test children retain production success/fault/cancel/result/resume behavior: covered by waited parity matrix across `TestRun` and `PublishedRun`.
- #682.6 no unrelated production/tenant/partition/scope cancellation: covered by hostile isolation tests and scope-guard search.
- #682.7 Groundwork restart/race before and after materialization: covered by Groundwork admission-versus-close, response-loss, concurrent cleaner, terminal race, and >100 page progression tests.

### Map and Guard Evidence

- Generated maps were intentionally left unchanged under the user's explicit opt-in policy; #682 changes no shell feature declarations or package references, so generated maps are not acceptance-bearing for this slice.
- Scope-guard search found only explicit spec exclusions and pre-existing Studio/WorkflowDefinitionActivity documentation references; no broker, Studio UI, #683 transport, activity-authored scope control, or `WorkflowDefinitionActivity` implementation expansion was introduced.
