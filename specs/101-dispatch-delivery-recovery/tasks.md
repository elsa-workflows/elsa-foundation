# Tasks: Dispatch Delivery Recovery

**Input**: Design documents from `specs/101-dispatch-delivery-recovery/`

**Prerequisites**: `plan.md`, `spec.md`, `research.md`, `data-model.md`, `contracts/`, `quickstart.md`

**Tests**: Required by #681. Contract, safety, race, duplicate, crash/restart, API authorization, and regression tests precede or accompany implementation.

## Phase 1: Setup and Baseline

**Purpose**: Lock the clean #680 baseline and work-unit scope.

- [x] T001 Verify the committed #680 baseline, existing ignore coverage, and focused Runtime/DispatchWorkflow/Groundwork/API test baselines in `tests/Elsa/`
- [x] T002 Record the validated host-policy, failure-classification, dead-letter, wait-resume, redrive, and authorization decisions in `specs/101-dispatch-delivery-recovery/`
- [x] T003 [P] Inventory existing final-claim, dispatch-inspection, capability, and extension-point surfaces in `src/Elsa/Workflows/Runtime/EXTENSION_POINTS.md`, `src/Elsa/Persistence/Groundwork/EXTENSION_POINTS.md`, and `src/Elsa/Activities/DispatchWorkflow/Runtime/EXTENSION_POINTS.md`

---

## Phase 2: Foundational Recovery Contracts

**Purpose**: Add provider-neutral safe failure/finalization/redrive primitives that block every story.

- [x] T004 [P] Add failing policy/classification/effective-final-status contract tests in `tests/Elsa/Workflows/Runtime/Tests/RuntimePostCommitOutboxProcessorTests.cs` and `tests/Elsa/Workflows/Runtime/Tests/RuntimePostCommitOutboxStoreTests.cs`
- [x] T005 [P] Add failing deterministic delivery-generation/incident/dead-letter/redrive identity tests plus missing-metadata defaults and clean current-baseline golden-fixture coverage in `tests/Elsa/Workflows/Runtime/Tests/WorkflowDispatchIdentityTests.cs` and `tests/Elsa/Persistence/Groundwork/Tests/GroundworkRuntimeDocumentFixtureTests.cs`
- [x] T006 Add safe delivery-failure classification and final-failure projection contracts in `src/Elsa/Workflows/Runtime/Core/Contracts/` and `src/Elsa/Workflows/Runtime/Core/Models/`
- [x] T007 Add redrive request/result/disposition and additive store capability in `src/Elsa/Workflows/Runtime/Core/Contracts/IWorkflowDispatchRedriveStore.cs` and `src/Elsa/Workflows/Runtime/Core/Models/WorkflowDispatchRedrive.cs`
- [x] T008 Extend `RuntimePostCommitOutboxClaimCompletion` with validated optional pending follow-up work in `src/Elsa/Workflows/Runtime/Core/Models/RuntimePostCommitOutbox.cs`
- [x] T009 Add safe generation/dead-letter/redrive metadata and deterministic identities without loosening ordinary terminal transitions in `src/Elsa/Workflows/Runtime/Core/Models/WorkflowDispatchRecord.cs` and `src/Elsa/Workflows/Runtime/Core/Models/WorkflowDispatchIdentity.cs`
- [x] T010 Implement only the additive follow-up completion validation/storage primitive, without the US2 finalization bundle, in `src/Elsa/Workflows/Runtime/Services/InMemoryRuntimeCheckpointCommitStore.cs`, `src/Elsa/Workflows/Runtime/Services/Coalescing/RuntimeCoalescingSession.cs`, and `src/Elsa/Persistence/Groundwork/Stores/GroundworkRuntimePostCommitOutboxStore.cs`
- [x] T011 Run foundational Runtime and Groundwork contract tests and keep every new test red-before-green using `/usr/local/share/dotnet/dotnet`

**Checkpoint**: Safe finalization and redrive contracts are available to all stories.

---

## Phase 3: User Story 1 - Recover Transient Child-Start Delivery (Priority: P1) 🎯 MVP

**Goal**: Retry transient start infrastructure failures under finite host policy without new logical children or activity inputs.

**Independent Test**: Fail start delivery repeatedly and then succeed; every attempt retains the same dispatch, child, intent, outbox, and idempotency identities.

### Tests for User Story 1

