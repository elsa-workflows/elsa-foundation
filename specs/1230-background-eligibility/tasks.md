# Tasks: Background eligibility

**Input**: [spec.md](spec.md), [plan.md](plan.md), [research.md](research.md), [data-model.md](data-model.md),
[contracts/background-eligibility.md](contracts/background-eligibility.md)

**Tests**: required (constitution §2.23). Test-first where marked: the test fails on today's code before the change.

**Format**: `[ID] [P?] [Story] Description`. `[P]` = can run in parallel (different files, no open dependency).

## Phase 1: Setup

- [ ] T001 Merge `origin/initiative/3052-elsa3-to-elsa4-conversion` into the implementation branch and confirm S-10's
  state (PR #4316 merged or not); US6 needs its write history (Q7).

## Phase 2: Foundation (blocks every story)

- [ ] T002 Add `BackgroundEligibilityCriterion`, `BackgroundEligibilitySubject` and `BackgroundEligibilityRefusal` in
  `Essentials/src/Workflows/Runtime/Core/Background/BackgroundEligibility.cs` (data-model.md).
- [ ] T003 Add `BackgroundEligibility.CheckPublication` and `CheckRunTime` with the ordered criteria C1–C5 taken over
  unchanged from `ExecutableNodeCompiler.ValidateBackgroundExecution` (R-1), the declaration precondition, and the
  rendered message `VF-BG-001 (C<n>): …` (R-5). Add the public API to `PublicAPI.Unshipped.txt`.
- [ ] T004 [P] Unit tests in `Essentials/tests/Workflows/Runtime/Tests/Background/BackgroundEligibilityTests.cs`: one
  subject failing exactly one criterion per C1–C5, at both entry points; several failing → the first is reported (P-2).

## Phase 3: User Story 1 — one set of rules (P1) 🎯 MVP

- [ ] T005 [US1] **(test-first)** In `WorkflowExecutableCompilerTests.cs`, one publish refusal per C1–C5 asserting the
  criterion id in the message.
- [ ] T006 [US1] **(test-first)** In `BackgroundActivityWorkerTests.cs`, a pinned node with an `Any` result (C3) and one
  with a transient-resource projection (C4) are refused at run time naming the criterion (today they pass).
- [ ] T007 [US1] Replace `ExecutableNodeCompiler.ValidateBackgroundExecution` and the intrinsic/boundary checks at
  `:110-120` with one call to `CheckPublication`; replace `BackgroundActivityEligibility.Validate(node)` and
  `Validate(activity)` in `BackgroundActivityCoordinator` and `BackgroundActivityWorker` with `CheckRunTime`; delete the
  old lists (keep `WorkIdMetadataKey` and `ResultReason` where they are used).
- [ ] T007a [US1] Report C6 through the component on the worker (`BackgroundActivityWorker.cs:181-183`), with a test
  that a staged dispatch and a published stimulus each fault naming C6.

## Phase 4: User Story 2 — no variable reader in the background (P1)

- [ ] T009 [US2] **(test-first)** A test activity `[BackgroundExecutionSafe]` + `IRuntimeScopedVariableReader`: placed
  in the background, publication refuses naming C9; a pinned node of it is refused at admission naming C9; placed
  inline it publishes and reads its variables.
- [ ] T010 [US2] Add C9 to the component as one predicate over the CLR type (Q3), so ADR 0090's later signals are one
  change.
- [ ] T011 [US2] Check that no shipped activity implementing `IRuntimeScopedVariableReader` is a C1 leaf (today only
  structural activities, e.g. `BpmnProcess`); record the result in research R-4.

## Phase 5: User Story 3 — worker-bound inputs (P2)

- [ ] T008 [US3] **(test-first, drive)** Place a background node with a stream input and with a transient-resource
  input; record what happens today (publish, admission or worker) in research R-1 before changing anything.
- [ ] T012 [US3] **(test-first)** Publication refuses each C10 case naming C10: no durable representation, not
  materializable on the worker, protection would be lost.
- [ ] T013 [US3] Add C10 to the component, reusing `EnsureSecretBindingsAdmissible` and the snapshot checks (P-4);
  add the input-representation check that is missing.
- [ ] T014 [P] [US3] A secret reference input on a background node: admitted, resolved at worker activation; assert
  no raw secret in the persisted input snapshot or the work record (SC-005).
- [ ] T015 [P] [US3] An `Any` input holding JSON is admitted and runs; an `Any` result stays refused by C3.

## Phase 6: User Story 4 — the promise on the attribute (P2)

- [ ] T016 [US4] Document C7, C8, the engine-checked criteria and inheritance (Q6) on
  `BackgroundExecutionSafeAttribute.cs`, pointing to ADR 0093.

## Phase 7: User Story 5 — built-in activities marked (P2)

- [ ] T017 [US5] Fill the spec's *Built-in assessment* table from research R-4 and get the approver's confirmation of
  C7 and C8 per marked activity (Q4).
- [ ] T018 [US5] Add `[BackgroundExecutionSafe]` to every confirmed activity.
- [ ] T019 [P] [US5] One end-to-end background run per marked activity (publish, worker, result applied); one refusal
  test per assessed-but-unmarked category, naming its criterion.
- [ ] T020 [US5] Move each marked activity's version per its module's versioning rule (the contract fingerprint
  changes); regenerate the contract surface baselines.
- [ ] T021 [US5] File one issue per candidate that falls short only because of its implementation (ADR 0093 decision 4),
  as sub-issues of #3052, and link them from the assessment table.

## Phase 8: User Story 6 — causal order on every provider (P3)

- [ ] T024 [US6] **(test)** A background step followed by a dependent step: the dependent step runs after the result
  is applied, on the in-memory and the EF Core provider.
- [ ] T025 [US6] **(test)** Two unordered background branches writing one variable: last commit wins; the write
  history (S-10) and the checkpoint sequence give the actual commit order, on both providers.
- [ ] T026 [US6] **(test)** A background result application is atomic: a fault injected during the result commit
  leaves no captured output and no next step.

## Phase 9: Polish

- [ ] T028 [P] Point spec 241's `contracts/background-activity.md` to ADR 0093 and this contract (FR-011).
- [ ] T029 [P] Update the background activities documentation and `EXTENSION_POINTS.md` where they list the attribute
  or the eligibility rules.
- [ ] T030 Release note: pinned background nodes of custom activities that now fail C3, C4, C9 or C10 are refused at
  run time (Q5).
- [ ] T031 Set **Status** to `Implemented` in the last implementation PR.

## Dependencies

- T002–T004 block every story. US1 (T005–T007a) before US2 and US3, which add criteria to the same component.
- US4 is independent. US5 needs US1–US3 (the refusals its tests assert) and the approver's C7/C8 confirmation.
- US6 needs S-10's write history (PR #4316) on the initiative branch.
