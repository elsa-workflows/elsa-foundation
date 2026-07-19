# Tasks: Durable and Inspectable Detached Dispatch

**Input**: Design documents from `specs/098-dispatch-durability-inspection/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Tests**: Required by #678. Add focused failing tests before each implementation slice and retain full regression coverage.

## Phase 1: Baseline and Compatibility Inventory

- [x] T001 Run focused baseline tests for DispatchWorkflow, Workflows Runtime/API, Groundwork, Resumption, and Architecture with `/usr/local/share/dotnet/dotnet`
- [x] T002 [P] Inventory `IWorkflowDispatchStore`, `RuntimeCheckpointCommitter`, `ChildStartExecutor`, in-memory/Groundwork/coalescing outbox stores and session flow, Groundwork writer/store/manifest/version constructors, runtime API permissions, and resumption registrations
- [x] T003 [P] Add failing public compatibility and shared lifecycle/query model tests

---

## Phase 2: Shared Store and Lifecycle Foundations

- [x] T004 [P] Add bounded `WorkflowDispatchQuery`, safe diagnostic/readiness models, retention/lifecycle contracts, and outbox claim/fencing models under `src/Elsa/Workflows/Runtime/Core/`
- [x] T005 Add an intentional record transition factory and centralize immutable equality, monotonic timestamp, idempotency, legal transition, and parent-create/child-lifecycle checkpoint ownership validation in one runtime-core lifecycle helper
- [x] T006 Add separate `IWorkflowDispatchQueryStore` and `IWorkflowDispatchDeleteStore` capabilities while preserving the complete existing `IWorkflowDispatchStore` surface
- [x] T007 Update `InMemoryWorkflowDispatchStore` to implement the additive bounded parent/child/status query, shared lifecycle validation, and delete capabilities
- [x] T008 Add replay, collapsed terminal observation, invalid regression, immutable mutation, bounded query, deterministic ordering, and delete tests

**Checkpoint**: In-memory runtime exposes one validated dispatch lifecycle/query contract without claiming restart durability.

---

## Phase 3: User Story 1 - Atomic Groundwork persistence and restart convergence (Priority: P1)

**Goal**: Parent checkpoint, Pending dispatch, child-start outbox, and marker commit atomically and resume after process failure.

**Independent Test**: Inject failures at transaction and delivery boundaries, recreate provider/runtime services, and verify one child.

- [x] T009 [P] [US1] Add failing Groundwork store identity/transition/query/access tests and manifest/version fixture tests
- [x] T010 [P] [US1] Add failing checkpoint atomic rollback, replay conflict, and uncertain acknowledgement tests containing dispatch plus outbox
- [x] T011 [US1] Declare `workflowDispatch` kind, collection, parent/child/status plus bounded composite indexes/query routes, and current document version in Groundwork manifest/serialization
- [x] T012 [US1] Implement `GroundworkWorkflowDispatchStore` with logical-ID collision defense, access scope, optimistic lifecycle saves, bounded queries, and delete
- [x] T013 [US1] Replace the #678 architecture coverage-ledger deferral with explicit store/storage-unit mapping and registration evidence
- [x] T014 [US1] Register Groundwork dispatch store and include it in transactional checkpoint apply-store construction
- [x] T015 [US1] Add the dispatch document kind to the checkpoint commit scope, validate/apply dispatch changes, and remove the explicit NotSupported deferral
- [x] T016 [US1] Add compatibility-safe `IRuntimePostCommitOutboxClaimStore`; implement atomic owner/token/visibility claims and stale acknowledge/failure rejection in in-memory and Groundwork stores; bump the outbox document to v2 with a v1-to-v2 upcaster and fixtures
- [x] T017 [US1] Make the outbox processor claim before handling, acknowledge/fail with the exact claim, and leave crashed claims reclaimable after expiry while preserving existing public constructors
- [x] T018 [US1] Forward claim/fenced-result semantics through `CoalescingRuntimePostCommitOutboxStore` and active sessions; test pass-through and overlay paths
- [x] T019 [US1] Keep the public start request unchanged; carry committed dispatch ID in private retained-start resolution and derive stable internal command/envelope/scheduler-work/root-activity identities plus exact-existing-child duplicate/conflict checks
- [x] T020 [US1] Add provider-backed restart tests before delivery, during expired claim, after child scheduler materialization, and after uncertain commit acknowledgement; assert one root chain and byte-equivalent replay
- [x] T021 [US1] Prove in-memory recreation loses process-local state while Groundwork recreation recovers committed dispatch/outbox state
- [x] T022 [US1] Run Groundwork runtime, SQLite, and applicable provider manifest/registration suites

**Checkpoint**: A committed detached dispatch cannot be lost across Groundwork process recreation and duplicates converge on one child.

---

## Phase 4: User Story 2 - Independent Started and terminal lifecycle (Priority: P1)

**Goal**: Dispatch status advances from Pending through child admission and child terminal checkpoint even after the parent completes.

**Independent Test**: Complete parent first, then admit and terminally checkpoint child; verify atomic monotonic lifecycle.

- [x] T023 [P] [US2] Add failing ChildStartExecutor Accepted/Duplicate/rejected/final-failure/crash-repair lifecycle tests, including synchronous child Completed/Faulted/Cancelled before Started returns
- [x] T024 [P] [US2] Add failing child Completed/Faulted/Cancelled terminal checkpoint enrichment tests with parent already terminal
- [x] T025 [US2] Implement `IWorkflowDispatchLifecycleService` compare-and-save updates and safe diagnostic classification
- [x] T026 [US2] Bridge ChildStartExecutor Accepted and same-identity Duplicate admission to Started while preserving existing constructors
- [x] T027 [US2] Add compatible runtime checkpoint enrichment fan-in before fingerprint/outbox persistence
- [x] T028 [US2] Implement dispatch checkpoint enricher mapping existing child terminal workflow status to linked dispatch status
- [x] T029 [US2] Permit safe Pending-to-terminal collapsed observation only for exact child terminal checkpoint linkage
- [x] T030 [US2] Add an additive claim-fenced finalization capability that atomically records final child-start outbox failure and projects DispatchFailed with allowlisted diagnostic code/category; test a process failure at that boundary while preserving #681 exhaustion/dead-letter/redrive scope
- [x] T031 [US2] Run in-memory/Groundwork lifecycle replay after commit, deterministic fingerprint, synchronous-terminal-supersedes-Started, final failure, crash repair, parent-completed, terminal atomicity, and illegal transition tests

**Checkpoint**: Detached lifecycle remains correct and queryable independently of parent completion.

---

## Phase 5: User Story 3 - Authenticated safe runtime inspection (Priority: P1)

**Goal**: Runtime-read operators can list/get by parent, child, and status without unsafe values.

**Independent Test**: Exercise permissions, filters, bounds, access scope, and serialized safe response corpus.

- [x] T032 [P] [US3] Add failing request/handler tests for parent, child, status, intersections, invalid filters, deterministic bounds, get, and not-found
- [x] T033 [P] [US3] Add failing endpoint permission tests and serialized response leak tests covering inputs, authority, exceptions, stack traces, outputs, redaction, and arbitrary metadata
- [x] T034 [US3] Add `WorkflowDispatchView` and allowlist diagnostic projection under Runtime API models
- [x] T035 [US3] Add mediator list/get requests and handlers over bounded `IWorkflowDispatchQueryStore` queries
- [x] T036 [US3] Add FastEndpoints list/get routes protected by `PermissionNames.WorkflowRuntimeRead`
- [x] T037 [US3] Add runtime capability links for dispatch list/get and document safe operational fields
- [x] T038 [US3] Run API handler/endpoint authorization, tenant-scope, bounds, all supported filter intersections, and data-leak tests

**Checkpoint**: Authenticated operators see only bounded safe dispatch evidence.

---

## Phase 6: User Story 4 - Retention and production readiness (Priority: P1)

**Goal**: Records remain while either linked execution is retained and partial production composition reports unsafe.

**Independent Test**: Remove executions in both orders; test process-local, complete durable, and every partial composition.

- [x] T039 [P] [US4] Add failing retention tests for parent-only, child-only, neither, guarded recheck race, cancellation, and store read failure
- [x] T040 [P] [US4] Add failing readiness tests for process-local, full Groundwork, and missing checkpoint/dispatch/outbox/scheduler/resumption components
- [x] T041 [US4] Implement bounded fail-closed `WorkflowDispatchRetentionCollector` over terminal dispatches and workflow execution stores
- [x] T042 [US4] Register the collector for host invocation and document its normal retention integration seam
- [x] T043 [US4] Include Pending/Started dispatch pinned artifacts in executable-reference GC roots and retain on dispatch-root query uncertainty
- [x] T044 [US4] Implement contributed provider-neutral dispatch durability component evidence and readiness aggregation without connection/type detail leakage
- [x] T045 [US4] Register process-local evidence by default and complete durable evidence from Groundwork; detect the runtime resumption pump at assessment time
- [x] T046 [US4] Expose `IWorkflowDispatchReadinessAssessor.AssessAsync` and integrate its stable assessment with readiness/health reporting without changing existing host startup behavior
- [x] T047 [US4] Run retention and readiness tests, including partial registration order permutations

**Checkpoint**: Retention is linked-execution safe and no partial production host can claim restart-safe dispatch.

---

## Phase 7: Documentation and Completion QA

- [x] T048 [P] Update Runtime, DispatchWorkflow, Groundwork, Resumption, API, and extension-point documentation with guarantee boundaries and new seams
- [x] T049 [P] Update architecture guards for no wait/fault-propagation/redrive/TestRun/broker/distributed expansion and public constructor compatibility
- [x] T050 Regenerate the narrow extension-point, feature-dependency, and architecture-reference maps if relevant inputs changed; inspect findings
- [x] T051 Run all quickstart projects plus Groundwork provider registration/manifest/fixture/conformance suites and Activities Design regression
- [x] T052 Run Spec Kit cross-artifact analysis and remediate every HIGH/CRITICAL or coverage gap
- [x] T053 Audit all #678 acceptance criteria, crash boundaries, API leak corpus, retention/readiness results, compatibility surfaces, and tasks; run `git diff --check`; mark all tasks complete

---

## Dependencies & Execution Order

- Phase 1 locks compatibility and baselines.
- Phase 2 blocks all stories with one shared lifecycle/query contract.
- US1 durable persistence precedes reliable lifecycle and inspection.
- US2 lifecycle must exist before safe API views and retention readiness claims.
- US3 and US4 can proceed independently after US2.
- Documentation, provider matrix, maps, and completion audit follow all stories.

## Parallel Opportunities

- T002/T003 inspect and test distinct surfaces.
- T009/T010 cover store versus transaction behavior.
- T020/T021 cover provider crash convergence versus process-local comparison.
- T023/T024 cover child admission versus terminal checkpoints.
- T032/T033 cover handler filters versus authorization/leak safety.
- T039/T040 cover retention versus readiness independently.
- T048/T049 touch docs and architecture tests separately.

## Implementation Strategy

1. Centralize lifecycle truth and preserve source compatibility.
2. Put dispatch inside the existing Groundwork atomic checkpoint transaction.
3. Prove process-recreation convergence at each crash boundary.
4. Project Started and terminal lifecycle through deterministic repair paths.
5. Expose allowlist-only authenticated inspection.
6. Add fail-closed retention and truthful composition readiness.
7. Complete provider, map, architecture, and acceptance audits before the local commit.

## Format Validation

All 53 tasks use required checkboxes, sequential IDs, optional parallel markers, user-story labels where applicable, and repository paths or precise source areas.