- [x] T012 [P] [US1] Add finite configurable child-start contribution and no-activity-input contract tests in `tests/Elsa/Activities/DispatchWorkflow/Tests/DispatchWorkflowContractTests.cs`
- [x] T013 [P] [US1] Add transient/permanent/accepted/business-fault classification tests in `tests/Elsa/Activities/DispatchWorkflow/Tests/ChildStartExecutorTests.cs`
- [x] T014 [P] [US1] Add finite retry schedule, exhaustion effective status, claim-expiry, and stale-fence processor tests in `tests/Elsa/Workflows/Runtime/Tests/RuntimePostCommitOutboxProcessorTests.cs`
- [x] T015 [P] [US1] Add safe observability-matrix tests for attempt, retry schedule, final dead letter/incident, wait resume queued/consumed, redrive disposition, and eventual delivery result while excluding reason/exception/payload/context data in `tests/Elsa/Workflows/Runtime/Tests/RuntimePostCommitOutboxProcessorTests.cs`
- [x] T016 [US1] Add end-to-end three-failures-then-success identity convergence test in `tests/Elsa/Activities/DispatchWorkflow/Tests/DispatchWorkflowEndToEndTests.cs`

### Implementation for User Story 1

- [x] T017 [US1] Add validated host delivery policy in `src/Elsa/Activities/DispatchWorkflow/Runtime/Configuration/DispatchWorkflowDeliveryOptions.cs`
- [x] T018 [US1] Register the finite child-start policy while preserving unbounded cancel/resume policies in `src/Elsa/Activities/DispatchWorkflow/Runtime/DispatchWorkflowRuntimeFeature.cs`
- [x] T019 [US1] Classify rejection, deferred delivery, infrastructure exceptions, acknowledged delivery, and business terminal behavior safely in `src/Elsa/Activities/DispatchWorkflow/Runtime/Services/ChildStartExecutor.cs`
- [x] T020 [US1] Make the outbox processor report/store effective retry/final/eventual status with fixed safe child-start text and structured attempt/retry/final-dead-letter events in `src/Elsa/Workflows/Runtime/Services/RuntimePostCommitOutboxProcessor.cs`
- [x] T021 [US1] Preserve deterministic start/admission behavior across retries and repair paths in `src/Elsa/Activities/DispatchWorkflow/Runtime/Services/ChildStartExecutor.cs`
- [x] T022 [US1] Run the independent US1 contract, processor, executor, and end-to-end tests with `/usr/local/share/dotnet/dotnet`

**Checkpoint**: Transient infrastructure failures recover without changing logical child identity.

---

## Phase 4: User Story 2 - Resolve Exhausted Wait Delivery Exactly Once (Priority: P1)

**Goal**: Atomically dead-letter exhausted wait delivery and resume the parent once as a safe, permanently abandoned `DispatchFailed` result.

**Independent Test**: Exhaust wait delivery, recreate providers around every boundary, triple-deliver resume work, and observe one dead letter, incident ID, and parent activity completion.

### Tests for User Story 2

- [x] T023 [P] [US2] Add `DispatchFailed` result/payload/diagnostic allowlist tests in `tests/Elsa/Activities/DispatchWorkflow/Tests/DispatchWorkflowContractTests.cs`
- [x] T024 [P] [US2] Add atomic final start + dispatch + wait-resume completion tests in `tests/Elsa/Workflows/Runtime/Tests/RuntimePostCommitOutboxProcessorTests.cs` and `tests/Elsa/Workflows/Runtime/Tests/RuntimePostCommitOutboxStoreTests.cs`
- [x] T025 [P] [US2] Add Groundwork atomic commit/rollback/stale-claim and admitted/visible-child-versus-finalization precedence tests in `tests/Elsa/Persistence/Groundwork/Tests/GroundworkRuntimePostCommitOutboxStoreTests.cs`
- [x] T026 [P] [US2] Add wait exhaustion, duplicate resume, zero outputs/faults, terminal-parent, and permanent redrive rejection tests in `tests/Elsa/Activities/DispatchWorkflow/Tests/DispatchWorkflowEndToEndTests.cs`
- [x] T027 [US2] Add Groundwork crash/restart tests before/after exhaustion bundle, resume claim, bookmark consumption, and acknowledgement in `tests/Elsa/Persistence/Groundwork/Tests/DispatchWorkflowDeliveryRecoveryCrashTests.cs`

