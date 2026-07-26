# Tasks: Durable Runtime Alterations

**Input**: [spec.md](spec.md), [plan.md](plan.md), [research.md](research.md),
[data-model.md](data-model.md), [contracts](contracts/)

**Tests**: Required. Write each slice's focused tests first, verify the new assertions fail for the
intended reason, then implement. Preserve every existing test objective.

**Hard dependency**: No built-in alteration handler starts before the generic fake-handler tests
prove complete preflight, one atomic target checkpoint, rollback, and acknowledgement reconciliation.

## Phase 1: Shared contracts, protected payload, and registry

- [ ] T001 [US1] Add failing validation/serialization tests for plan, target/job, claim, outcome, envelope, selector, and lifecycle invariants in `tests/Elsa/Workflows/Runtime/Tests/Alterations/AlterationModelTests.cs`
- [ ] T002 [P] [US1] Add failing canonicalization/idempotency tests covering alteration order, explicit-ID normalization, query/default normalization, tenant/authority scope, JSON scalar interpretation, and hash conflicts in `tests/Elsa/Workflows/Runtime/Tests/Alterations/AlterationRequestCanonicalizerTests.cs`
- [ ] T003 [P] [US6] Add failing registration tests for built-in reservations, custom namespaces, exact schema versions, duplicate rejection, scoped lifetime, and no persisted CLR identities in `tests/Elsa/Workflows/Runtime/Tests/Alterations/WorkflowAlterationRegistryTests.cs`
- [ ] T004 [P] [US3] Add failing protected-payload/redaction tests for tenant-bound associated data, restart-stable keys, wrong-key/tenant rejection, plan DTO redaction, and no value leakage in `tests/Elsa/Workflows/Runtime/Tests/Alterations/WorkflowAlterationPayloadProtectionTests.cs`
- [ ] T005 [US1] Implement the plan, target/job, claim, outcome, selector, authority, provenance, safe-failure, envelope, descriptor, and exact lifecycle models under `src/Elsa/Workflows/Runtime/Core/Models/Alterations/`
- [ ] T006 [US1] Implement deterministic plan/job/commit identities and canonical request hashing under `src/Elsa/Workflows/Runtime/Services/Alterations/`
- [ ] T007 [US6] Add scoped handler/staging contracts and startup-built descriptor/service registry contracts under `src/Elsa/Workflows/Runtime/Core/Contracts/Alterations/`
- [ ] T008 [US6] Implement `AddWorkflowAlterationHandler<T>` registration, immutable startup validation, and the five reserved built-in descriptors in `src/Elsa/Workflows/Runtime/Core/Extensions/` and `src/Elsa/Workflows/Runtime/Services/Alterations/WorkflowAlterationRegistry.cs`
- [ ] T009 [US3] Add `IWorkflowAlterationPayloadProtector`, protected payload model, configured key-ring options, and authenticated encryption implementation under `src/Elsa/Workflows/Runtime/Core/Contracts/Alterations/` and `src/Elsa/Workflows/Runtime/Services/Alterations/`
- [ ] T010 [US1] Register scoped services, immutable registries/options, TimeProvider, and in-memory development payload protection in `src/Elsa/Workflows/Runtime/Extensions/RuntimeCoreServiceCollectionExtensions.cs`
- [ ] T011 [US1] Add feature-registration/lifetime tests in `tests/Elsa/Workflows/Runtime/Tests/WorkflowsRuntimeFeatureTests.cs` and re-run T001–T004

**Checkpoint**: Stable Runtime-owned vocabulary, deterministic request identity, protected deferred
payloads, and handler resolution exist without persistence or orchestration.

## Phase 2: Durable stores, immutable target capture, and atomic job evidence

