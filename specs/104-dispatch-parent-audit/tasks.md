# Tasks: DispatchWorkflow Parent Audit Remediation

**Input**: [spec.md](spec.md), [plan.md](plan.md), [research.md](research.md), [data-model.md](data-model.md), [contracts/](contracts/), [quickstart.md](quickstart.md)

## Phase 1: Setup and regression surface

- [x] T001 Record every review finding and chosen convergence rule in `specs/104-dispatch-parent-audit/research.md`
- [x] T002 Define lifecycle, paging, retention, and API contracts in `specs/104-dispatch-parent-audit/data-model.md` and `specs/104-dispatch-parent-audit/contracts/`
- [x] T003 Add failing focused tests for final-failure replay, redrive crash/concurrency, resume retry, admission cancellation, conditional retention, multi-page cleanup, API rejection/safety, and bounded provider queries under `tests/Elsa/`

## Phase 2: Crash-safe dispatch work

- [x] T004 [US1] Make final-failure observation durably replayable in `src/Elsa/Workflows/Runtime/Services/RuntimePostCommitOutboxProcessor.cs` and related outbox models/stores
- [x] T005 [US1] Verify atomic dispatch-failure projection in `src/Elsa/Activities/DispatchWorkflow/Runtime/Services/WorkflowDispatchDeliveryFailureProjector.cs`
- [x] T006 [US1] Verify provider-atomic redrive evidence/state convergence in the in-memory checkpoint store and `src/Elsa/Persistence/Groundwork/Stores/GroundworkRuntimePostCommitOutboxStore.cs`
- [x] T007 [US1] Apply bounded retry metadata to parent resume and child cancellation work in `src/Elsa/Workflows/Runtime/Services/`
- [x] T008 [US1] Add Groundwork convergence tests in `tests/Elsa/Workflows/Runtime/Distributed/Persistence/Groundwork/Tests/`

## Phase 3: Lifecycle and bounded progress

- [x] T009 [US1] Reconcile durable distributed forwarding and admission/cancellation races in `src/Elsa/Activities/DispatchWorkflow/Runtime/Services/ChildStartExecutor.cs` and runtime transition contracts
- [x] T010 [US2] Verify snapshot-conditional retention deletion through `IWorkflowDispatchDeleteStore` and provider dispatch stores
- [x] T011 [US2] Verify bounded continuation progress in `src/Elsa/Workflows/Runtime/Services/WorkflowDispatchRetentionCollector.cs` and provider test-scope cleanup stores
- [x] T012 [US2] Push stable ordering/limits into `src/Elsa/Persistence/Groundwork/Stores/GroundworkWorkflowDispatchStore.cs` and `src/Elsa/Persistence/Groundwork/Stores/GroundworkRuntimePostCommitOutboxStore.cs`
- [x] T013 [US2] Add greater-than-page-size and provider-bound regression tests under `tests/Elsa/Workflows/Runtime/` and `tests/Elsa/Persistence/Groundwork/`

## Phase 4: Runtime API correctness and safety

- [x] T014 [US3] Preserve the safe redrive disposition contract in `src/Elsa/Workflows/Runtime/Api/`
- [x] T015 [US3] Return safe failure evidence from list and detail inspection without per-record unbounded access in `src/Elsa/Workflows/Runtime/Api/Handlers/WorkflowDispatchInspectionRequestHandlers.cs`
- [x] T016 [US3] Validate deterministic failure identifiers before exposing them from `src/Elsa/Workflows/Runtime/Api/Models/WorkflowDispatchViews.cs`
- [x] T017 [US3] Verify safe retry attempt/scheduling evidence produced by `src/Elsa/Workflows/Runtime/Services/RuntimePostCommitOutboxProcessor.cs`
- [x] T018 [US3] Add Runtime API contract, permission, corruption, and list/detail parity tests under `tests/Elsa/Workflows/Runtime/Api/Tests/`

## Phase 5: Acceptance evidence and audit

- [x] T019 [US4] Add waited TestRun completion/fault/cancellation, run-kind inspection, and published-child selection tests under `tests/Elsa/Activities/DispatchWorkflow/Tests/`
- [x] T020 [US4] Add an integrated two-node DispatchWorkflow acceptance test in `tests/Elsa/Workflows/Runtime/Distributed/Tests/`
- [x] T021 [US4] Correct `specs/101-dispatch-delivery-recovery/tasks.md`, `specs/102-dispatch-test-run-scope/tasks.md`, and `specs/103-dispatch-distributed-execution/tasks.md` so every checked claim names existing evidence
- [x] T022 [US4] Record the pre-existing generated-map freshness exception in `docs/reports/dispatch-workflow-674-parent-audit.md` without changing generated snapshots or running map generators
- [x] T023 [US4] Update `docs/reports/dispatch-workflow-674-parent-audit.md` with final verified evidence

## Phase 6: Verification, review, and delivery

- [x] T024 Run every focused and provider command in `specs/104-dispatch-parent-audit/quickstart.md`
- [x] T025 Run architecture tests, full solution tests, and `git diff --check`
- [x] T026 Run the `self-review-loop` for up to ten iterations and resolve every actionable finding
- [x] T027 Commit the reviewed remediation locally

Remote delivery (push, ready PR, automated-review convergence, and merge) follows this implementation ledger under the user's explicit authorization; it is recorded in the final handoff because those actions occur after the branch contents are finalized.