### Implementation for User Story 2

- [x] T028 [US2] Implement DispatchWorkflow final-failure projection, deterministic wait follow-up creation, and safe dead-letter/incident/resume-queued events in `src/Elsa/Activities/DispatchWorkflow/Runtime/Services/WorkflowDispatchDeliveryFailureProjector.cs`
- [x] T029 [US2] Resolve the optional final-failure projector and commit its atomic aggregate in `src/Elsa/Workflows/Runtime/Services/RuntimePostCommitOutboxProcessor.cs`
- [x] T030 [US2] Atomically persist final start, safe `DispatchFailed` dead-letter metadata, and follow-up item in `src/Elsa/Workflows/Runtime/Services/InMemoryRuntimeCheckpointCommitStore.cs`
- [x] T031 [US2] Atomically persist the same finalization bundle and reject stale versions in `src/Elsa/Persistence/Groundwork/Stores/GroundworkRuntimePostCommitOutboxStore.cs`
- [x] T032 [US2] Extend safe result/payload validation and activity outcome mapping to `DispatchFailed` in `src/Elsa/Activities/DispatchWorkflow/Runtime/Models/DispatchWorkflowResult.cs`, `src/Elsa/Activities/DispatchWorkflow/Runtime/Models/WorkflowDispatchParentResumePayload.cs`, and `src/Elsa/Activities/DispatchWorkflow/Runtime/Activities/DispatchWorkflow.cs`
- [x] T033 [US2] Extend parent resume validation/delivery and safe resume-consumed/eventual-result observability without changing bookmark identity in `src/Elsa/Activities/DispatchWorkflow/Runtime/Services/ParentResumeExecutor.cs` and `src/Elsa/Activities/DispatchWorkflow/Runtime/Services/WorkflowDispatchCompletionEnricher.cs`
- [x] T034 [US2] Register the final-failure projector and keep wait resume unbounded in `src/Elsa/Activities/DispatchWorkflow/Runtime/DispatchWorkflowRuntimeFeature.cs`
- [x] T035 [US2] Run the independent US2 contract, atomicity, end-to-end, and restart suites with `/usr/local/share/dotnet/dotnet`

**Checkpoint**: Exhausted wait delivery completes the parent once and is permanently abandoned.

---

## Phase 5: User Story 3 - Inspect and Redrive Exhausted Detached Delivery (Priority: P1)

**Goal**: Safely inspect and separately authorize identity-preserving redrive of fire-and-forget dead letters without reopening the parent.

**Independent Test**: Exhaust detached delivery, inspect under read permission, deny read-only/cross-tenant mutation, redrive under manage permission, and prove one current generation/original child under concurrency and restart.

### Tests for User Story 3

- [x] T036 [P] [US3] Add in-memory eligibility, same-request idempotency, active conflict, stale completion, generation, and identity-preservation tests in `tests/Elsa/Workflows/Runtime/Tests/WorkflowDispatchRedriveTests.cs`
- [x] T037 [P] [US3] Add Groundwork atomic redrive, rollback, fencing, restart, and 100-race convergence tests in `tests/Elsa/Persistence/Groundwork/Tests/GroundworkWorkflowDispatchRedriveTests.cs`
- [x] T038 [P] [US3] Add safe failed-dispatch view, bounded-list maximum/continuation, and read/manage permission tests in `tests/Elsa/Workflows/Runtime/Api/Tests/WorkflowDispatchInspectionTests.cs`
- [x] T039 [P] [US3] Add unauthorized, read-only, cross-tenant, ineligible, duplicate, and accepted redrive endpoint tests in `tests/Elsa/Workflows/Runtime/Api/Tests/WorkflowDispatchInspectionTests.cs`
- [x] T040 [US3] Add end-to-end detached exhaustion/redrive/success tests proving parent immutability and original child identity in `tests/Elsa/Activities/DispatchWorkflow/Tests/DispatchWorkflowEndToEndTests.cs`
- [x] T041 [US3] Extend Groundwork crash suite through redrive commit, response loss, expired claim, successful admission, and acknowledgement in `tests/Elsa/Persistence/Groundwork/Tests/DispatchWorkflowDeliveryRecoveryCrashTests.cs`

### Implementation for User Story 3