- [ ] T012 [P] [US1] Add a shared alteration-store conformance suite for admission/idempotency, immutable-key target capture, deduplication, seal, no-match, paging, claims, expiry, cancellation, reconciliation, and tenant isolation under `tests/Elsa/Workflows/Runtime/Tests/Alterations/AlterationStoreConformanceTests.cs`
- [ ] T013 [P] [US1] Add failing multi-page capture tests proving restart continuation, no execution before seal, stable membership under query-field mutation, explicit missing-target records, and `matchAllAuthorized` validation in `tests/Elsa/Workflows/Runtime/Tests/Alterations/AlterationTargetCaptureTests.cs`
- [ ] T014 [P] [US1] Add failing checkpoint tests proving terminal job evidence commits atomically with workflow state, fingerprint/replay equality, claim-fence validation, acknowledgement reconciliation, and no deferred coalescing in `tests/Elsa/Workflows/Runtime/Tests/Alterations/AlterationCheckpointTests.cs`
- [ ] T015 [US1] Add plan/target query, admission, capture/seal, claim, cancel, page, and reconciliation store contracts under `src/Elsa/Workflows/Runtime/Core/Contracts/Alterations/`
- [ ] T016 [US1] Add immutable tenant-partition/execution-ID target-scan query semantics to `src/Elsa/Workflows/Runtime/Core/Models/` and `src/Elsa/Workflows/Runtime/Core/Contracts/IWorkflowExecutionStateStore.cs`
- [ ] T017 [US1] Implement in-memory alteration plan/target/job/idempotency stores and immutable target scan in `src/Elsa/Workflows/Runtime/Services/Alterations/InMemory*.cs` and `src/Elsa/Workflows/Runtime/Services/InMemoryWorkflowExecutionStateStore.cs`
- [ ] T018 [US1] Add alteration job state changes, validation, copier methods, fingerprint identity, and replay equivalence to `src/Elsa/Workflows/Runtime/Core/Models/RuntimeCheckpointCommit.cs` and related checkpoint models
- [ ] T019 [US1] Apply alteration job changes inside the in-memory checkpoint critical section and commit marker in `src/Elsa/Workflows/Runtime/Services/InMemoryRuntimeCheckpointCommitStore.cs`
- [ ] T020 [US1] Add `RuntimeAlterationJob` checkpoint name and force a mandatory immediate boundary in `src/Elsa/Workflows/Runtime/Core/Constants/RuntimeCheckpointNames.cs` and `src/Elsa/Workflows/Runtime/Services/Coalescing/`
- [ ] T021 [P] [US1] Add failing Groundwork golden fixture, query-route, claim-fence, paging, replay, and same-unit workflow/job commit tests under `tests/Elsa/Persistence/Groundwork/Tests/Alterations/`
- [ ] T022 [US1] Add alteration plan/job document kinds, versions, projections, indexes, and bounded query routes in `src/Elsa/Persistence/Groundwork/ElsaRuntimeStorageManifest.cs`, `Serialization/`, `Querying/`, and schema admission fixtures
- [ ] T023 [US1] Implement Groundwork alteration admission/capture/claim/page/reconcile stores under `src/Elsa/Persistence/Groundwork/Stores/` and register them in `DependencyInjection/GroundworkRuntimeStoreRegistration.cs`
- [ ] T024 [US1] Apply terminal alteration job changes in the same Groundwork unit-of-work, validation, fingerprint, and commit marker as the workflow checkpoint in `src/Elsa/Persistence/Groundwork/Stores/GroundworkRuntimeCheckpointWriter.cs`
- [ ] T025 [US1] Run the shared conformance suite against InMemory and unified Groundwork, plus the existing SQLite/PostgreSQL/SQL Server/MongoDB admission smoke lanes

**Checkpoint**: A target cohort can be durably captured and sealed, jobs can be claimed/replayed, and
terminal job truth can commit atomically with a workflow checkpoint.

## Phase 3: User Story 1 — durable plan orchestration

**Goal**: Submit, capture, execute, inspect, retry, and cancel one durable plan independently of any
specific production alteration.

**Independent test**: Use internal fake handlers to prove multi-page capture, independent target jobs,
complete preflight, atomic rollback, cooperative cancellation, and acknowledgement reconciliation.

