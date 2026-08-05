# Tasks: Execution Evidence foundation vertical slice

**Status**: Approved (2026-08-05) — approved by control-room review, independent architecture/dependency review, independent requirement/test/scope review, and speckit-analyze after operation-key contract correction.

**Input**: [spec.md](spec.md), [plan.md](plan.md), [research.md](research.md), [data-model.md](data-model.md), [quickstart.md](quickstart.md), and [execution-evidence.openapi.yaml](contracts/execution-evidence.openapi.yaml).

**Prerequisites**: The plan is approved. The constitution remains Draft and the Execution Evidence ADRs remain proposed; governance tasks below must not describe either as ratified unless their separate review records that result.

**Tests**: Required. The specification explicitly requires project, Runtime, provider, API, architecture, e2e, and benchmark verification. Write focused failing coverage before its implementation task, keep test setup local to each project, and do not add shared protocol/conformance fixtures or J-Test assertions (reserved to #1138).

**Scope guard**: This is #1133 only. Do not add #1134 settled barriers/gap-free or definitive-negative semantics, #1135 stimuli/scheduling/child causation, #1136 values or disposition behavior, #1137 Evidence durability/recovery/provider conformance, #1138 shared fixtures/J-Test, or UI work. Runtime remains completely Evidence-agnostic.

## Format: `[ID] [P?] [Story] Description`

- **[P]** means the task changes disjoint files and has no dependency on an incomplete task.
- **[Story]** maps a task to the user story in [spec.md](spec.md). Setup and Runtime prerequisites have no story label.
- Tasks that edit `Elsa.Server.slnx`, server project references, shell configuration, generated maps, or the same Runtime store file are serialized even when their subject areas otherwise differ.

## Task-stage approval gate

Task-stage approval precedes T001. That approval has now been granted by the reviews recorded in the status above, so T001 may begin under the dependency graph below. Check tasks only with the focused test/command result and the review/evidence required by [quickstart.md](quickstart.md); a passing test alone does not ratify a constitution or ADR amendment.

---

## Phase 1: Governance and ADR collision repair

**Purpose**: Repair the duplicate ADR number before touching architecture, code, projects, maps, or tests; then submit the approved boundary amendments for review without claiming ratification.

- [X] T001 Rename the still-proposed Evidence durability ADR from `docs/adr/0062-execution-evidence-starts-in-memory-and-adds-groundwork-durability.md` to `docs/adr/0063-execution-evidence-starts-in-memory-and-adds-groundwork-durability.md`, preserving `docs/adr/0062-javascript-binding-grammar-is-pinned-at-publish.md` as the sole ADR 0062.
- [X] T002 Update non-generated Evidence durability references from 0062 to 0063 in `docs/plans/runtime-execution-evidence-prd.md`, `docs/adr/0052-execution-evidence-is-checkpoint-atomic-and-at-least-once-delivered.md`, `specs/147-execution-evidence-foundation/{plan.md,research.md,quickstart.md}`, and ADR backlinks under `docs/adr/`.
- [X] T003 Verify the rename and links with a repository-wide ADR-number/path search; record the before/after link inventory and the deferred generated-map update in `specs/147-execution-evidence-foundation/quickstart.md`.
- [X] T004 [P] Align only the Execution Evidence module-family and first-slice wording in `docs/glossary/elsa.md`: state InMemory as the explicit #1133 first provider while preserving the glossary’s end-state meanings and #1134–#1138 ownership rather than collapsing them into first-slice semantics (FR-001, FR-017, SC-001).
- [X] T005 [P] Draft the approved E2.1 Execution Evidence module-row amendment, preserving the contracts-only Core and explicit concrete-provider leaf, in `.specify/memory/constitution.md` and `docs/program-goals/runtime-execution-evidence.md` (FR-001, SC-001).
- [X] T006 [P] Amend the #1133 decisions in `docs/adr/0052-execution-evidence-is-checkpoint-atomic-and-at-least-once-delivered.md`, `docs/adr/0053-execution-evidence-capture-is-explicitly-session-scoped.md`, `docs/adr/0057-execution-evidence-api-exposes-neutral-verification-primitives.md`, `docs/adr/0060-execution-evidence-ordering-is-workflow-local-and-causal.md`, and `docs/adr/0063-execution-evidence-starts-in-memory-and-adds-groundwork-durability.md`.
- [X] T007 Amend only the #1133-scoped corrections in `docs/adr/0054-execution-evidence-kinds-form-a-governed-extensible-catalog.md`, `docs/adr/0055-execution-evidence-integrates-through-domain-owned-adapters.md`, and `docs/adr/0061-baseline-execution-evidence-records-committed-semantic-transitions.md`; for each of ADR 0056, 0058, and 0059 record a per-ADR `unchanged-and-why`, `amended-to-defer`, or `rejected` disposition plus explicit pass/fail against #1133 exclusions in `specs/147-execution-evidence-foundation/quickstart.md`.
- [X] T008 Complete the **Governance / Architecture-Review Disposition Gate** in `specs/147-execution-evidence-foundation/quickstart.md`: submit the glossary, E2.1, and ADR 0052–0061 plus 0063 set; record each reviewer’s disposition, unresolved concern, and accepted ratification evidence if any; otherwise retain explicit Draft/proposed wording. T009 and every code/project/test task are blocked until this gate passes.

**Checkpoint**: There is one ADR 0062 (JavaScript) and one proposed Evidence durability ADR 0063; the glossary preserves its end-state meanings; no code or generated map has been changed; and the Governance / Architecture-Review Disposition Gate has passed without overstating ratification.

---

## Phase 2: Four-project skeleton and dependency-envelope proof

**Purpose**: After T008’s Governance / Architecture-Review Disposition Gate, establish the four required project envelopes and their focused dependency tests before Runtime or Evidence behavior is added.

- [X] T009 Create the contracts-only Core skeleton and its focused project test in `src/Elsa/Workflows/ExecutionEvidence/Core/Elsa.Workflows.ExecutionEvidence.Core.csproj` and `tests/Elsa/Workflows/ExecutionEvidence/Core/Tests/Elsa.Workflows.ExecutionEvidence.Core.Tests.csproj`, proving Core has no Runtime implementation, provider, ASP.NET, FastEndpoints, or test-framework production dependency (FR-001, FR-008, SC-001).
- [X] T010 Create the provider-neutral base skeleton and its registration/dependency test in `src/Elsa/Workflows/ExecutionEvidence/{Elsa.Workflows.ExecutionEvidence.csproj,WorkflowsExecutionEvidenceFeature.cs}` and `tests/Elsa/Workflows/ExecutionEvidence/Tests/Elsa.Workflows.ExecutionEvidence.Tests.csproj`; make the feature public/non-sealed with a public virtual `ConfigureServices` and allow only Core, Runtime.Core, and Tasks.Core as intended foundation dependencies (FR-001).
- [X] T011 Create the sole process-local provider leaf and its dependency test in `src/Elsa/Workflows/ExecutionEvidence/InMemory/Elsa.Workflows.ExecutionEvidence.InMemory.csproj` and `tests/Elsa/Workflows/ExecutionEvidence/InMemory/Tests/Elsa.Workflows.ExecutionEvidence.InMemory.Tests.csproj`, proving it depends on Core/base and remains the only #1133 Evidence store (FR-001, FR-017).
- [X] T012 Create the HTTP transport skeleton and its dependency-direction test in `src/Elsa/Workflows/ExecutionEvidence/Api/{Elsa.Workflows.ExecutionEvidence.Api.csproj,WorkflowsExecutionEvidenceApiFeature.cs}` and `tests/Elsa/Workflows/ExecutionEvidence/Api/Tests/Elsa.Workflows.ExecutionEvidence.Api.Tests.csproj`, proving the API feature explicitly inherits `WorkflowsExecutionEvidenceFeature`, calls `base.ConfigureServices(services)` under the repository convention, has no peer-service-locator shortcut, and never directly or transitively references InMemory (framework §2.5; FR-001, FR-015, SC-001).
- [X] T013 Add the four source and four test projects in one serialized solution edit to `Elsa.Server.slnx`, retaining the project-tree placement required by the architecture guard.
- [X] T014 Add a fail-closed four-module/reference-direction architecture test in `tests/Elsa/Architecture/ExecutionEvidenceArchitectureTests.cs` for Core-only contracts, API-without-InMemory, explicit API-to-base feature inheritance with correct `base.ConfigureServices` semantics/no peer-service locator, explicit feature composition, and no Runtime-to-Design or Runtime-to-Evidence edge (framework §2.5; FR-001, FR-002, SC-001, SC-008).
- [X] T015 Create and independently review the exact 28-production-caller/22-file manifest in `docs/reports/runtime-checkpoint-committer-callers.md`; classify direct handlers, both checkpoint middlewares, incident/bookmark/alteration/activity-parent paths, and the synthetic coalescing flush, and make it the no-bypass test input before modifying `src/Elsa/Workflows/Runtime/` (FR-002, FR-019, SC-003).

**Checkpoint**: The four envelopes compile and their dependency tests fail closed. The reviewed caller manifest is the sole acceptance inventory for Runtime call-site coverage.

---

## Phase 3: Generic Runtime provenance, prepared ledger, replay, and coalescing

**Purpose**: Deliver the Evidence-agnostic Runtime correctness protocol before any Evidence capture adapter reads it. Every one of the 28 callers must flow through this seam; the four evidence fact producers remain separate consumers and never allocate order or context.

- [ ] T016 Add parameterized no-bypass coverage that enumerates every row of `docs/reports/runtime-checkpoint-committer-callers.md` and proves it reaches generic preparation in `tests/Elsa/Workflows/Runtime/Tests/RuntimeCheckpointCommitterCallerCoverageTests.cs` (FR-002, FR-019, SC-003).
- [ ] T017 Add failing contract/replay tests for bounded versioned opaque context, positive monotonic order, provenance-inclusive fingerprinting, matching replay, changed canonical input conflicts, and no Evidence identifier in Runtime in `tests/Elsa/Workflows/Runtime/Tests/RuntimeCheckpointPreparationTests.cs` and `tests/Elsa/Workflows/Runtime/Tests/RuntimeCheckpointSerializationTests.cs` (FR-002, SC-003).
- [ ] T018 Introduce the provider-neutral prepare/commit models and contracts in `src/Elsa/Workflows/Runtime/Core/Models/{RuntimeExecutionContextSnapshot.cs,RuntimeCheckpointProvenance.cs,RuntimeCheckpointPreparationToken.cs,RuntimeCheckpointPreparationResult.cs,RuntimeLogicalCheckpointLedgerEntry.cs,RuntimeCheckpointCommitFingerprint.cs,RuntimeCheckpoint.cs}` and `src/Elsa/Workflows/Runtime/Core/Contracts/IRuntimeCheckpointCommitStore.cs`; keep entries opaque and validate only generic bounds/version/order (FR-002, FR-009).
- [ ] T019 Change `src/Elsa/Workflows/Runtime/Services/RuntimeCheckpointCommitter.cs` to preflight marker/ledger replay, persist a `Prepared` reservation before enrichment, attach stored provenance/order before deterministic enrichers, and perform final `CommitPreparedAsync` CAS; perform persistence-policy selection and the immediate override only after enrichment and post-commit folding (FR-002, FR-009, FR-010, SC-003, SC-004).
- [ ] T020 Implement prepare/CAS/replay receipts, reserved and committed order high-watermarks, and canonical fingerprint enforcement in `src/Elsa/Workflows/Runtime/Services/InMemoryRuntimeCheckpointCommitStore.cs` and `src/Elsa/Workflows/Runtime/Services/InMemoryWorkflowExecutionStateStore.cs` (FR-002, FR-009).
- [ ] T021 Implement the same generic preparation/commit protocol and atomic marker/state/context/outbox write in `src/Elsa/Persistence/Groundwork/Stores/{GroundworkRuntimeCheckpointWriter.cs,GroundworkWorkflowExecutionStateStore.cs}` and register it through `src/Elsa/Persistence/Groundwork/DependencyInjection/GroundworkRuntimeStoreRegistration.cs` (FR-002, FR-009).
- [ ] T022 Update every production call site listed in `docs/reports/runtime-checkpoint-committer-callers.md` to use the one preparation seam, with no caller-side order/context allocation, in the exact files listed by that inventory (FR-002, FR-019, SC-003).
- [ ] T023 Separately verify the four v1 fact producers — `src/Elsa/Workflows/Runtime/Services/{WorkflowCheckpointSchedulerWorkHandler.cs,WorkflowScheduleActivitySchedulerWorkHandler.cs,WorkflowStartActivitySchedulerWorkHandler.cs,WorkflowStartSchedulerWorkHandler.cs}` — contribute source transitions only and never allocate provenance/order/context in `tests/Elsa/Workflows/Runtime/Tests/RuntimeCheckpointFactProducerSeamTests.cs` (FR-002, FR-006).
- [ ] T024 Add coalescing contract tests for a durable `Prepared` reservation containing canonical raw logical `RuntimeCheckpoint`, pre-enrichment state-change set, requested context mutation, stable source/operation identity, provenance/order, expected revisions/fence, and input fingerprint in `tests/Elsa/Workflows/Runtime/Tests/RuntimeCheckpointCoalescingPreparedLedgerTests.cs` (FR-002, FR-009, SC-003).
- [ ] T025 Implement durable logical-ledger reservation/high-watermark behavior in `src/Elsa/Workflows/Runtime/Services/Coalescing/{CoalescingRuntimeCheckpointCommitStore.cs,RuntimeCoalescingSession.cs,RuntimeCheckpointFold.cs,CoalescingRuntimeStateStores.cs}`; reservation must not attach context, write state/outbox, or expose a checkpoint/evidence fact (FR-002, FR-009).
- [ ] T026 Add crash/recovery/fold tests in `tests/Elsa/Workflows/Runtime/Tests/RuntimeCheckpointCoalescingRecoveryTests.cs` for fingerprint verification, deterministic re-enrichment without scheduler-source redrive, ordered fold, duplicate-after-fold receipt, safe compaction, and non-contiguous skipped/failed internal orders (FR-002, FR-009, FR-010, SC-003, SC-004).
- [ ] T027 Implement recovery from the stored canonical input and post-fold compaction only to an immutable receipt/marker in `src/Elsa/Workflows/Runtime/Services/Coalescing/{CoalescingRuntimeCheckpointCommitStore.cs,RuntimeCheckpointFold.cs}`; do not pretend the `Prepared` reservation is cheap or a durable enriched payload (FR-002, FR-009).
- [ ] T028 Add tests in `tests/Elsa/Workflows/Runtime/Tests/RuntimeCheckpointCoalescingPolicyTests.cs` for the after-enrichment immediate override on non-empty context, context mutation, or folded post-commit work, and for `SkipHasPostCommitWork` exposing no checkpoint/context/outbox/evidence (FR-010, SC-004).
- [ ] T029 Implement the generic after-enrichment override and skip-with-work rejection in `src/Elsa/Workflows/Runtime/Services/{RuntimeCheckpointCommitter.cs,Coalescing/CoalescingRuntimeCheckpointPersistencePolicy.cs}` without inspecting an Evidence key, intent, type, or policy (FR-002, FR-010).
- [ ] T030 Run the complete caller matrix plus focused InMemory, coalescing, and Groundwork checkpoint suites, recording the exact manifest count, provider results, and unresolved caller only in `specs/147-execution-evidence-foundation/quickstart.md` (FR-019, SC-003, SC-004).

**Checkpoint**: All baseline proposals have generic durable pre-enrichment provenance/order, all 28 callers are covered, and coalescing retains truthful prepared-input storage/recovery/overhead rather than a synthetic fold order.

---

## Phase 4: User Story 1 — Isolated capture and deliberate association (Priority: P1) 🎯 MVP

**Goal**: An authorized caller can associate before start or at one fenced checkpoint boundary, while an uncomposed or unscoped workload remains Evidence-free.

**Independent Test**: In a composed in-process host, prove associate-and-start is `Starting` until its first committed checkpoint, a late attach returns its effective order and never reconstructs earlier work, and start/attach/freeze races leave exactly one truthful outcome.

- [ ] T031 [US1] Add failing start-context, late-attach, competing-session, active-drain, and completion-freeze race tests in `tests/Elsa/Workflows/ExecutionEvidence/Tests/{EvidenceAssociationConcurrencyTests.cs,EvidenceAssociationLifecycleTests.cs}`; prove one-character and 256-character nonblank caller-supplied `Idempotency-Key` values are valid, missing/blank/257-character values fail stably, exact replay after a lost acknowledgement returns Runtime authority, and reuse with different normalized caller access, session, target, or canonical request material conflicts without mutation. Prove a stale or competing Evidence receipt/projection cannot settle a retry or freeze (FR-003, FR-004, FR-013, SC-002, SC-005).
- [ ] T032 [US1] Add generic Runtime contracts for optional start context, checkpoint-boundary `AttachIfAbsent`, authoritative generic operation receipts/commit results, and terminal checkpoint observation in `src/Elsa/Workflows/Runtime/Core/Contracts/{IRuntimeExecutionOwnershipService.cs,IRuntimeCheckpointCommitStore.cs}` and `src/Elsa/Workflows/Runtime/Core/Models/{RuntimeCheckpointCommandPayload.cs,RuntimeCheckpointCommitResult.cs,RuntimeExecutionContextOperationReceipt.cs}` (FR-004, FR-013).
- [ ] T033 [US1] Propagate generic start context through scheduler dispatch and execute late attach behind workflow-owner drain/fencing in `src/Elsa/Workflows/Runtime/Services/{RuntimeExecutionOwnershipService.cs,WorkflowSchedulerDrainer.cs,WorkflowSchedulerCommandRouter.cs,RuntimeCheckpointCommitter.cs}`; the committed CAS must check absent entry, context/order revision, fence, ledger/marker token, state, and outbox together (FR-004, SC-002).
- [ ] T034 [US1] Add Evidence session/association/reservation contracts with opaque Runtime context encoding and only a `RuntimeAssociationReceiptReference` plus Evidence-domain reconciliation state in `src/Elsa/Workflows/ExecutionEvidence/Core/Models/{EvidenceSession.cs,EvidenceAssociation.cs,EvidenceAssociationReservation.cs,RuntimeAssociationReceiptReference.cs,SessionWorkflowCutoff.cs}` and `src/Elsa/Workflows/ExecutionEvidence/Core/Contracts/IEvidenceSessionStore.cs`; do not persist an authoritative Runtime receipt in Evidence Core (FR-003, FR-004, FR-008).
- [ ] T035 [US1] Implement reserve-before-dispatch, `Starting` to `Active` promotion only at first committed checkpoint, and authoritative failure removal in `src/Elsa/Workflows/ExecutionEvidence/Services/{EvidenceAssociationService.cs,EvidenceSessionService.cs,RuntimeAssociationReconciler.cs}`. Consume the validated 1–256-character/nonblank caller-supplied `Idempotency-Key` as the durable operation key, binding normalized caller access, session, target, and canonical request material; an exact retry must reread and return the Runtime-owned authoritative receipt/commit result through the opaque reference after a lost acknowledgement, never select a competing Evidence receipt, while a material mismatch conflicts before mutation (FR-003, FR-004, FR-013, SC-002, SC-005).
- [ ] T036 [US1] Implement atomic session freeze over resolved associations and pre-freeze pending reservations in `src/Elsa/Workflows/ExecutionEvidence/Services/{EvidenceSessionService.cs,RuntimeAssociationReconciler.cs}` by rereading Runtime authority: a Runtime-committed winner racing Evidence finalization enters the frozen set, while post-freeze/admission failure/skip/rejection does not (FR-013, SC-005).
- [ ] T037 [US1] Prove all fenced race outcomes — one attach winner, drain serialization, Runtime-authoritative uncertain retry, no permanent ghost after authoritative failure, stale-Evidence-projection rejection, and freeze inclusion-or-rejection — in `tests/Elsa/Workflows/ExecutionEvidence/Tests/EvidenceAssociationConcurrencyTests.cs` (FR-004, FR-013, SC-002, SC-005).

**Checkpoint**: Association is commit-linearized by generic Runtime ownership/CAS and session completion cannot fabricate, lose, or admit an association.

---

## Phase 5: Generic six-status reader prerequisite

**Purpose**: Provide the Evidence-agnostic terminal/checkpoint/outbox observations required for truthful session completion before catalog, capture, provider, or API code relies on them.

- [ ] T038 Add failing bounded/paged six-status reader tests for normalized status subsets, workflow/kind filters, inclusive checkpoint-order cutoffs, `(order, outboxId)` order, and cursor binding in `tests/Elsa/Workflows/Runtime/Tests/RuntimePostCommitOutboxStatusReaderTests.cs` and `tests/Elsa/Persistence/Groundwork/Tests/GroundworkRuntimePostCommitOutboxStoreTests.cs` (FR-012, SC-006).
- [ ] T039 Add `Pending`, `Delivering`, `Delivered`, `FailedRetryable`, `FailedFinal`, and `Cancelled` status read contracts/cursors and terminal-checkpoint observations in `src/Elsa/Workflows/Runtime/Core/Contracts/IRuntimePostCommitOutboxStatusReader.cs` and `src/Elsa/Workflows/Runtime/Core/Models/{RuntimePostCommitOutbox.cs,RuntimePostCommitOutboxStatusReadRequest.cs,RuntimePostCommitOutboxStatusPage.cs}` (FR-012, FR-013).
- [ ] T040 Persist each committed logical checkpoint order on generic outbox items and implement the bounded reader in `src/Elsa/Workflows/Runtime/Services/{RuntimePostCommitOutboxItems.cs,RuntimePostCommitOutboxProcessor.cs,InMemoryRuntimeCheckpointCommitStore.cs,Coalescing/CoalescingRuntimePostCommitOutboxStore.cs}` (FR-012, SC-006).
- [ ] T041 Implement the same safe generic order/status page and opaque cursor binding in `src/Elsa/Persistence/Groundwork/Stores/{GroundworkRuntimePostCommitOutboxStore.cs,GroundworkRuntimeCheckpointWriter.cs}` without exposing provider offsets (FR-012, SC-006).
- [ ] T042 Verify all six states, malformed/deleted/mismatched cursor rejection, and terminal checkpoint cutoff observations across InMemory, coalescing, and Groundwork in `tests/Elsa/Workflows/Runtime/Tests/RuntimePostCommitOutboxStatusReaderTests.cs` and `tests/Elsa/Persistence/Groundwork/Tests/GroundworkRuntimePostCommitOutboxContinuityTests.cs` (FR-012, FR-019, SC-006).

**Checkpoint**: Completion code can distinguish every generic delivery state in bounded pages without an Evidence-specific Runtime surface.

---

## Phase 6: User Story 2 — Capture committed facts with stable provenance and ordering (Priority: P1)

**Goal**: An associated baseline checkpoint produces one deterministic, metadata-only evidence batch/intent after it has been committed; retries and delivery do not create false facts.

**Independent Test**: Re-enrich the same committed checkpoint, inject pre-commit failures/skip and post-commit delivery failures, and verify identical identities/order pairs, no skip exposure, and idempotent materialization.

- [ ] T043 [US2] Add Core contract tests for descriptor validation, future-only disposition names, bounded capture profile/correlation, canonical IDs, strict `(WorkflowCheckpointOrder, CheckpointOrdinal)` ordering, and typed versus registered-unknown wire envelopes in `tests/Elsa/Workflows/ExecutionEvidence/Core/Tests/EvidenceContractTests.cs` (FR-005, FR-007, FR-008, SC-007, SC-008).
- [ ] T044 [US2] Implement provider-neutral session, profile, catalog, cursor/query, contribution, reconciliation, integrity, batch, record, and wire contracts in `src/Elsa/Workflows/ExecutionEvidence/Core/{Models,Contracts,Serialization}/`; reserve `captured`, `redacted`, `omitted`, and `truncated` as names only and model no values (FR-005, FR-007, FR-008, FR-018).
- [ ] T045 [US2] Add catalog tests for the four v1 descriptors, deterministic conflict failure, arbitrary/unregistered payload rejection, and forward inspection of a registered unknown kind in `tests/Elsa/Workflows/ExecutionEvidence/Tests/EvidenceKindCatalogTests.cs` (FR-006, FR-007, SC-008).
- [ ] T046 [US2] Register canonical `workflow.started`, `workflow.completed`, `activity.started`, and `activity.completed` v1 typed metadata-only descriptors and an extensible contributed-kind catalog in `src/Elsa/Workflows/ExecutionEvidence/{Catalog/ExecutionEvidenceKindCatalog.cs,WorkflowsExecutionEvidenceFeature.cs}`; retain a public non-sealed base feature with replacement-safe virtual `ConfigureServices` semantics (FR-006, FR-007).
- [ ] T047 [US2] Add deterministic capture tests for one bounded batch/opaque intent, canonical map/array ordering, stable batch/intent/record IDs, duplicate enrichment, absent association, nonbaseline checkpoints, and no clock/random/mutable association lookup in `tests/Elsa/Workflows/ExecutionEvidence/Tests/ExecutionEvidenceCheckpointEnricherTests.cs` (FR-005, FR-009, SC-003, SC-004).
- [ ] T048 [US2] Implement `ExecutionEvidenceCheckpointEnricher` in `src/Elsa/Workflows/ExecutionEvidence/Runtime/ExecutionEvidenceCheckpointEnricher.cs` to consume only immutable generic provenance and recognize the four committed baseline transitions; append one `execution-evidence.batch.v1` intent and never introduce an Evidence branch into Runtime (FR-002, FR-006, FR-009).
- [ ] T049 [US2] Add checkpoint atomicity tests for preparation/persistence/CAS failure, after-enrichment skip-with-work rejection, and committed workflow state surviving a materializer failure in `tests/Elsa/Workflows/ExecutionEvidence/Tests/ExecutionEvidenceCheckpointAtomicityTests.cs` (FR-009, FR-010, FR-011, SC-004, SC-005).
- [ ] T050 [US2] Register the Evidence post-commit handler contribution and recurring driver in `src/Elsa/Workflows/ExecutionEvidence/{WorkflowsExecutionEvidenceFeature.cs,Tasks/ExecutionEvidenceIntentDriver.cs,Handlers/ExecutionEvidenceIntentHandler.cs}`; handler failure must change generic delivery status but never roll back a committed workflow (FR-011).
- [ ] T051 [US2] Add InMemory store tests for process-local session visibility, stable identity, idempotent record upsert, duplicate suppression, bounded filter/query behavior, and whole completed-session deletion in `tests/Elsa/Workflows/ExecutionEvidence/InMemory/Tests/InMemoryEvidenceStoreTests.cs` (FR-011, FR-017, SC-005).
- [ ] T052 [US2] Implement the process-local store and registration leaf in `src/Elsa/Workflows/ExecutionEvidence/InMemory/{Stores/InMemoryEvidenceSessionStore.cs,Stores/InMemoryEvidenceRecordStore.cs,WorkflowsExecutionEvidenceInMemoryFeature.cs}`; document no crash/restart/failover/durable-provider claim in the feature’s XML/docs comment (FR-017).
- [ ] T053 [US2] Add the associated/unscoped/late-attached ordinary-workflow integration coverage in `tests/Elsa/Workflows/ExecutionEvidence/Tests/ExecutionEvidenceCaptureIntegrationTests.cs`, proving facts begin at the returned effective order and the four distinct fact producers do not duplicate a semantic occurrence (FR-003, FR-004, FR-005, SC-002, SC-003, SC-004).
- [ ] T054 [US2] Complete base-feature registration and Runtime-adapter wiring in `src/Elsa/Workflows/ExecutionEvidence/WorkflowsExecutionEvidenceFeature.cs` and `src/Elsa/Workflows/ExecutionEvidence/Runtime/ExecutionEvidenceCheckpointEnricher.cs`, then run the Core/base/InMemory focused suites from [quickstart.md](quickstart.md) (FR-001, FR-009, FR-011, SC-003, SC-005).

**Checkpoint**: The four metadata-only facts have deterministic commit-derived identities and ordering, while unscoped/failed/skipped work cannot become Evidence.

---

## Phase 7: User Story 3 — Reconcile delivery and use the neutral API (Priority: P1)

**Goal**: An authorized remote caller can manage a process-local session, retrieve/wait for evidence using bounded opaque continuation, and distinguish delivery incompleteness from terminal integrity failure without asserting a definitive negative.

**Independent Test**: Drive a frozen session through terminal checkpoint cutoffs and each of six outbox statuses; call every OpenAPI endpoint under correct and incorrect scope/filter/cursor combinations.

- [ ] T055 [US3] Add lifecycle/integrity tests for freeze, unresolved `Starting`, terminal cutoff requirement, `Pending`/`Delivering`/`FailedRetryable` incompleteness, `FailedFinal`/`Cancelled` terminal integrity failure, all-delivered completion, and delete preconditions in `tests/Elsa/Workflows/ExecutionEvidence/Tests/EvidenceSessionCompletionTests.cs` (FR-013, FR-014, FR-016, SC-005, SC-006).
- [ ] T056 [US3] Implement terminal-cutoff acquisition and page-by-page generic outbox reconciliation in `src/Elsa/Workflows/ExecutionEvidence/Services/{EvidenceSessionService.cs,EvidenceIntegrityReconciler.cs}`; permit completed-range-without-match/deletion only after every frozen winner has a terminal cutoff and all relevant intents through it are Delivered (FR-013, FR-014, FR-016).
- [ ] T057 [US3] Add bounded query/wait tests for all filters, correlation-pair/order-range rejection, deterministic candidate order, last-examined cursor advancement after nonmatch/timeout, access/filter/page-size/session cursor binding, and every wait outcome in `tests/Elsa/Workflows/ExecutionEvidence/Tests/EvidenceQueryAndWaitTests.cs` (FR-015, FR-016, SC-007).
- [ ] T058 [US3] Implement normalized query/wait scans, authorization-scoped opaque cursors, and the five precisely named outcomes in `src/Elsa/Workflows/ExecutionEvidence/Services/{EvidenceQueryService.cs,EvidenceWaitService.cs,EvidenceCursorCodec.cs}`; `completed-range-without-match` must retain `isDefinitiveNegative: false` (FR-014, FR-015, FR-016).
- [ ] T059 [US3] Add TestServer/FastEndpoints contract tests for create/inspect/complete/delete, associate-and-start, late attach, query, and wait responses in `tests/Elsa/Workflows/ExecutionEvidence/Api/Tests/ExecutionEvidenceEndpointsTests.cs`. For both association POSTs, require `Idempotency-Key`; prove one-character and 256-character nonblank values are accepted, missing/blank/257-character values return stable `400 invalid-request`, exact replay after a lost acknowledgement returns the Runtime-authoritative prior result, and normalized caller-access/session/target/canonical-request mismatch returns `409 idempotency-conflict` without mutation (FR-004, FR-015, FR-018, SC-002, SC-007).
- [ ] T060 [US3] Implement session lifecycle and association endpoints with `execution-evidence.manage` and access-context enforcement in `src/Elsa/Workflows/ExecutionEvidence/Api/Endpoints/{CreateEvidenceSessionEndpoint.cs,GetEvidenceSessionEndpoint.cs,CompleteEvidenceSessionEndpoint.cs,DeleteEvidenceSessionEndpoint.cs,AssociateAndStartWorkflowEndpoint.cs,AttachWorkflowEndpoint.cs}`. Both association endpoints must consume, validate, and pass the caller-supplied `Idempotency-Key` to the association service rather than minting an operation key (FR-004, FR-015).
- [ ] T061 [US3] Implement records and wait endpoints plus exact OpenAPI wire mappings in `src/Elsa/Workflows/ExecutionEvidence/Api/Endpoints/{QueryEvidenceRecordsEndpoint.cs,WaitForEvidenceEndpoint.cs}` and `src/Elsa/Workflows/ExecutionEvidence/Api/Models/ExecutionEvidenceApiContracts.cs`; emit disjoint `recordShape` typed/registered-unknown unions, never provider offsets, and keep the reusable `Idempotency-Key` contract confined to the two association POSTs (FR-005, FR-015, SC-007).
- [ ] T062 [US3] Implement `WorkflowsExecutionEvidenceApiFeature : WorkflowsExecutionEvidenceFeature` in `src/Elsa/Workflows/ExecutionEvidence/Api/WorkflowsExecutionEvidenceApiFeature.cs`: call `base.ConfigureServices(services)` before FastEndpoints, API capabilities, permissions, problem responses, and domain-to-HTTP mappings; map missing/invalid association-operation keys to stable `400 invalid-request` and mismatched key reuse to `409 idempotency-conflict`, use no peer-service-locator shortcut, and retain no InMemory dependency (framework §2.5; FR-001, FR-015).
- [ ] T063 [US3] Validate `specs/147-execution-evidence-foundation/contracts/execution-evidence.openapi.yaml` with repository-supported OpenAPI linting/YAML parsing and reconcile every operation, status code, scope, cursor, disjoint record shape, reusable `Idempotency-Key` parameter, replay result, and no-mutation mismatch conflict with `tests/Elsa/Workflows/ExecutionEvidence/Api/Tests/ExecutionEvidenceEndpointsTests.cs` (FR-004, FR-015, SC-007).
- [ ] T064 [US3] Run the API suite against authorization/tenant access, malformed/mismatched/deleted cursor, failed/skipped attach, all lifecycle/integrity outcomes, registered-unknown payloads, and both association endpoints' Idempotency-Key bounds/exact replay/lost-acknowledgement/mismatch-conflict behavior; record exact results in `specs/147-execution-evidence-foundation/quickstart.md` (FR-004, FR-014, FR-015, FR-016, FR-019, SC-002, SC-006, SC-007).

**Checkpoint**: The neutral HTTP surface faithfully exposes process-local facts and integrity state, while timeouts/incomplete delivery never claim absence.

---

## Phase 8: User Story 4 — Compose a governed provider-neutral foundation (Priority: P2)

**Goal**: A host explicitly composes base + InMemory + API, while a host that omits all three retains ordinary Runtime behavior with no Evidence registration or allocation path.

**Independent Test**: Compose the enabled and absent in-process shapes; resolve the expected registrations only in the enabled shape and prove source/project directions stay valid.

- [ ] T065 [US4] Add failing enabled/absent in-process composition and registration tests in `tests/Elsa/Architecture/ExecutionEvidenceArchitectureTests.cs` and `tests/Elsa/Workflows/ExecutionEvidence/Tests/ExecutionEvidenceCompositionTests.cs`: for each feature class, construct it, call `ConfigureServices` by repository convention, build the provider, and prepare to resolve every registered service; also assert no Evidence service/setting/serializer/persistence item/type branch/allocation path in the absent shape (FR-001, FR-003, FR-019, SC-001, SC-008).
- [ ] T066 [US4] Finalize public non-sealed base feature registration, replacement-safe virtual `ConfigureServices`, and exact `WorkflowsExecutionEvidence` feature ID in `src/Elsa/Workflows/ExecutionEvidence/WorkflowsExecutionEvidenceFeature.cs` using existing CShells conventions (FR-001, FR-003).
- [ ] T067 [US4] Finalize the explicit process-local leaf feature and exact `WorkflowsExecutionEvidenceInMemory` ID in `src/Elsa/Workflows/ExecutionEvidence/InMemory/WorkflowsExecutionEvidenceInMemoryFeature.cs`; compose it safely with the base, resolve all its registrations, and do not create a provider umbrella or default registration (FR-001, FR-017).
- [ ] T068 [US4] Finalize `WorkflowsExecutionEvidenceApiFeature : WorkflowsExecutionEvidenceFeature` and exact `WorkflowsExecutionEvidenceApi` ID in `src/Elsa/Workflows/ExecutionEvidence/Api/WorkflowsExecutionEvidenceApiFeature.cs`; call `base.ConfigureServices`, retain Core/base-only dependency direction, and make replacement-safe inherited composition executable without InMemory or a peer-service locator (framework §2.5; FR-001, FR-015).
- [ ] T069 [US4] Add direct server references and feature-catalog assembly discovery for base, InMemory, and API only in `src/Apps/Elsa.Server/Elsa.Server.csproj` and `src/Apps/Elsa.Server/Program.cs`; feature-name dependency must not substitute for an undiscoverable assembly reference (FR-001, SC-001).
- [ ] T070 [US4] Add the explicit enabled reference shell entries only after all three feature IDs exist in `src/Apps/Elsa.Server/shells.json` and `src/Apps/Elsa.Server/shells.baseline.json`, preserving an in-process absent-composition test rather than inventing an absent-server e2e claim (FR-001, FR-003, SC-001).
- [ ] T071 [US4] After T066–T068, execute the registration/composition tests in `tests/Elsa/Workflows/ExecutionEvidence/Tests/ExecutionEvidenceCompositionTests.cs`: construct `WorkflowsExecutionEvidence`, `WorkflowsExecutionEvidenceInMemory`, and `WorkflowsExecutionEvidenceApi`, call `ConfigureServices`, build each required provider shape, resolve every registered service, and prove replacement-safe inheritance/composition. Then run four-project project-reference and architecture checks; record the dependency graph and module-absence evidence in `specs/147-execution-evidence-foundation/quickstart.md` (framework §2.5; FR-001, FR-019, SC-001, SC-008).

**Checkpoint**: Composition is explicit and governed; API never implies an InMemory provider, and an uncomposed host performs no Evidence work.

---

## Phase 9: Documentation, e2e, benchmarks, maps, and final verification

**Purpose**: Hand off a truthful, reproducible vertical slice. Generated maps are refreshed only after source inputs change, explicit authorization is obtained, and generated findings are reviewed.

- [ ] T072 [P] Document the base domain, contracts, capture seam, process-local/no-restart limitation, and extension contributions in `src/Elsa/Workflows/ExecutionEvidence/{README.md,EXTENSION_POINTS.md}` and `src/Elsa/Workflows/ExecutionEvidence/InMemory/{README.md,EXTENSION_POINTS.md}` (FR-017, SC-009).
- [ ] T073 Update the API extension catalog and root catalog entry in `src/Elsa/Workflows/ExecutionEvidence/Api/EXTENSION_POINTS.md` and `EXTENSION_POINTS.md`, including the exact host composition and no provider-offset/UI/J-Test claims (FR-015, FR-018, SC-009).
- [ ] T074 Add the explicit server-composition example and validation caveats to `specs/147-execution-evidence-foundation/quickstart.md` and `src/Apps/Elsa.Server/README.md`, including the requirement to launch a separate absent shell before making any absent-server e2e claim (SC-001, SC-009).
- [ ] T075 [P] Create benchmark coverage and a reproducible observation record in `benchmarks/Elsa/Workflows/ExecutionEvidence/Benchmarks/Elsa.Workflows.ExecutionEvidence.Benchmarks.csproj`, `benchmarks/Elsa/Workflows/ExecutionEvidence/Benchmarks/ExecutionEvidenceBenchmarks.cs`, and `specs/147-execution-evidence-foundation/quickstart.md` for absent, enabled-unscoped, enabled-scoped metadata-only, and Prepared-reservation storage/allocation/throughput before and after safe compaction (FR-020, SC-009).
- [ ] T076 Create the enabled-composition REST suite in `e2e-tests/execution-evidence/Test-ExecutionEvidenceFoundation.ps1` and document its input/output expectations in `e2e-tests/execution-evidence/README.md`; cover ordinary workflow execution, open/associate/query/wait/complete/delete, failed/retried attach/start, effective order, cutoff, and six-status integrity (FR-019, SC-002, SC-006).
- [ ] T077 Run the e2e lifecycle exactly as documented in `specs/147-execution-evidence-foundation/quickstart.md`: stop the prior dedicated server and verify port 5095 is free; build while stopped; delete only the exact server SQLite DB, `-wal`/`-shm`, and `*.schema.lock` artifacts; apply the enabled Groundwork schema; launch a dedicated server; wait for HTTP readiness rather than sleeping; execute the PowerShell suite; then stop the server and preserve the DB only for diagnosis (FR-019).
- [ ] T078 Inspect `docs/maps/manifest.json` after all source/docs/ADR inputs are changed, identify the narrow stale maps, and obtain explicit map-refresh authorization before invoking a generator; record the authorization and selected generators in `specs/147-execution-evidence-foundation/quickstart.md` (FR-019, SC-009).
- [ ] T079 After T078 authorization, run only the required generators through `tools/maps/Elsa.Maps.Generator`, review `docs/reports/maps-v1-findings.md` and `docs/reports/maps-v2-findings.md`, confirm the 0063 rename appears in generated references, then run `dotnet run --project tools/maps/Elsa.Maps.Generator -- check` (FR-019, SC-009).
- [ ] T080 Run `dotnet test benchmarks/Elsa/Workflows/ExecutionEvidence/Benchmarks/Elsa.Workflows.ExecutionEvidence.Benchmarks.csproj --filter "FullyQualifiedName~ExecutionEvidence" --logger "console;verbosity=detailed" on an otherwise idle host and record source revision, command, host/hardware, workload, throughput, allocation, and canonical-input storage results in `specs/147-execution-evidence-foundation/quickstart.md` (FR-020, SC-009).
- [ ] T081 Run `git diff --check`, YAML parse/OpenAPI lint, `.specify/scripts/bash/check-prerequisites.sh --json --paths-only`, focused Core/base/InMemory/API/Runtime/Groundwork/architecture suites (including the logic-bearing-class gate), and `dotnet build Elsa.Server.slnx`; record commands/results and any reconciliation of stale tests in `specs/147-execution-evidence-foundation/quickstart.md` (FR-019).
- [ ] T082 Create and execute the final constitution §2.23.1–.2 logic-bearing-class gate in `specs/147-execution-evidence-foundation/logic-bearing-class-test-gate.md` and `tests/Elsa/Architecture/ExecutionEvidenceLogicBearingClassGateTests.cs`: map every new logic-bearing implementation class to direct local stubbed branch tests, fail on zero-unmapped-class violations, and reject shared #1138 conformance fixtures. In the same review, compare the final diff with `specs/147-execution-evidence-foundation/{spec.md,plan.md,research.md,data-model.md,quickstart.md}` and reject Evidence-specific Runtime branches, durability/value/shared-fixture/UI scope creep, unreviewed map changes, missing 28-caller coverage, or any definitive-negative claim (FR-002, FR-017, FR-018, FR-019, SC-001–SC-009).

**Checkpoint**: The enabled vertical slice is reproducibly verified, its process-local limits are candid, generated findings were reviewed, and no out-of-scope follow-on was smuggled in.

---

## Requirement and success-criterion coverage

| Requirement | Covering tasks |
|---|---|
| FR-001 | T004–T006, T009–T014, T062, T065–T071 |
| FR-002 | T015–T030, T048, T082 |
| FR-003 | T031–T037, T053–T054, T065–T071 |
| FR-004 | T031–T037, T059–T064, T076–T077 |
| FR-005 | T043–T044, T047–T048, T061–T064 |
| FR-006 | T023, T045–T048, T053 |
| FR-007 | T043–T046, T059–T064 |
| FR-008 | T009, T043–T044 |
| FR-009 | T017–T027, T047–T049 |
| FR-010 | T019, T024–T029, T049 |
| FR-011 | T049–T052, T055–T056 |
| FR-012 | T038–T042 |
| FR-013 | T031–T037, T039, T055–T056 |
| FR-014 | T038–T042, T055–T058 |
| FR-015 | T012, T057–T064, T073 |
| FR-016 | T055–T058, T064 |
| FR-017 | T004, T011, T051–T052, T067, T072 |
| FR-018 | T043–T044, T059, T073, T082 |
| FR-019 | T014–T016, T030, T042, T064, T071, T076–T082 |
| FR-020 | T075, T080 |

| Success criterion | Covering tasks |
|---|---|
| SC-001 | T004–T006, T009–T014, T062, T065–T071, T082 |
| SC-002 | T031–T037, T059–T064, T076–T077 |
| SC-003 | T015–T030, T047–T048, T053–T054 |
| SC-004 | T017–T030, T047–T049, T053 |
| SC-005 | T031–T037, T049–T052, T055–T056 |
| SC-006 | T038–T042, T055–T056, T064, T076–T077 |
| SC-007 | T043, T057–T064 |
| SC-008 | T014–T015, T043–T046, T065–T071, T082 |
| SC-009 | T072–T082 |

## Dependencies and execution order

```text
Task-stage approval gate
  └─> T001–T003 ADR collision repair
       └─> T004–T007 glossary/E2.1/ADR amendments and exclusion dispositions
            └─> T008 Governance / Architecture-Review Disposition Gate (remain Draft/proposed unless accepted)
                 └─> T009–T014 four project skeleton/dependency proof
                      └─> T015 inventory review
                           └─> T016–T030 generic Runtime provenance/ledger/coalescing
                                └─> T031–T037 fenced start/attach/freeze races
                                     └─> T038–T042 six-status reader
                                          └─> T043–T054 Core/catalog/capture/InMemory
                                               └─> T055–T064 lifecycle/query/API
                                                    └─> T065–T071 host composition
                                                         └─> T072–T082 docs/e2e/benchmarks/maps/verification
```

- Task-stage approval precedes T001. T001 then precedes every other listed task: no second change may reuse the colliding Evidence ADR 0062 path.
- T008 is the distinct Governance / Architecture-Review Disposition Gate. T009 and every code/project/test task remain blocked until it has a recorded passing disposition; a proposed or rejected amendment keeps the affected boundary Draft rather than inferred ratified.
- T015 and its review must finish before *any* task changes `src/Elsa/Workflows/Runtime/`; T016 makes it executable as a no-bypass gate.
- T019 must run enrichers before selecting deferred/immediate persistence; T028/T029 verify and enforce the generic post-enrichment context/post-commit override.
- T024–T027 preserve durable canonical raw recovery input. The reservation may approach a logical checkpoint payload, so T075/T080 measure storage as well as allocation/throughput and report it without a fabricated budget.
- T031–T037 are deliberately before capture implementation: generic Runtime fencing and authoritative Runtime-owned operation receipts/commit results must be reread through Evidence’s opaque reference before any capture adapter or freeze reconciliation relies on them.
- T038–T042 precede lifecycle completion, query wait outcomes, and deletion because all require bounded six-status reconciliation.
- T078/T079 are serialized and occur only after inputs change, authorization is explicit, and generated findings are reviewed. Never hand-edit generated maps.

## Parallel opportunities

- T004, T005, and T006 may run in parallel after T003: they edit the glossary, constitution/program goal, and disjoint ADR files. T007–T008 remain serialized disposition/review work.
- T072 and T075 may run in parallel after T071: they edit disjoint documentation and benchmark paths. T073/T074 serialize their shared root/server documentation changes.
- Do not parallelize Runtime store work, server/solution composition, generated-map work, e2e server lifecycle, or full verification. Each has shared-state or exact-range ordering requirements.

## Implementation strategy

1. **Governance first**: after task-stage approval, make the ADR numbering, glossary wording, and reviewed boundary truthful; T008 is the explicit disposition gate and retains Draft/proposed status where review has not accepted an amendment.
2. **Build Runtime correctness before capture**: complete the inventory, generic durable preparation/replay/order/CAS protocol, all-caller migration, and coalescing recovery. This is the foundation, not an Evidence feature branch.
3. **MVP**: complete User Story 1 through Phase 5, then prove an authorized explicit association is isolated, fenced, and reconcilable before expanding capture.
4. **Incremental capture/API**: land deterministic catalog/capture/InMemory delivery (US2), then completion/query/wait/API (US3), then explicit server composition (US4).
5. **Finish truthfully**: run the enabled e2e against a fresh DB/server, record honest Prepared-reservation benchmark observations, refresh maps only with authorization, review findings, and complete the full verification matrix.

## Final validation commands

```bash
dotnet test tests/Elsa/Workflows/ExecutionEvidence/Core/Tests/Elsa.Workflows.ExecutionEvidence.Core.Tests.csproj
dotnet test tests/Elsa/Workflows/ExecutionEvidence/Tests/Elsa.Workflows.ExecutionEvidence.Tests.csproj
dotnet test tests/Elsa/Workflows/ExecutionEvidence/InMemory/Tests/Elsa.Workflows.ExecutionEvidence.InMemory.Tests.csproj
dotnet test tests/Elsa/Workflows/ExecutionEvidence/Api/Tests/Elsa.Workflows.ExecutionEvidence.Api.Tests.csproj
dotnet test tests/Elsa/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj
dotnet test tests/Elsa/Persistence/Groundwork/Tests/Elsa.Persistence.Groundwork.Tests.csproj
dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj
dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj --filter "FullyQualifiedName~ExecutionEvidenceLogicBearingClassGateTests"
dotnet run --project tools/maps/Elsa.Maps.Generator -- check
dotnet build Elsa.Server.slnx
```

The backend e2e and benchmark commands are intentionally not folded into a single shell command: both require the explicit lifecycle/host evidence recorded by T077 and T080.