- [x] T042 [US3] Implement sanctioned dispatch/outbox redrive transitions and safe dispositions in `src/Elsa/Workflows/Runtime/Core/Models/WorkflowDispatchRecord.cs` and `src/Elsa/Workflows/Runtime/Core/Models/RuntimePostCommitOutbox.cs`
- [x] T043 [US3] Implement provider-atomic in-memory redrive over the shared state/fence in `src/Elsa/Workflows/Runtime/Services/InMemoryRuntimeCheckpointCommitStore.cs`
- [x] T044 [US3] Implement provider-atomic Groundwork redrive with tenant scope, exact dead-letter linkage, OCC, and fencing in `src/Elsa/Persistence/Groundwork/Stores/GroundworkRuntimePostCommitOutboxStore.cs`
- [x] T045 [US3] Extend the allowlisted dispatch view/handlers with incident, dead-letter, attempts, generation, failure time, eligibility, and the existing bounded maximum-page/continuation contract in `src/Elsa/Workflows/Runtime/Api/Models/WorkflowDispatchViews.cs` and `src/Elsa/Workflows/Runtime/Api/Handlers/WorkflowDispatchInspectionRequestHandlers.cs`
- [x] T046 [US3] Add the request/result and manage-protected POST redrive endpoint in `src/Elsa/Workflows/Runtime/Api/Requests/WorkflowDispatchInspectionRequests.cs` and `src/Elsa/Workflows/Runtime/Api/Endpoints/WorkflowDispatchInspection.cs`
- [x] T047 [US3] Publish the redrive capability link and route in `src/Elsa/Workflows/Runtime/Api/Capabilities/RuntimeApiCapabilities.cs` and `src/Elsa/Workflows/Runtime/Api/Constants/RouteConstants.cs`
- [x] T048 [US3] Emit safe accepted/rejected redrive and eventual redrive-delivery result events without payload/context fields in `src/Elsa/Workflows/Runtime/Api/Handlers/WorkflowDispatchInspectionRequestHandlers.cs`
- [x] T049 [US3] Run the independent US3 core/provider/API/end-to-end tests with `/usr/local/share/dotnet/dotnet`

**Checkpoint**: Authorized detached redrive preserves original identity and leaves the parent unchanged.

---

## Phase 6: Polish and Cross-Cutting Completion

- [x] T050 [P] Update runtime, DispatchWorkflow, and Groundwork extension-point catalogs in `src/Elsa/Workflows/Runtime/EXTENSION_POINTS.md`, `src/Elsa/Activities/DispatchWorkflow/Runtime/EXTENSION_POINTS.md`, and `src/Elsa/Persistence/Groundwork/EXTENSION_POINTS.md`
- [x] T051 [P] Update provider storage inventory/coverage ledger only if finalization/redrive persistence surfaces changed in `src/Elsa/Persistence/Groundwork/RuntimeGroundworkStorageManifestSource.cs`, `tests/Elsa/Architecture/GroundworkPersistenceInventoryScanner.cs`, and `specs/094-harden-groundwork-stores/coverage-ledger.json`
- [x] T052 Leave generated map snapshots unchanged; map generation is explicitly user-invoked for this program and was not requested for this work unit
- [x] T053 Run the sensitive-data corpus and verify durable/API/log/metric/trace allowlists in `tests/Elsa/Workflows/Runtime/Tests/RuntimePostCommitOutboxProcessorTests.cs` and `tests/Elsa/Workflows/Runtime/Api/Tests/WorkflowDispatchInspectionTests.cs`
- [x] T054 Run full DispatchWorkflow, Runtime, Resumption, Runtime API, Groundwork, and Architecture test projects from `specs/101-dispatch-delivery-recovery/quickstart.md`
- [x] T055 Audit every FR/SC and #681 acceptance criterion against source/tests and record evidence in `specs/101-dispatch-delivery-recovery/tasks.md`
- [x] T056 Run Speckit cross-artifact analysis and a five-axis code review; record/remediate every required finding against `specs/101-dispatch-delivery-recovery/spec.md`
- [x] T057 Verify `git diff --check`, task/checklist completion, scope exclusions, and create the required local #681 commit

---

## Phase 7: Post-completion Retention/Redrive Race Correction