- [ ] T026 [P] [US1] Add failing fake-handler tests for ordered complete preflight, failure at ordinal N, all-earlier-not-applied, later-skipped, one checkpoint, independent targets, and conflicting handlers in `tests/Elsa/Workflows/Runtime/Tests/Alterations/WorkflowAlterationJobExecutorTests.cs`
- [ ] T027 [P] [US1] Add failing orchestration tests for submission replay/conflict, capture retry/failure, bounded claims, worker restart, cancellation races, and terminal count invariants in `tests/Elsa/Workflows/Runtime/Tests/Alterations/WorkflowAlterationOrchestrationTests.cs`
- [ ] T028 [US1] Implement submission validation/composition, authorization snapshot input, canonical hash, protected payload admission, and replay result in `src/Elsa/Workflows/Runtime/Services/Alterations/WorkflowAlterationPlanService.cs`
- [ ] T029 [US1] Implement durable target capture, seal, retry/backoff, explicit missing-target handling, and cancellation in `src/Elsa/Workflows/Runtime/Services/Alterations/WorkflowAlterationTargetCaptureTask.cs`
- [ ] T030 [US1] Add deterministic `AlterWorkflow` actor command/payload and dispatcher under `src/Elsa/Workflows/Runtime/Core/Models/Alterations/` and `src/Elsa/Workflows/Runtime/Services/Alterations/`
- [ ] T031 [US1] Implement projected-state preflight/staging workspace, handler sequencing, safe outcomes, one mandatory checkpoint, and claim/checkpoint reconciliation in `src/Elsa/Workflows/Runtime/Services/Alterations/WorkflowAlterationJobExecutor.cs`
- [ ] T032 [US1] Implement bounded job leasing/dispatch and plan terminalization/cancellation tasks in `src/Elsa/Workflows/Runtime/Services/Alterations/WorkflowAlterationJobTask.cs` and `WorkflowAlterationPlanReconciliationTask.cs`
- [ ] T033 [US1] Wire actor command routing, background tasks, options, and feature activation through `src/Elsa/Workflows/Runtime/Extensions/RuntimeCoreServiceCollectionExtensions.cs`
- [ ] T034 [US1] Run T026–T027 and existing actor/checkpoint/coalescing/restart tests before any built-in handler work

**Checkpoint**: Generic atomic jobs are proven. This checkpoint blocks T045 and every later built-in.

## Phase 4: User Story 1 — authenticated REST vertical slice

- [ ] T035 [P] [US1] Add failing endpoint/handler tests for submit/replay/conflict, plan read, stable job page, job read, cancellation, permissions, inaccessible-as-404, query validation, `429 Retry-After`, redaction, and capability links under `tests/Elsa/Workflows/Runtime/Api/Tests/Alterations/`
- [ ] T036 [US1] Add exact route constants, request/response records, redacted views, projections, and safe ProblemDetails codes under `src/Elsa/Workflows/Runtime/Api/{Constants,Requests,Models}/Alterations/`
- [ ] T037 [US1] Implement submit/read/page/read-job/cancel mediator handlers and tenant/authority projection under `src/Elsa/Workflows/Runtime/Api/Handlers/Alterations/`
- [ ] T038 [US1] Implement FastEndpoints with `WorkflowRuntimeManage` for submit/cancel and `WorkflowRuntimeRead` for reads under `src/Elsa/Workflows/Runtime/Api/Endpoints/Alterations/`
- [ ] T039 [US1] Add the five alteration relations to `src/Elsa/Workflows/Runtime/Api/Capabilities/RuntimeApiCapabilities.cs` only when required services are composed
- [ ] T040 [US1] Add Runtime API feature wiring/options and reference-host restart-stable protection-key configuration in `src/Elsa/Workflows/Runtime/Api/WorkflowsRuntimeApiFeature.cs` and `src/Apps/Elsa.Server/`
- [ ] T041 [US1] Run T035 and verify the implemented OpenAPI shape against `contracts/runtime-alterations.openapi.yaml`

**Checkpoint**: Fake handlers can be exercised through the complete authenticated durable REST surface.

## Phase 5: User Story 2 — cancel workflow executions

