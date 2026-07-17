# Tasks: DispatchWorkflow Parent Audit Remediation

**Input**: [spec.md](spec.md), [plan.md](plan.md), [research.md](research.md), [data-model.md](data-model.md), [contracts/](contracts/), [quickstart.md](quickstart.md)

## Phase 1: Setup and regression surface

- [ ] T001 Record every review finding and chosen convergence rule in `specs/104-dispatch-parent-audit/research.md`
- [ ] T002 Define lifecycle, paging, retention, and API contracts in `specs/104-dispatch-parent-audit/data-model.md` and `specs/104-dispatch-parent-audit/contracts/`
- [ ] T003 Add failing focused tests for final-failure replay, redrive crash/concurrency, resume retry, admission cancellation, conditional retention, multi-page cleanup, API rejection/safety, and bounded provider queries under `tests/Elsa/`

## Phase 2: Crash-safe dispatch work

- [ ] T004 [US1] Make final-failure observation durably replayable in `src/Elsa/Workflows/Runtime/Services/RuntimePostCommitOutboxProcessor.cs` and related outbox models/stores
- [ ] T005 [US1] Make dispatch failure projection independently reconcilable in `src/Elsa/Workflows/Runtime/Services/WorkflowDispatchDeliveryFailureHandler.cs`
- [ ] T006 [US1] Make redrive evidence/state convergence crash-safe and concurrent-request safe in `src/Elsa/Workflows/Runtime/Services/WorkflowDispatchDeliveryFailureHandler.cs`
- [ ] T007 [US1] Apply bounded retry metadata to parent resume and child cancellation work in `src/Elsa/Workflows/Runtime/Services/`
- [ ] T008 [US1] Add Groundwork convergence tests in `tests/Elsa/Workflows/Runtime/Distributed/Persistence/Groundwork/Tests/`

## Phase 3: Lifecycle and bounded progress

- [ ] T009 [US1] Reconcile durable distributed forwarding and admission/cancellation races in `src/Elsa/Activities/DispatchWorkflow/Runtime/Services/ChildStartExecutor.cs` and runtime transition contracts
- [ ] T010 [US2] Add snapshot-conditional retention deletion to `src/Elsa/Workflows/Runtime/Core/Contracts/IWorkflowDispatchDeleteStore.cs` and provider dispatch stores
- [ ] T011 [US2] Add bounded continuation progress to `src/Elsa/Workflows/Runtime/Services/WorkflowDispatchRetentionCollector.cs` and `src/Elsa/Workflows/Runtime/Services/WorkflowDispatchTestRunScopeCleanupService.cs`
- [ ] T012 [US2] Push stable ordering/limits into `src/Elsa/Persistence/Groundwork/Stores/GroundworkWorkflowDispatchStore.cs` and `src/Elsa/Persistence/Groundwork/Stores/GroundworkRuntimePostCommitOutboxStore.cs`
- [ ] T013 [US2] Add greater-than-page-size and provider-bound regression tests under `tests/Elsa/Workflows/Runtime/` and `tests/Elsa/Persistence/Groundwork/`

## Phase 4: Runtime API correctness and safety

- [ ] T014 [US3] Map rejected redrives to the existing API error shape in `src/Elsa/Workflows/Runtime/Api/`
- [ ] T015 [US3] Return safe failure evidence from list and detail inspection without per-record unbounded access in `src/Elsa/Workflows/Runtime/Api/Handlers/WorkflowDispatchRequestHandlers.cs`
- [ ] T016 [US3] Validate failure classifications and derive deterministic identifiers in `src/Elsa/Workflows/Runtime/Api/Models/WorkflowDispatchViews.cs`
- [ ] T017 [US3] Produce and expose safe retry attempt/scheduling evidence required by spec 101 in `src/Elsa/Workflows/Runtime/Core/Models/WorkflowDispatchFailureModels.cs`
- [ ] T018 [US3] Add Runtime API contract, permission, corruption, and list/detail parity tests under `tests/Elsa/Workflows/Runtime/Api/Tests/`

## Phase 5: Acceptance evidence and audit

- [ ] T019 [US4] Add waited TestRun completion/fault/cancellation, run-kind inspection, and published-child selection tests under `tests/Elsa/Activities/DispatchWorkflow/Tests/`
- [ ] T020 [US4] Add an integrated two-node DispatchWorkflow acceptance test in `tests/Elsa/Workflows/Runtime/Distributed/Tests/`
- [ ] T021 [US4] Correct `specs/101-dispatch-redrive-failures/tasks.md`, `specs/102-dispatch-test-run-scope/tasks.md`, and `specs/103-dispatch-distributed-nodes/tasks.md` so every checked claim names existing evidence
- [ ] T022 [US4] Reconcile pre-existing generated-map deltas and manifest/audit wording in `docs/maps/manifest.json` and `docs/reports/dispatch-workflow-674-parent-audit.md` without running map generators
- [ ] T023 [US4] Update `docs/reports/dispatch-workflow-674-parent-audit.md` with final verified evidence

## Phase 6: Verification, review, and delivery

- [ ] T024 Run every focused and provider command in `specs/104-dispatch-parent-audit/quickstart.md`
- [ ] T025 Run architecture tests, full solution tests, and `git diff --check`
- [ ] T026 Run the `self-review-loop` for up to ten iterations and resolve every actionable finding
- [ ] T027 Commit the reviewed remediation, push `codex/dispatch-674-audit`, open a ready PR, and converge automated review feedback
- [ ] T028 Merge the PR into the repository default branch after all required checks are terminal and non-blocking, recording the result in `docs/reports/dispatch-workflow-674-parent-audit.md`