- [x] T058 Add red-before-green collector, in-memory redrive, and Groundwork redrive regressions proving a stale terminal retention snapshot cannot delete a reopened dispatch
- [x] T059 Replace ID-only retention deletion with provider-atomic full-record snapshot fencing in Runtime Core, in-memory, and Groundwork stores
- [x] T060 Run focused Runtime/Groundwork tests, proportional full suites, completion audit, and create the required local corrective commit

---

## Completion Audit Evidence

- **FR-001–FR-004 / SC-001**: host-owned finite retry, fixed classification, identity retention, positive backoff, fenced claims, and admitted-child precedence are covered by `ChildStartExecutorTests`, `RuntimePostCommitOutboxProcessorTests`, `WorkflowDispatchDeliveryFailureProjectorTests`, and Groundwork finalization tests.
- **FR-005–FR-011 / SC-002–SC-004**: provider-atomic exhaustion, safe incident/dead-letter evidence, wait-only deterministic resume, normal `DispatchFailed` completion, duplicate convergence, permanent wait non-redrive, and unchanged detached parents are covered by processor/store, end-to-end, and `DispatchWorkflowDeliveryRecoveryCrashTests` scenarios.
- **FR-012–FR-017 / SC-005 / SC-007**: allowlisted inspection, distinct read/manage permissions, tenant-scoped authorization-pipeline behavior, exact eligible redrive, identity/payload/policy retention, generation/fence advancement, and 100-way convergence are covered by runtime redrive, Groundwork redrive, API contract, and TestServer authorization suites.
- **FR-018–FR-020 / SC-006 / SC-008**: stable safe event IDs cover attempt, retry, final failure, incident, resume queued/consumed, redrive, and eventual success; Groundwork recreation covers retry scheduling, exhaustion, claim expiry, bookmark consumption, uncertain acknowledgement, redrive response loss, admission, and stale writers.
- **FR-021–FR-023 / SC-009–SC-010**: successful start/wait/detached, terminal child, cancellation, retention, missing/legacy-metadata defaults, and unsupported-kind safe-failure regressions remain green; no Studio, broker, #682, #683, or `WorkflowDefinitionActivity` scope was added.
- **Final test run at original work-unit completion**: DispatchWorkflow 154/154; Runtime 1049/1049; Resumption 16/16; Runtime API 51/51; Groundwork 416/416; Architecture 201/201. Two independent cross-artifact/code-review passes were remediated through closure, including order-independent replacement enforcement, constitutional type naming, exhaustive dead-letter rejection branches, projector guards/conflicts, and matching-tenant manage behavior. Generated maps are intentionally outside automatic completion and remain user-invoked.
- **Retention/redrive race correction**: deterministic collector, in-memory, SQLite, and Groundwork in-memory-provider regressions prove that redrive winning after retention selection preserves the reopened `Pending` dispatch, its incremented generation/request identity, and its pending outbox item. Full-record provider-atomic snapshot fencing is green across Runtime 1076/1076, Groundwork 447/447, DispatchWorkflow 161/161, and Architecture 201/201. Automatic map refresh was intentionally not run for this correction per the active user preference.

## Dependencies & Execution Order

- Phase 1 precedes Phase 2; Phase 2 blocks all stories.
- US1 provides finite classified retry behavior.
- US2 depends on US1 effective exhaustion and the foundational finalization aggregate.
- US3 depends on committed fire-and-forget `DispatchFailed` dead-letter evidence from US2’s finalization path, but its redrive/API tests are independently reviewable.
- Phase 6 depends on all stories.

### Parallel Opportunities

- Foundational tests T004/T005 can run in parallel.
- Within US1, T012–T015 can be delegated independently before integration.
- Within US2, T023–T026 can run in parallel; Groundwork crash closure T027 follows the atomic contract.
- Within US3, core/provider/API test authoring T036–T039 can run in parallel; end-to-end/crash closure follows.
- Documentation and provider ledger updates T050/T051 can run in parallel after implementation stabilizes.

## Implementation Strategy

1. Deliver and validate finite identity-preserving retry (US1).
2. Add atomic exhaustion and exactly-once wait failure resolution (US2).
3. Add separately authorized detached inspection/redrive (US3).
4. Complete safety, restart, race, regression, architecture, and issue audits before committing.

## Notes

- `[P]` means a different-file task with no incomplete dependency.
- Tests precede implementation and must demonstrate the intended failure first where practical.
- No task authorizes GitHub mutation, pushing, PR creation, Studio, broker, #682, #683, or WorkflowDefinitionActivity work.
