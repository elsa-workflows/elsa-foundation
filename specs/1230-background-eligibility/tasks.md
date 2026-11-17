# Tasks: Background eligibility

**Input**: [spec.md](spec.md), [plan.md](plan.md), [research.md](research.md), [data-model.md](data-model.md),
[contracts/background-eligibility.md](contracts/background-eligibility.md)

**Tests**: required (constitution §2.23). Test-first where marked: the test fails on today's code before the change.

**Format**: `[ID] [P?] [Story] Description`. `[P]` = can run in parallel (different files, no open dependency).

## Phase 1: Setup

- [x] T001 Merge `origin/initiative/3052-elsa3-to-elsa4-conversion` into the implementation branch and confirm S-10's
  state (PR #4316 merged or not); US6 needs its write history (Q7).

## Phase 2: Foundation (blocks every story)

- [x] T002 Add `BackgroundEligibilityCriterion`, `BackgroundEligibilitySubject` and `BackgroundEligibilityRefusal` in
  `Essentials/src/Workflows/Runtime/Core/Background/BackgroundEligibility.cs` (data-model.md).
- [x] T003 Add `BackgroundEligibility.CheckPublication` and `CheckRunTime` with the ordered criteria C1–C5 taken over
  from `ExecutableNodeCompiler.ValidateBackgroundExecution` (R-1; C3 now also refuses the `Any` and `System.Object`
  aliases, as ADR 0093 C3 and US1 scenario 1 require), the declaration precondition, and the
  rendered message `VF-BG-001 (C<n>): …` (R-5). Add the public API to `PublicAPI.Unshipped.txt`.
- [x] T004 [P] Unit tests in `Essentials/tests/Workflows/Runtime/Tests/Background/BackgroundEligibilityTests.cs`: one
  subject failing exactly one criterion per C1–C5, at both entry points; several failing → the first is reported (P-2).

## Phase 3: User Story 1 — one set of rules (P1) 🎯 MVP

- [x] T005 [US1] **(test-first)** In `WorkflowExecutableCompilerTests.cs`, one publish refusal per C1–C5 asserting the
  criterion id in the message. *Done as:* C1, C3, C4 and C5 there; C2 in `BackgroundEligibilityPublicationTests`.
- [x] T006 [US1] **(test-first)** In `BackgroundActivityWorkerTests.cs`, a pinned node with an `Any` result (C3) and one
  with a transient-resource projection (C4) are refused at run time naming the criterion (today they pass).
- [x] T007 [US1] Replace `ExecutableNodeCompiler.ValidateBackgroundExecution` and the intrinsic/boundary checks at
  `:110-120` with one call to `CheckPublication`; replace `BackgroundActivityEligibility.Validate(node)` and
  `Validate(activity)` in `BackgroundActivityCoordinator` and `BackgroundActivityWorker` with `CheckRunTime`; delete the
  old lists (keep `WorkIdMetadataKey` and `ResultReason` where they are used).
- [x] T007a [US1] Report C6 through the component on the worker (`BackgroundActivityWorker.cs:181-183`), with a test
  that a staged dispatch and a published stimulus each fault naming C6.

## Phase 4: User Story 2 — no variable reader in the background (P1)

- [x] T009 [US2] **(test-first)** A test activity `[BackgroundExecutionSafe]` + `IRuntimeScopedVariableReader`: placed
  in the background, publication refuses naming C9; a pinned node of it is refused at admission naming C9; placed
  inline it publishes and reads its variables. *Done as:* the inline placement publishes; the inline read path is
  unchanged by this spec and not re-run here.
- [x] T010 [US2] Add C9 to the component as one predicate over the CLR type (Q3), so ADR 0090's later signals are one
  change.
- [x] T011 [US2] Check that no shipped activity implementing `IRuntimeScopedVariableReader` is a C1 leaf (today only
  structural activities, e.g. `BpmnProcess`); record the result in research R-4.

## Phase 5: User Story 3 — worker-bound inputs (P2)

- [x] T008 [US3] **(test-first, drive)** Place a background node with a stream input and with a transient-resource
  input; record what happens today (publish, admission or worker) in research R-1 before changing anything.
- [x] T012 [US3] **(test-first)** Publication refuses each C10 case naming C10: no durable representation, not
  materializable on the worker, protection would be lost. *Done as:* publication refuses the stream and the protection
  cases (`BackgroundEligibilityPublicationTests`); no `[ActivityInput]` can declare a non-persistable policy, so that
  case is shown through the component at both entry points (`BackgroundEligibilityTests`).
- [x] T013 [US3] Add C10 to the component, reusing `EnsureSecretBindingsAdmissible` and the snapshot checks (P-4);
  add the input-representation check that is missing. *Done as:* C10 lets a `SecretRead` binding through and checks
  the rest; `EnsureSecretBindingsAdmissible` still runs beside it in the compiler and placer, so a secret binding the
  activity refuses is reported as `VF-ACT-012`, not as C10.
- [x] T014 [P] [US3] A secret reference input on a background node: admitted, resolved at worker activation; assert
  no raw secret in the persisted input snapshot or the work record (SC-005). *Done in*
  `NativeBackgroundActivityFlowchartEfTests.Inputs` on both providers; the EF case also scans the database files.
- [x] T015 [P] [US3] An `Any` input holding JSON is admitted and runs; an `Any` result stays refused by C3. *Done in*
  `NativeBackgroundActivityFlowchartEfTests.Inputs` (worker run, both providers) and `BackgroundEligibilityPublicationTests`.

## Phase 6: User Story 4 — the promise on the attribute (P2)

- [x] T016 [US4] Document C7, C8, the engine-checked criteria, inheritance (Q6) and the two cases not to mark
  (inline-drain results, triggers; Q8) on `BackgroundExecutionSafeAttribute.cs`, pointing to ADR 0093.

## Phase 7: User Story 5 — built-in activities marked (P2)

- [x] T017 [US5] Fill the spec's *Built-in assessment* table from research R-4 and get the approver's confirmation of
  C7 and C8 per marked activity (Q4).
- [x] T018 [US5] Add `[BackgroundExecutionSafe]` to every confirmed activity that passed T022.
- [x] T019 [P] [US5] One end-to-end background run per marked activity (publish, worker, result applied); one refusal
  test per assessed-but-unmarked category, naming its criterion. *Done as:* the five Essentials built-ins publish in
  the background, `WriteLine` and `Fault` run end to end on both persistence modes, every unmarked category is refused
  naming why (`BuiltInBackgroundAssessmentTests`), and guards pin the marked connector set. End-to-end runs of the
  eight connector activities need a connector host with a worker: #4737.
- [x] T020 [US5] Move each marked activity's version per its module's versioning rule (the contract fingerprint
  changes); regenerate the contract surface baselines.
- [x] T022 [US5] **(drive first)** Before marking, check the unknowns of research R-4 with background runs: a connector
  read resolves its connection's tenant partition on a worker; a secret-bound input resolves on the
  worker with no raw secret persisted; a `DownloadFile` result above the receipt limit faults with `BG-ACT-006`. An
  activity that fails gets an issue instead of the mark (Q10). *Done as:* the secret-bound input by a worker run
  (T014), the receipt limit by a worker run (`BackgroundActivityWorkerTests.Faults_a_result_above_the_receipt_size_limit`),
  the connector tenant partition by reading the code; background runs against connector endpoints: #4737.
- [x] T023 [US1] **(test-first)** Call `CheckPublication` in `ActivityTemplatePlacer` for every background node a
  template provider returns (FR-012, Q9). Tests: a template node that fails a criterion is refused naming it; the
  agent's model-turn node (`AgentActivityProvider.CompileAsync`) passes.
- [x] T021 [US5] File one issue per candidate that falls short only because of its implementation (ADR 0093 decision 4):
  M365 `CreateEvent`, M365 `UploadFile`, Slack `AddReaction`, `EvaluateDecision`, and any T022 failure; as sub-issues
  of #3052, linked from the assessment table.

## Phase 8: User Story 6 — causal order on every provider (P3)

- [x] T024 [US6] **(test)** A background step followed by a dependent step: the dependent step runs after the result
  is applied, on the in-memory and the EF Core provider. *Done in* `NativeBackgroundActivityFlowchartEfTests.CausalOrder`:
  the dependent step reads the captured result, and its first commit sequence follows the result's.
- [x] T025 [US6] **(test)** Two unordered background branches writing one variable: last commit wins; the write
  history (S-10) and the checkpoint sequence give the actual commit order, on both providers. *Done in*
  `NativeBackgroundActivityFlowchartEfTests.CausalOrder`, in both completion orders.
- [x] T026 [US6] **(test)** A background result application is atomic: a fault injected during the result commit
  leaves no captured output and no next step. *Done in* `NativeBackgroundActivityFlowchartEfTests.CausalOrder`; the
  result stays `ResultReady` and the failure surfaces as a `SchedulerWorkPoisoned` incident for repair.

## Phase 9: Polish

- [x] T028 [P] Point spec 241's `contracts/background-activity.md` to ADR 0093 and this contract (FR-011).
- [x] T029 [P] Update the background activities documentation and `EXTENSION_POINTS.md` where they list the attribute
  or the eligibility rules.
- [x] T030 Release note: pinned background nodes of custom activities that now fail C3, C4, C9 or C10 are refused at
  run time (Q5).
- [x] T031 Set **Status** to `Implemented` in the last implementation PR.

## Dependencies

- T002–T004 block every story. US1 (T005–T007a) before US2 and US3, which add criteria to the same component.
- T023 (agent template) needs T003 only and can run with US1.
- US4 is independent. US5 needs US1–US3 (the refusals its tests assert) and the approver's C7/C8 confirmation.
- US6 needs S-10's write history (PR #4316) on the initiative branch.

## Implementation status (2026-11-17, PR #4725)

- **Done:** every task. US6 landed after S-10's PR #4316 merged into the initiative branch. Findings in research
  *Implementation findings*.
- **Recorded limits:**
  - SC-002: every criterion is refused at both entry points in the component tests (`BackgroundEligibilityTests`).
    Through the real call sites: C1–C5, C9 and C10 at publication and at template placement, C1–C4 and C6 on the
    worker, and C9 at admission. The other combinations share those call sites and are shown through the component.
  - T019 and T022 for the eight connector activities: their background runs need a connector host with a worker, so
    their marks rest on reading the code (Q10). Follow-up: #4737.