**Independent test**: Submit single and bulk cancellation and observe terminal checkpoint-backed results,
including already-terminal no-ops.

- [ ] T042 [P] [US2] Add failing handler tests for exclusivity, active cleanup, already-terminal no-op, concurrent terminal race, dispatch cancellation, replay, and success-only-after-checkpoint in `tests/Elsa/Workflows/Runtime/Tests/Alterations/CancelWorkflowAlterationHandlerTests.cs`
- [ ] T043 [US2] Extract reusable cancellation staging from `src/Elsa/Workflows/Runtime/Services/WorkflowCancelSchedulerWorkHandler.cs` into `src/Elsa/Workflows/Runtime/Services/WorkflowCancellationPlanner.cs` without changing ordinary cancel behavior
- [ ] T044 [US2] Implement/register `CancelWorkflow/1` under `src/Elsa/Workflows/Runtime/Services/Alterations/Handlers/`
- [ ] T045 [US2] Add authenticated single/bulk cancellation integration tests under `tests/Elsa/Workflows/Runtime/Api/Tests/Alterations/CancelWorkflowAlterationApiTests.cs`
- [ ] T046 [US2] Run cancellation, actor, dispatch-workflow, and checkpoint regression suites

## Phase 6: User Story 3 — modify workflow variables

**Independent test**: Modify one root variable and prove missing, shadowed, stale, incompatible, and
sensitive cases fail safely without mutation or payload disclosure.

- [ ] T047 [P] [US3] Add failing tests for stable reference lookup, root-only scope, captured revision, declaration type/protection validation, atomic rollback, and redacted evidence in `tests/Elsa/Workflows/Runtime/Tests/Alterations/ModifyVariableAlterationHandlerTests.cs`
- [ ] T048 [US3] Implement protected `ModifyVariable/1` payload decoding and declared `ValueEnvelope` conversion under `src/Elsa/Workflows/Runtime/Services/Alterations/Handlers/`
- [ ] T049 [US3] Stage `VariableFrameState.Set` through the projected workflow state with the captured root-frame revision and register the handler
- [ ] T050 [US3] Add Runtime API tests for valid/stale/missing/container/type cases and absence of before/replacement values in every plan/job response under `tests/Elsa/Workflows/Runtime/Api/Tests/Alterations/ModifyVariableAlterationApiTests.cs`
- [ ] T051 [US3] Run variable-frame, value-protection, output-capture, checkpoint, and persistence regression suites

## Phase 7: User Story 4 — schedule and reschedule activities

**Independent test**: Schedule a capability-authorized direct child and supersede every eligible source
state with fresh identity/lineage while invalid topology, live work, blocking incidents, and terminal
workflows reject atomically.

