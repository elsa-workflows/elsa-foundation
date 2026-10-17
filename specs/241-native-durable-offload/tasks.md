# Tasks: Native durable background activities

Input: [plan.md](plan.md), [spec.md](spec.md), [data-model.md](data-model.md), [contracts](contracts/background-activity.md). Tracking: #2914. All P1 stories are mandatory for the active goal.

## Phase 1: Setup and design

- [x] T001 Ground the native/EF baseline and record the source decisions in `specs/241-native-durable-offload/research.md`.
- [x] T002 Write and review the specification, acceptance matrix and contracts in `specs/241-native-durable-offload/`.
- [x] T003 Record active ownership and implementation progress in `docs/program-goals/runtime-execution-seam.md` and GitHub #2914/#2892.

## Phase 2: Shared durability contracts

- [x] T004 Add provider-neutral background request/claim/result/lifecycle contracts in `Essentials/src/Workflows/Runtime/Core/Models/BackgroundActivityWork.cs` and `Contracts/IBackgroundActivityWorkStore.cs`.
- [x] T005 Add explicit admission/acceptance/cancellation participants and preserve them through state-set copies, validation, fingerprinting and coalescing in `Essentials/src/Workflows/Runtime/Core/Models/RuntimeCheckpointCommit.cs` and `Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/Services/Checkpoints/`.
- [x] T006 Implement shared lifecycle validation and in-memory participant parity in `Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/Services/` with focused contract tests under `Essentials/tests/Workflows/Runtime/`.

## Phase 3: US1 - Concurrent native leaves (P1)

Independent check: publish Sequence → Flowchart with two background leaves → final step; hold both workers before either finishes, release in both orders, observe one final continuation.

- [x] T007 [P] [US1] Add fingerprinted background-safe declaration and placement fields in `Essentials/src/Activities/Runtime/Core/`, `Essentials/src/Workflows/Design/Core/Models/ActivityNode.cs` and `Essentials/src/Workflows/Runtime/Core/Models/ExecutableNode.cs`.
- [x] T008 [US1] Compile/validate placement and hash nondefault behavior in `Essentials/src/Workflows/Publishing/` and `Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/Services/Executables/WorkflowExecutableHasher.cs`; cover legacy and invalid artifacts in affected publishing/runtime tests.
- [x] T009 [P] [US1] Add EF background entity, schema/configuration, scoped store and checkpoint admission/acceptance staging under `Essentials/src/Workflows/Runtime/Persistence/EntityFrameworkCore/`; add provider migration/model tests.
- [x] T010 [US1] Admit background invocations atomically without body activation in `Essentials/src/Activities/Runtime/Elsa.Activities.Runtime/Services/WorkflowInvokeActivitySchedulerWorkHandler.cs` and reuse prepared logical attempt logic in `ActivityAttemptActivationClaimer.cs`.
- [x] T011 [US1] Execute claimed leaves in isolated scopes with typed result/fault serialization in `Essentials/src/Activities/Runtime/Elsa.Activities.Runtime/Services/BackgroundActivityWorker.cs`.
- [x] T012 [US1] Add typed result reconstruction and native completion reuse/atomic receipt acceptance under `Essentials/src/Activities/Runtime/Elsa.Activities.Runtime/Services/`.
- [x] T013 [US1] Add bounded opt-in worker pump/registration using Elsa tasks under `Essentials/src/Activities/Runtime/Elsa.Activities.Runtime/` and test lifecycle/options validation.
- [x] T014 [US1] Prove overlap, both completion orders, outputs and one native continuation in real EF integration tests under `Essentials/tests/Activities/Http/IntegrationTests/` or the existing Runtime EF integration harness.

## Phase 4: US2 - Durable recovery and replay (P1)

Independent check: reopen fresh providers/processes against the same EF database at each admission/claim/receipt/application boundary.

