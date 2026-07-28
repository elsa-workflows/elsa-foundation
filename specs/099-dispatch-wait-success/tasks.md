# Tasks: Wait for a Successful Child and Return Safe Outputs

**Input**: Design documents from `specs/099-dispatch-wait-success/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Tests**: Required by #679. Add focused failing tests before each implementation slice and retain full regression coverage.

## Phase 1: Baseline and Compatibility Inventory

- [x] T001 Run focused baselines with `/usr/local/share/dotnet/dotnet` for `tests/Elsa/Activities/DispatchWorkflow/Tests/Elsa.Activities.DispatchWorkflow.Tests.csproj`, `tests/Elsa/Activities/Runtime/Tests/Elsa.Activities.Runtime.Tests.csproj`, `tests/Elsa/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj`, `tests/Elsa/Workflows/Runtime/Resumption/Tests/Elsa.Workflows.Runtime.Resumption.Tests.csproj`, `tests/Elsa/Persistence/Groundwork/Tests/Elsa.Persistence.Groundwork.Tests.csproj`, and `tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj`
- [x] T002 [P] Inventory public constructors and JSON shapes in `src/Elsa/Workflows/Runtime/Core/Models/WorkflowDispatchRecord.cs`, `src/Elsa/Workflows/Runtime/Core/Models/RuntimePostCommitOutbox.cs`, `src/Elsa/Workflows/Runtime/Core/Models/RuntimePostCommitIntentHandlerContribution.cs`, and `src/Elsa/Activities/DispatchWorkflow/Runtime/Models/DispatchWorkflowResult.cs`
- [x] T003 [P] Inventory suspension, consumption, output projection, enrichment, resumption, and provider fixtures in `src/Elsa/Activities/Runtime/Services/WorkflowInvokeActivitySchedulerWorkHandler.cs`, `src/Elsa/Workflows/Runtime/Services/BookmarkConsumptionCheckpointService.cs`, `src/Elsa/Workflows/Runtime/Services/RuntimeWorkflowOutputStateProjection.cs`, `src/Elsa/Workflows/Runtime/Services/WorkflowDispatchCheckpointEnricher.cs`, and `tests/Elsa/Persistence/Groundwork/Tests/`

---

## Phase 2: Deterministic Resume, Lookup, and Retry Foundations

- [x] T004 [P] Add failing public constructor/property compatibility tests in `tests/Elsa/Workflows/Runtime/Tests/RuntimePostCommitPolicyTests.cs` and `tests/Elsa/Activities/DispatchWorkflow/Tests/DispatchWorkflowContractTests.cs`
- [x] T005 [P] Add failing deterministic bookmark/stimulus/resume identity tests in `tests/Elsa/Workflows/Runtime/Tests/WorkflowDispatchIdentityTests.cs`
- [x] T006 [P] Add failing bounded/none/unbounded/saturating retry transition tests in `tests/Elsa/Workflows/Runtime/Tests/RuntimePostCommitOutboxStoreTests.cs`
- [x] T007 Extend `WorkflowDispatchIdentity` with deterministic wait bookmark, stimulus, resume-intent, and resume-idempotency derivations in `src/Elsa/Workflows/Runtime/Core/Models/WorkflowDispatchIdentity.cs`
- [x] T008 Extend `RuntimePostCommitRetryPolicy` with source-compatible unbounded retry-until-acknowledged semantics and saturating attempt accounting in `src/Elsa/Workflows/Runtime/Core/Models/RuntimePostCommitOutbox.cs`
- [x] T009 Extend `RuntimePostCommitIntentHandlerContribution` and `AddRuntimePostCommitIntentHandler` with a source-compatible policy-bearing overload and conflict validation in `src/Elsa/Workflows/Runtime/Core/Models/RuntimePostCommitIntentHandlerContribution.cs` and `src/Elsa/Workflows/Runtime/Core/Extensions/RuntimePostCommitIntentHandlerServiceCollectionExtensions.cs`
- [x] T010 Make `RuntimeCheckpointCommitter`/`RuntimePostCommitOutboxItems` select the matching contributed policy while defaulting missing or unsupported kinds to `None` in `src/Elsa/Workflows/Runtime/Services/RuntimeCheckpointCommitter.cs` and `src/Elsa/Workflows/Runtime/Services/RuntimePostCommitOutboxItems.cs`
- [x] T011 Add separate `IPostCommitOutboxLookupStore` and safe workflow-output source contracts/models under `src/Elsa/Workflows/Runtime/Core/Contracts/` and `src/Elsa/Workflows/Runtime/Core/Models/`
- [x] T012 Implement in-memory outbox lookup and the output source over `RuntimeWorkflowOutputStateProjection`, including pending terminal durable-value changes, in `src/Elsa/Workflows/Runtime/Services/`
- [x] T013 Register the additive output source and preserve all existing Runtime Core composition lifetimes in `src/Elsa/Workflows/Runtime/Extensions/RuntimeCoreServiceCollectionExtensions.cs`
- [x] T014 Run foundational identity, retry, contribution conflict, unsupported-kind, lookup, projection, and compatibility tests in `tests/Elsa/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj` and `tests/Elsa/Activities/DispatchWorkflow/Tests/Elsa.Activities.DispatchWorkflow.Tests.csproj`

**Checkpoint**: Built-in runtime composition can derive stable wait/resume identities, look up committed outbox payloads, and assign unbounded retry to one registered kind without changing defaults.

---

## Phase 3: User Story 1 - Atomic parent wait before child visibility (Priority: P1)

**Goal**: Commit suspended DispatchWorkflow state, bookmark, dispatch, and child-start responsibility in one parent checkpoint.

**Independent Test**: Pause delivery at the parent checkpoint and prove all-or-nothing wait state with no child visible before commit.

- [x] T015 [P] [US1] Add failing wait-mode activity/bookmark/default/outcome tests in `tests/Elsa/Activities/DispatchWorkflow/Tests/DispatchWorkflowContractTests.cs`
- [x] T016 [P] [US1] Add failing atomic suspended-state/bookmark/dispatch/outbox checkpoint tests in `tests/Elsa/Activities/DispatchWorkflow/Tests/DispatchWorkflowCheckpointTests.cs`
- [x] T017 [P] [US1] Add failing rollback, replay-equivalence, conflicting-replay, and no-child-before-commit tests in `tests/Elsa/Activities/DispatchWorkflow/Tests/DispatchWorkflowEndToEndTests.cs`
- [x] T018 [US1] Extend `WorkflowDispatchCheckpointRequest` with a compatible validated optional wait bookmark in `src/Elsa/Workflows/Runtime/Core/Models/WorkflowDispatchRecord.cs`
- [x] T019 [US1] Replace wait-mode rejection with deterministic non-expiring bookmark creation and wait-mode dispatch staging in `src/Elsa/Activities/DispatchWorkflow/Runtime/Activities/DispatchWorkflow.cs`
- [x] T020 [US1] Add stable DispatchWorkflow wait stimulus/resume-target identifiers in `src/Elsa/Activities/DispatchWorkflow/Runtime/Constants/DispatchWorkflowConstants.cs`
- [x] T021 [US1] Allow exactly one matching staged wait plus bookmark and build one mandatory bookmark-created commit in `src/Elsa/Activities/Runtime/Services/WorkflowInvokeActivitySchedulerWorkHandler.cs`
- [x] T022 [US1] Reuse or extract bookmark suspension construction so ordinary bookmarks and DispatchWorkflow wait checkpoints share status/metadata invariants in `src/Elsa/Workflows/Runtime/Core/Models/` and `src/Elsa/Workflows/Runtime/Services/WorkflowCreateBookmarkSchedulerWorkHandler.cs`
- [x] T023 [US1] Notify bookmark lifecycle observers only after the atomic wait commit and preserve existing direct/pipeline behavior in `src/Elsa/Activities/Runtime/Services/WorkflowInvokeActivitySchedulerWorkHandler.cs`
- [x] T024 [US1] Add Groundwork atomic commit/rollback/replay tests containing activity suspension, bookmark, wait dispatch, and child-start outbox in `tests/Elsa/Persistence/Groundwork/Tests/GroundworkRuntimeCheckpointWriterTests.cs`
- [x] T025 [US1] Cover coalescing boundaries so the wait checkpoint is mandatory and never split or silently skipped in `tests/Elsa/Workflows/Runtime/Tests/RuntimeCheckpointCoalescingTests.cs`
- [x] T026 [US1] Run wait-checkpoint suites in `tests/Elsa/Activities/DispatchWorkflow/Tests/Elsa.Activities.DispatchWorkflow.Tests.csproj`, `tests/Elsa/Activities/Runtime/Tests/Elsa.Activities.Runtime.Tests.csproj`, `tests/Elsa/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj`, and `tests/Elsa/Persistence/Groundwork/Tests/Elsa.Persistence.Groundwork.Tests.csproj`

**Checkpoint**: A child cannot become externally visible without the exact durable parent wait and replay uses one identity set.

---

## Phase 4: User Story 2 - Child Completed records safe parent-resume work (Priority: P1)

**Goal**: A child Completed checkpoint atomically carries dispatch Completed and one replay-stable safe resume intent.

**Independent Test**: Stop after child completion but before delivery, recreate services, and inspect one exact safe resume outbox item.

- [x] T027 [P] [US2] Add failing wait-only Completed checkpoint enrichment and no-Faulted/Cancelled-resume tests in `tests/Elsa/Activities/DispatchWorkflow/Tests/WorkflowDispatchCompletionEnricherTests.cs`
- [x] T028 [P] [US2] Add failing disclosed/redacted/external/custom/missing-output and current-terminal-change projection tests in `tests/Elsa/Workflows/Runtime/Tests/WorkflowOutputSourceTests.cs` and `tests/Elsa/Workflows/Runtime/Tests/RuntimeWorkflowOutputStateProjectionTests.cs`
- [x] T029 [P] [US2] Add failing uncertain-ack replay tests proving exact committed intent reuse across capture-policy change in `tests/Elsa/Activities/DispatchWorkflow/Tests/WorkflowDispatchCompletionEnricherTests.cs` and `tests/Elsa/Persistence/Groundwork/Tests/DispatchWorkflowWaitCrashTests.cs`
- [x] T030 [US2] Add validated `WorkflowDispatchParentResumePayload` with safe Completed result snapshot in `src/Elsa/Activities/DispatchWorkflow/Runtime/Models/WorkflowDispatchParentResumePayload.cs`
- [x] T031 [US2] Implement exact-status outbox lookup in `src/Elsa/Workflows/Runtime/Services/InMemoryRuntimeCheckpointCommitStore.cs` and `src/Elsa/Persistence/Groundwork/Stores/GroundworkRuntimePostCommitOutboxStore.cs`
- [x] T032 [US2] Implement safe output-source projection over stored plus terminal-commit durable values in `src/Elsa/Workflows/Runtime/Services/WorkflowOutputSource.cs`
- [x] T033 [US2] Implement `WorkflowDispatchCompletionEnricher` to select wait-mode Completed dispatches, reuse existing outbox intents, project safe outputs, and append deterministic resume work in `src/Elsa/Activities/DispatchWorkflow/Runtime/Services/WorkflowDispatchCompletionEnricher.cs`
- [x] T034 [US2] Make completion enrichment deterministic under multiple linked records, duplicate intent IDs, replayed terminal state, and enricher ordering in `src/Elsa/Activities/DispatchWorkflow/Runtime/Services/WorkflowDispatchCompletionEnricher.cs`
- [x] T035 [US2] Register the completion enricher after Runtime lifecycle enrichment and add focused registration/order tests in `src/Elsa/Activities/DispatchWorkflow/Runtime/DispatchWorkflowRuntimeFeature.cs` and `tests/Elsa/Activities/DispatchWorkflow/Tests/DispatchWorkflowContractTests.cs`
- [x] T036 [US2] Add Groundwork child Completed + dispatch Completed + safe resume outbox atomic rollback/uncertain-ack tests in `tests/Elsa/Persistence/Groundwork/Tests/DispatchWorkflowWaitCrashTests.cs`
- [x] T037 [US2] Assert serialized resume payloads contain no redacted values, exception material, stack traces, or activity-inspection payloads in `tests/Elsa/Activities/DispatchWorkflow/Tests/WorkflowDispatchCompletionEnricherTests.cs` and `tests/Elsa/Persistence/Groundwork/Tests/DispatchWorkflowWaitCrashTests.cs`
- [x] T038 [US2] Run terminal/output suites in `tests/Elsa/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj`, `tests/Elsa/Activities/DispatchWorkflow/Tests/Elsa.Activities.DispatchWorkflow.Tests.csproj`, and `tests/Elsa/Persistence/Groundwork/Tests/Elsa.Persistence.Groundwork.Tests.csproj`

**Checkpoint**: Every committed successful child has one safe replay-stable parent-resume responsibility in the same checkpoint.

---

## Phase 5: User Story 3 - Consume once and complete with a safe result (Priority: P1)

**Goal**: Global resume delivery retries until consumption and the parent activity completes once with `Completed` and a safe result.

**Independent Test**: Deliver accepted, duplicate, deferred, missing, and post-consumption resume work repeatedly and assert one callback/completion.

- [x] T039 [P] [US3] Add failing parent-resume handler tests for Dispatched/Duplicate/Deferred/Rejected, consumed, terminal, removed, and inconsistent missing-bookmark states in `tests/Elsa/Activities/DispatchWorkflow/Tests/ParentResumeExecutorTests.cs`
- [x] T040 [P] [US3] Add failing DispatchWorkflow resume-target payload validation, output assignment, redaction, and Completed-only outcome tests in `tests/Elsa/Activities/DispatchWorkflow/Tests/DispatchWorkflowContractTests.cs`
- [x] T041 [P] [US3] Add failing unbounded claim/retry/backoff/stale-owner/saturated-attempt and payload-safe structured alert-signal tests in `tests/Elsa/Workflows/Runtime/Tests/RuntimePostCommitOutboxProcessorTests.cs`
- [x] T042 [US3] Implement `ParentResumeExecutor` over `IBookmarkResumeDispatcher` with deterministic idempotency and authoritative post-dispatch state rechecks in `src/Elsa/Activities/DispatchWorkflow/Runtime/Services/ParentResumeExecutor.cs`
- [x] T043 [US3] Add a stable safe retry-deferred exception/classification and emit one alertable structured warning per recorded parent-resume retry from `src/Elsa/Activities/DispatchWorkflow/Runtime/` and `src/Elsa/Workflows/Runtime/Services/RuntimePostCommitOutboxProcessor.cs`, carrying identifiers/intent kind/saturated attempt/next availability but no payload, result, exception, or actor-envelope values
- [x] T044 [US3] Implement the context-shaped DispatchWorkflow `[ResumeTarget]` callback to read `IExecutionExpressionState.ResumeInput`, validate payload, set both outputs, and select `Completed` in `src/Elsa/Activities/DispatchWorkflow/Runtime/Activities/DispatchWorkflow.cs`
- [x] T045 [US3] Register parent-resume intent handling with retry-until-acknowledged positive backoff while leaving child-start policy unchanged in `src/Elsa/Activities/DispatchWorkflow/Runtime/DispatchWorkflowRuntimeFeature.cs`
- [x] T046 [US3] Add real global outbox plus bookmark dispatcher/actor integration proving outside-mailbox delivery and retry until consumption in `tests/Elsa/Activities/DispatchWorkflow/Tests/DispatchWorkflowEndToEndTests.cs`
- [x] T047 [US3] Add duplicate terminal/resume/consumption convergence tests proving one callback, one bookmark delete, one activity completion, and one graph continuation in `tests/Elsa/Activities/DispatchWorkflow/Tests/DispatchWorkflowEndToEndTests.cs`
- [x] T048 [US3] Add terminal-parent and removed-parent acknowledgement tests plus nonterminal missing-bookmark retry tests in `tests/Elsa/Activities/DispatchWorkflow/Tests/ParentResumeExecutorTests.cs`
- [x] T049 [US3] Re-run fire-and-forget and unsupported-intent tests in `tests/Elsa/Activities/DispatchWorkflow/Tests/DispatchWorkflowEndToEndTests.cs` and `tests/Elsa/Workflows/Runtime/Tests/RuntimePostCommitOutboxProcessorTests.cs` to prove unchanged `Dispatched` and failed/final semantics
- [x] T050 [US3] Run resume suites in `tests/Elsa/Activities/DispatchWorkflow/Tests/Elsa.Activities.DispatchWorkflow.Tests.csproj`, `tests/Elsa/Activities/Runtime/Tests/Elsa.Activities.Runtime.Tests.csproj`, `tests/Elsa/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj`, and `tests/Elsa/Workflows/Runtime/Resumption/Tests/Elsa.Workflows.Runtime.Resumption.Tests.csproj`

**Checkpoint**: Successful wait mode completes exactly once with a safe result, and resume work cannot be lost between actor acceptance and bookmark consumption.

---

## Phase 6: User Story 4 - Groundwork crash convergence (Priority: P1)

**Goal**: Recreate provider/runtime services at every successful wait boundary and converge on the same result.

**Independent Test**: Run the complete named crash matrix with Groundwork and assert one child, bookmark consumption, parent completion, and safe result.

- [x] T051 [P] [US4] Add failing current-only v3 outbox fixture tests for the unbounded parent-resume policy in `tests/Elsa/Persistence/Groundwork/Tests/GroundworkRuntimeDocumentFixtureFactory.cs` and `tests/Elsa/Persistence/Groundwork/Tests/Fixtures/`
- [x] T052 [US4] Replace the pre-GA Groundwork outbox baseline with v3, keep minimum-readable equal to current, remove the v2 fixture, and require datastore reset for older rows in `src/Elsa/Persistence/Groundwork/Serialization/ElsaRuntimeDocumentVersions.cs`
- [x] T053 [US4] Verify the current-only v3 fixture, empty Elsa upcaster set, lookup physicalization, and access scope in `src/Elsa/Persistence/Groundwork/DependencyInjection/GroundworkRuntimeStoreRegistration.cs`, `tests/Elsa/Persistence/Groundwork/Tests/GroundworkRuntimePersistenceRegistrationTests.cs`, `tests/Elsa/Persistence/Groundwork/Tests/GroundworkRuntimeDocumentFixtureTests.cs`, and `tests/Elsa/Persistence/Groundwork/Tests/GroundworkRuntimePostCommitOutboxStoreTests.cs`
- [x] T054 [P] [US4] Add service-recreation crash tests before/after parent suspension and during child-start claim/materialization in `tests/Elsa/Persistence/Groundwork/Tests/DispatchWorkflowWaitCrashTests.cs`
- [x] T055 [P] [US4] Add service-recreation crash tests before/after child Completed, safe output capture, resume-intent commit, and uncertain terminal acknowledgement in `tests/Elsa/Persistence/Groundwork/Tests/DispatchWorkflowWaitCrashTests.cs`
- [x] T056 [P] [US4] Add service-recreation crash tests during parent-resume claim/dispatch, claim expiry, bookmark consumption, and uncertain outbox acknowledgement in `tests/Elsa/Persistence/Groundwork/Tests/DispatchWorkflowWaitCrashTests.cs`
- [x] T057 [US4] Add crash tests after bookmark consumption and before/after ordinary parent graph completion propagation in `tests/Elsa/Persistence/Groundwork/Tests/DispatchWorkflowWaitCrashTests.cs`
- [x] T058 [US4] Add redacted/disclosed output equivalence and no-leak assertions across every crash boundary in `tests/Elsa/Persistence/Groundwork/Tests/DispatchWorkflowWaitCrashTests.cs`
- [x] T059 [US4] Run Groundwork in-memory/SQLite plus configured PostgreSQL, SQL Server, and Mongo manifest/registration/serialization compatibility suites from `tests/Elsa/Persistence/Groundwork/Tests/Elsa.Persistence.Groundwork.Tests.csproj`
- [x] T060 [US4] Prove in-memory wait execution converges within one process but loses uncommitted process state after recreation in `tests/Elsa/Activities/DispatchWorkflow/Tests/DispatchWorkflowEndToEndTests.cs`

**Checkpoint**: Groundwork restarts at all required boundaries and produces one equivalent safe successful wait result.

---

## Phase 7: Documentation and Completion QA

- [x] T061 [P] Update wait/retry/output/lookup guarantees in `src/Elsa/Activities/DispatchWorkflow/Runtime/README.md`, `src/Elsa/Activities/DispatchWorkflow/Runtime/EXTENSION_POINTS.md`, `src/Elsa/Workflows/Runtime/Resumption/EXTENSION_POINTS.md`, and `src/Elsa/Persistence/Groundwork/EXTENSION_POINTS.md`
- [x] T062 [P] Add scope guards in `tests/Elsa/Architecture/ArchitectureGuardTests.cs` and public-compatibility guards in `tests/Elsa/Activities/DispatchWorkflow/Tests/DispatchWorkflowContractTests.cs` and `tests/Elsa/Workflows/Runtime/Tests/RuntimePostCommitPolicyTests.cs` for no fault/cancel propagation, dead-letter/redrive, TestRun, broker, Studio, construct-only activity, or distributed-placement expansion
- [x] T063 Regenerate maps with `tools/maps/generate-extension-point-map.sh`, `tools/maps/generate-feature-dependency-map.sh`, and `tools/maps/generate-architecture-reference-map.sh`; inspect `docs/maps/manifest.json` and generated findings
- [x] T064 Run every command in `specs/099-dispatch-wait-success/quickstart.md` plus affected tests in `tests/Elsa/Workflows/Publishing/Api/Tests/` and `tests/Elsa/Activities/DispatchWorkflow/Tests/DispatchWorkflowDesignTests.cs`
- [x] T065 Run Spec Kit cross-artifact analysis over `specs/099-dispatch-wait-success/` and remediate every HIGH/CRITICAL finding or uncovered #679 acceptance criterion
- [x] T066 Independently review `specs/099-dispatch-wait-success/spec.md`, `specs/099-dispatch-wait-success/plan.md`, and `specs/099-dispatch-wait-success/tasks.md` against the current GitHub body and only then update status/checklist if approved
- [x] T067 Audit evidence in `specs/099-dispatch-wait-success/tasks.md` against all #679 requirements, crash boundaries, redaction corpus, alertable payload-safe retry signals, retry semantics, provider fixtures, and public compatibility; run `git diff --check`; mark every completed task

---

## Dependencies & Execution Order

- Phase 1 locks current behavior and source compatibility.
- Phase 2 blocks every story: all later work requires stable identities, selective retry, output projection, and committed-item lookup.
- US1 must land before the child can start in wait mode.
- US2 requires US1 and #678 terminal enrichment; it creates the durable resume responsibility.
- US3 requires US2 and completes the user-visible successful path.
- US4 integrates US1–US3 through the complete Groundwork crash matrix.
- Documentation, maps, full regression, independent spec review, and acceptance audit follow all stories.

## Parallel Opportunities

- T002/T003 inventory contracts and execution flow independently.
- T004/T005/T006 author compatibility, identity, and retry tests in separate files.
- T015/T016/T017 cover activity contract, checkpoint shape, and end-to-end rollback separately.
- T027/T028/T029 cover terminal lifecycle, output safety, and replay reuse separately.
- T039/T040/T041 cover handler, callback, and outbox transitions separately.
- T054/T055/T056 partition the Groundwork crash matrix by parent, child, and resume boundary.
- T061/T062 update docs and architecture tests independently.

## Implementation Strategy

1. Preserve public surfaces while adding deterministic wait/resume primitives.
2. Close the parent pre-start atomicity gap before enabling wait delivery.
3. Make successful child terminal state carry safe resume responsibility atomically and replay exactly.
4. Reuse bookmark dispatch/consumption and retry only until authoritative consumption.
5. Prove the complete provider-backed crash cycle, then close compatibility, docs, maps, and issue audits.

## Format Validation

All 67 tasks use required checkboxes, sequential IDs, optional parallel markers, user-story labels in story phases, and exact repository paths or precise source areas.