- [ ] T052 [P] [US4] Add failing compiler/hash/round-trip tests for optional exact operator-scheduling capability on executable child relations in `tests/Elsa/Workflows/Publishing/Api/Tests/OperatorActivitySchedulingCapabilityTests.cs`
- [ ] T053 [P] [US4] Add failing policy registry and parent-state/scope/duplicate/completion tests in `tests/Elsa/Workflows/Runtime/Tests/Alterations/OperatorActivitySchedulingPolicyTests.cs`
- [ ] T054 [US4] Add capability/policy contracts under `src/Elsa/Activities/Runtime/Core/`, pin capability data into executable child relations, and include it in `src/Elsa/Workflows/Publishing/Api/Services/WorkflowExecutableHasher.cs`
- [ ] T055 [US4] Add startup registration and at least one first-party activity-owned scheduling policy needed by the e2e fixture under the owning activity module
- [ ] T056 [P] [US4] Add failing `ScheduleActivity/1` tests for exact artifact/node/direct-parent capability, active scope, conflict detection, server-derived identity/provenance, authored input evaluation, and replay in `tests/Elsa/Workflows/Runtime/Tests/Alterations/ScheduleActivityAlterationHandlerTests.cs`
- [ ] T057 [US4] Extract reusable scheduling staging from `src/Elsa/Workflows/Runtime/Services/WorkflowScheduleActivitySchedulerWorkHandler.cs` and implement/register `ScheduleActivity/1`
- [ ] T058 [P] [US4] Add failing supersession tests for all eligible/ineligible statuses, blocking incidents, terminal workflow, claimed work, descendant/bookmark/timer/queue cleanup, immutable pinned inputs, fresh scope/identity, and lineage in `tests/Elsa/Workflows/Runtime/Tests/Alterations/RescheduleActivityAlterationHandlerTests.cs`
- [ ] T059 [US4] Add `Superseded` status, successor fields, serialization/upcast fixtures, inspection projection, and immutable supersession lineage under `src/Elsa/Workflows/Runtime/Core/Models/` and `src/Elsa/Persistence/Groundwork/Serialization/`
- [ ] T060 [US4] Implement targeted source-subtree inventory/reclamation and successor staging under `src/Elsa/Workflows/Runtime/Services/Alterations/ActivityExecutionSupersessionPlanner.cs`
- [ ] T061 [US4] Implement/register `RescheduleActivity/1`, cloning pinned durable inputs and rejecting incident resolution/recovery
- [ ] T062 [US4] Add Runtime API integration tests for schedule/reschedule success, every rejection state, duplicate delivery, and safe outcomes under `tests/Elsa/Workflows/Runtime/Api/Tests/Alterations/ActivitySchedulingAlterationApiTests.cs`
- [ ] T063 [US4] Run scheduler, retry/resume, bookmarks, durable timers, incidents, activity hierarchy/inspection, graph, BPMN, and coalescing regression suites

## Phase 8: User Story 5 — migrate compatible suspended executions

**Independent test**: Migrate one quiescent suspended execution and reject each compatibility or liveness
dimension with byte-equivalent pre/post state.

- [ ] T064 [P] [US5] Add a compatibility corpus covering exact five-field target identity, same definition, node/consumer/schema/contract, bookmarks/resume targets, scopes, variables/policies, dependencies, requirements, provenance/inspection, source references, and compatible downgrade in `tests/Elsa/Workflows/Runtime/Tests/Alterations/WorkflowMigrationCompatibilityValidatorTests.cs`
- [ ] T065 [P] [US5] Add quiescence race tests for running activity, scheduler claims/work, outbox claims, timer delivery, dispatch linkage, actor fence loss, and target artifact disappearance in `tests/Elsa/Workflows/Runtime/Tests/Alterations/WorkflowMigrationQuiescenceTests.cs`
- [ ] T066 [US5] Implement safe compatibility reports/finding codes and exact retained-artifact loading under `src/Elsa/Workflows/Runtime/Core/Models/Alterations/` and `src/Elsa/Workflows/Runtime/Services/Alterations/WorkflowMigrationCompatibilityValidator.cs`
- [ ] T067 [US5] Implement quiescence proof under the workflow actor/fence across scheduler, outbox, timers, liveness, dispatches, and retained Runtime state
- [ ] T068 [US5] Implement atomic source-to-target projection/reference staging and post-migration projected artifact view in `src/Elsa/Workflows/Runtime/Services/Alterations/WorkflowMigrationPlanner.cs`
- [ ] T069 [US5] Implement/register `Migrate/1`, enforce first/once composition, and validate later variable/schedule operations against staged target state
- [ ] T070 [US5] Add Runtime API integration/restart tests for compatible migration, every rejection finding, artifact disappearance, exact rollback, and mixed post-migration plans under `tests/Elsa/Workflows/Runtime/Api/Tests/Alterations/MigrateWorkflowAlterationApiTests.cs`
- [ ] T071 [US5] Run publishing/artifact identity/retention/GC, runtime restart, bookmarks, variables, inspection, scheduler, and Groundwork regression suites

## Phase 9: User Story 6 — public extension and safe inspection

**Independent test**: Register and execute a namespaced custom handler through the same atomic path and
prove unknown/duplicate/version/host-defect and sensitive-result cases remain deterministic.