- [x] T015 [US2] Implement fenced expiring claim/renew/result operations and exact-retry/conflict rules in `Essentials/src/Workflows/Runtime/Persistence/EntityFrameworkCore/Stores/EfBackgroundActivityWorkStore.cs`.
- [x] T016 [US2] Repair lost receipt wakeups with a bounded ResultReady sweep in `Essentials/src/Activities/Runtime/Elsa.Activities.Runtime/Services/BackgroundActivityWorker.cs` and its pump.
- [x] T017 [P] [US2] Resolve late native completion replay without suppressing the first callback in `Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/Services/WorkHandlers/WorkflowCompleteActivitySchedulerWorkHandler.cs` and affected activity completion helpers; add delayed replay/loop/fork tests.
- [x] T018 [US2] Test rollback, lost response, expired/stale claim, conflicting receipt and competing coordinator fence in `Essentials/tests/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/BackgroundActivityWorkTests.cs`.
- [x] T019 [US2] Add a real owned-process restart witness with `Essentials/tests/Workbench/Tests/WorkbenchProcess.cs` (the C# counterpart of `e2e-tests/_ServerLifecycle.ps1`); rebuild Workbench and record recovery evidence.

## Phase 5: US3 - Explicit cancellation (P1)

Independent check: cancellation before claim/during work/between results/before application/after completion, followed by restart and late receipt delivery.

- [x] T020 [US3] Stage workflow/subtree background cancellation in the native cancellation checkpoint path under `Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/Services/` and atomically retain provider tombstones.
- [x] T021 [US3] Link worker cooperative cancellation to claim loss/explicit cancellation while keeping host shutdown reclaimable in `Essentials/src/Activities/Runtime/Elsa.Activities.Runtime/Services/BackgroundActivityWorker.cs`.
- [x] T022 [US3] Cover the race table and subtree isolation in EF/native tests and the owned Workbench process witness.

## Phase 6: US4 - Eligibility, diagnostics and operational use (P2)

Independent check: invalid scope/pins/types cannot execute; native incidents/inspection explain terminal work; feature composition/rollback documented.

- [x] T023 [US4] Enforce artifact/contract/input/attempt/scope validation and value-policy-safe fault/result handling across worker/store/coordinator under `Essentials/src/Activities/Runtime/Elsa.Activities.Runtime/Services/`.
- [x] T024 [US4] Test returned Fault/Cancel, thrown/disposal errors, unsupported transitions, invalid scopes/pins and unavailable types in affected activity and EF suites.
- [x] T025 [US4] Document feature settings, eligibility, schema migration, diagnostics, idempotency, pause/drain and rollback in `docs/reference/background-activity-execution.md` and affected `EXTENSION_POINTS.md` files.

## Phase 7: Integrated verification and delivery

- [x] T026 Amend serialized-orchestration wording in `docs/adr/0031-runtime-burst-execution-sticky-single-writer-drain-with-in-process-fast-path.md` with links to the proven native boundary.
- [x] T027 Run meaningful fencing, cancellation, receipt atomicity/dedup, pin/scope and continuation replay mutations; record failing controls and restored green results in `specs/241-native-durable-offload/quickstart.md`.
- [x] T028 Run affected suites, architecture guard and generated-map check; refresh changed maps with manifest as needed under `docs/maps/` and record unrelated main #2293 separately.
- [x] T029 Independently review integrated code and the actual Workbench journey, resolve findings, and record final evidence in `specs/241-native-durable-offload/quickstart.md`.
- [x] T030 Commit reviewed work, open/attach one scoped draft PR for #2914, and update issue/program evidence plus `specs/241-native-durable-offload/tasks.md`; leave unrun gates explicitly pending.

## Verification state

The native proof is delivered in draft PR [#2915](https://github.com/elsa-workflows/elsa-foundation/pull/2915). The rebuilt Workbench passes both owned-process recovery orders across admission, body, receipt-before-notification, and terminal-history boundaries, plus explicit REST cancellation across restart. Full activity runtime (485), workflow runtime (2,338), publishing (776), Sequence (22), Flowchart (121), Runtime EF (969), and architecture (576) suites pass. Publication placement, renewal-response races, coalesced first admission, and EF/in-memory deletion parity fixes are integrated. All recorded negative controls fail with their safeguard removed and pass after restoration. Exact revisions, commands, freshness results, review findings and limits are recorded in [quickstart.md](quickstart.md). The specification remains Approved until merge; remote CI and merge readiness are separate from this completed local proof.

## Dependencies and parallel lanes

T001–T003 precede implementation. T004–T006 establish contracts before worker/native integration. T007–T008 are independent from EF implementation once naming is coordinated; T009/T015/T018 share the persistence owner. Root owns T010–T013/T016/T020–T021. The separate verification lane owns T017, then T014/T019/T022/T024 once interfaces are integrated. US2/US3 are developed incrementally alongside US1; the goal is not complete until all three are proven. US4 is part of usable delivery, not optional hardening.

Use isolated worktrees and bounded commits per lane. Tests exercise behavior, not implementation shape. No throughput benchmark is planned. Root performs integration, affected checks, independent review resolution and final evidence. Local commits do not imply merge, deployment or production readiness beyond the recorded proof.