- [ ] T072 [P] [US6] Add custom handler tests for namespaced exact dispatch, scoped lifetime, complete preflight, staged commit, failure rollback, safe metadata bounds, unknown versions, duplicates, and trusted-host defect logging in `tests/Elsa/Workflows/Runtime/Tests/Alterations/CustomWorkflowAlterationHandlerTests.cs`
- [ ] T073 [US6] Finalize the public registration/descriptor/staging surface only after all five built-ins exercise it; remove any built-in-only escape hatch under `src/Elsa/Workflows/Runtime/Core/Contracts/Alterations/` and `Extensions/`
- [ ] T074 [US6] Add descriptor-only discovery to Runtime capability/inspection if required by client authoring, without constructing handler instances or exposing payload schemas as executable types
- [ ] T075 [US6] Document trusted handler obligations, registration, built-ins, atomicity, redaction, and failure semantics in `src/Elsa/Workflows/Runtime/README.md` and `src/Elsa/Workflows/Runtime/EXTENSION_POINTS.md`
- [ ] T076 [US6] Document routes, permissions, capability relations, paging, idempotency, and polling in `src/Elsa/Workflows/Runtime/Api/README.md` and `src/Elsa/Workflows/Runtime/Api/EXTENSION_POINTS.md`

## Phase 10: Backend e2e, architecture, maps, and delivery

- [ ] T077 [P] Add full real-server plan/built-in/bulk/cancellation/redaction coverage in `e2e-tests/runtime-alterations/Test-AlterationPlans.ps1` with shared helpers under `e2e-tests/runtime-alterations/`
- [ ] T078 [P] Add restart, target-capture continuation, duplicate delivery, idempotency, and acknowledgement-reconciliation coverage in `e2e-tests/runtime-alterations/Test-AlterationReplayAndRestart.ps1`
- [ ] T079 [P] Add Runtime write-endpoint status/shape assertions to `e2e-tests/write-endpoints/Test-RuntimeWrites.ps1` and update its README count
- [ ] T080 Add architecture tests proving Runtime remains Design-free, public wire state contains no CLR type names, and feature/capability/store registrations are complete under `tests/Elsa/Architecture/`
- [ ] T081 Run focused unit/API/Groundwork/architecture projects, all relevant existing backend e2e suites from `quickstart.md`, then full `dotnet test Elsa.Server.slnx`; reconcile regressions rather than weakening tests
- [ ] T082 Check `docs/maps/manifest.json`, refresh the narrow domain/dependency/extension-point maps using the selected map-shell preference, and review generated findings before staging
- [ ] T083 Update `docs/program-goals/runtime-alterations.md` with the PR and verification evidence, and mark objectives/completion state truthfully
- [ ] T084 Run `git diff --check`, validate OpenAPI YAML, search for payload/value/exception leakage and persisted CLR handler identities, and inspect project-reference drift
- [ ] T085 Perform root-owned bounded self-review for correctness, atomicity/replay, authorization/tenant isolation, migration safety, API compatibility, performance bounds, docs, and tests; remediate every finding and rerun affected checks
- [ ] T086 Commit coherent implementation slices, push `codex/1016-runtime-alterations` to `origin`, open a draft PR with `Fixes #1016`, and inspect CI/check linkage

## Dependencies and Parallel Opportunities

- T001–T011 establish contracts and block persistence/orchestration.
- T012–T025 establish durable storage and checkpoint truth and block the job kernel.
- T026–T034 are the hard atomic-kernel gate. No real handler begins before T034 passes.
- T035–T041 can follow the kernel and initially use internal fake handlers.
- Cancel (T042–T046) is the first production vertical slice and issue minimum.
- Variable (T047–T051) can proceed after the kernel/API independently of scheduling.
- Schedule capability/policy (T052–T057) blocks reschedule (T058–T063).
- Migration (T064–T071) follows variable and scheduling so compatibility covers their artifact-bound
  state.
- Public extension hardening (T072–T076) happens after built-ins prove the staging contract.
- Groundwork tests begin in Phase 2, not as a final catch-up.
- E2e scripts T077–T079 may be authored in parallel once their routes and fixtures stabilize; they
  run only after all selected built-ins are composed.
