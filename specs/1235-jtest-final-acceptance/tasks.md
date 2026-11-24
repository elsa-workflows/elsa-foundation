# Tasks: Final JTest acceptance for Elsa 3 to Elsa 4 conversion

**Input**: [spec.md](spec.md), [plan.md](plan.md), [research.md](research.md), [data-model.md](data-model.md),
[contracts/](contracts/), [quickstart.md](quickstart.md).

**Suite root**: `extensions/Elsa3/tests/JTest/` (written `JTest/` below). **Corpus**: the JTest-Nexxbiz repository at
commit `b1cde13`.

**Tests**: The JTest suites are the tests. The one production change (T004, T005) adds a project reference and a
feature-catalog entry with no logic; the existing feature-composition and Elsa 3 boundary tests cover it, so no new
unit test is required (framework §2.23). No test cadence beyond that applies.

**Approval gates**: T016 (baseline failure list), T029 and T036 (suite changes and adjustments) need the coordinator's
recorded approval before the tasks after them start.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: US1 to US4 from spec.md

---

## Phase 1: Setup

- [ ] T001 Copy the corpus at `b1cde13` unchanged into `JTest/`: `workflow-definitions/`, `tests/`, `templates/`,
  `environments/`, `test-files/`, `global-configuration.json`, `import-and-publish-workflows.json`; leave out
  `pipelines/` and the repository README. Commit this copy alone, with the source commit in the message (FR-001).
- [ ] T002 Check `JTest/` for secrets and line endings: no credential values (environment files hold only
  `${VARIABLE}` references), and add a `.gitattributes` entry if needed so the JSON files keep their original bytes
  (FR-004).
- [ ] T003 [P] Create the ledger files with their table headers from data-model.md: `JTest/ledger/suites.md` (one row
  per suite, all `not run`), `JTest/ledger/baseline-failures.md`, `JTest/ledger/suite-changes.md`,
  `JTest/ledger/dispositions.md` (one row per definition, bucket `unclassified`), `JTest/ledger/runs.md`.

---

## Phase 2: Foundational (Elsa 4 host composition)

- [ ] T004 Add a `ProjectReference` to the Elsa 3 reconciliation project
  (`extensions/Elsa3/src/Activities/Design/Import/Reconciliation/`) in `apps/Elsa.Workbench/Elsa.Workbench.csproj`.
- [ ] T005 Add `typeof(Elsa3JsonReconciliationFeature).Assembly` to the Elsa 3 feature assemblies in
  `apps/Elsa.Workbench/Program.cs`, next to the four existing Elsa 3 entries; leave the feature disabled in
  `apps/Elsa.Workbench/shells.json`.
- [ ] T006 Build the Workbench and run the existing architecture tests that cover feature composition and the Elsa 3
  boundary (`Essentials/tests/Architecture/Tests/`); regenerate committed solution filters only if the maps check says
  they changed.
- [ ] T007 Find and verify the configuration override that enables `Elsa3JsonReconciliation` (`SourceId`, `Files` or
  `FolderPath`), sets `WorkflowsRuntimeCheckpointPersistence` to `Durable` and turns on value and variable payload
  capture for one Workbench run without editing the committed `shells.json` (environment variables or a run-specific
  shells file, R-5). Record the exact commands in `JTest/README.md`.

**Checkpoint**: the Workbench starts with the reconciliation enabled for a run and logs its summary.

---

## Phase 3: User Story 1 - A green Elsa 3 baseline (Priority: P1)

**Goal**: Every suite passes on Elsa 3.6.3, or is blocked by a named 3.6.3 defect, with approved suite fixes.

**Independent test**: run the corpus against the 3.6.3 container and read `JTest/ledger/suites.md`.

- [ ] T008 [US1] Build the baseline image from elsa-core tag `3.6.3` (`docker/ElsaServer.Dockerfile`) as in
  quickstart.md, find its development admin credentials, and record the build command and image digest in
  `JTest/README.md` (FR-006).
- [ ] T009 [US1] Install or update `JTest.Cli` to the latest release and run
  `jtest validate "tests/**/*"` from `JTest/`; record the version and any invalid file in `JTest/ledger/runs.md`.
- [ ] T010 [US1] Start a fresh baseline container, run `import-and-publish-workflows.json`, and record its result per
  dataset in `JTest/ledger/runs.md`; a definition that fails to import on 3.6.3 is itself a baseline failure.
- [ ] T011 [US1] Run all 42 suites on the same container; record the run in `JTest/ledger/runs.md` and the Elsa 3
  result per suite in `JTest/ledger/suites.md`.
- [ ] T012 [US1] Rerun every failing suite on a fresh container to separate intermittent failures from stable ones;
  keep both runs in `JTest/ledger/runs.md`.
- [ ] T013 [US1] For each failing assertion, add a row to `JTest/ledger/baseline-failures.md` with observed and expected
  values and the suite's description (FR-007).
- [ ] T014 [US1] Classify each row as suite defect, corrected behaviour or 3.6.3 defect, with evidence (suite
  description, elsa-core 3.6.3 source or release notes, a minimal reproduction) and the proposed fix or `none
  (blocked)`.
- [ ] T015 [US1] Check the If and Switch suites (`tests/branching/if-then-else-tests.json`, `switch-*`,
  `switchflow-*`) are present and cover the corpus's If, Switch and FlowSwitch activities; record any activity the
  corpus definitions use that no suite covers as a finding in `JTest/ledger/baseline-failures.md` notes.
- [ ] T016 [US1] **Approval gate**: post the classified list on the PR and get the coordinator's approval of the whole
  list; record the approval link in `JTest/ledger/baseline-failures.md`.
- [ ] T017 [US1] Apply the approved fixes to the suite and template files under `JTest/tests/` and `JTest/templates/`,
  one commit, with the ledger row named per change; mark 3.6.3-defect suites `blocked: <defect>` in
  `JTest/ledger/suites.md` (FR-008).
- [ ] T018 [US1] Rerun all suites on a fresh container and confirm every unblocked suite passes (SC-001); record the
  run.

**Checkpoint**: green Elsa 3 baseline.

---

## Phase 4: User Story 2 - Engine-neutral suites (Priority: P1)

**Goal**: suite files carry no Elsa 3-specific values; they still pass on 3.6.3.

**Independent test**: Elsa 3 rerun green; the diff of each suite shows only values moved behind parameters.

- [ ] T019 [US2] List every Elsa 3-specific value in `JTest/tests/**` that the template contract cannot absorb (API
  paths outside templates, response fields read directly from `http` steps, status words the translation table does
  not cover, Elsa 3 generated identifiers); add the list to `JTest/ledger/suite-changes.md` as the refactor scope.
- [ ] T020 [US2] For each Elsa 4 template that needs a parameter its Elsa 3 version lacks (for example the workflow
  instance id for `elsa-activity-instance-get`, contracts/templates.md), add the parameter to the Elsa 3 template in
  `JTest/templates/` without changing its outputs.
- [ ] T021 [US2] Move each listed value behind a template output, an environment value in `JTest/environments/dev.json`
  or a suite parameter, and pass any new template parameters, in `JTest/tests/**`; no expected business value or
  assertion changes (FR-010).
- [ ] T022 [US2] Rerun all suites on a fresh 3.6.3 container; the same suites pass as after T018 (SC-002 part 1);
  record the run.

**Checkpoint**: the suite files are frozen for the Elsa 4 work; later edits go through T029.

---

## Phase 5: User Story 3 - Converted definitions pass on Elsa 4 (Priority: P1)

**Goal**: the Elsa 4 template set and run; every suite not blocked by JOIN or an approved incompatibility passes.

**Independent test**: fresh Workbench with reconciliation, `jtest run` with the Elsa 4 configuration, ledger read.

- [ ] T023 [P] [US3] Create `JTest/environments/elsa4.json` (same keys as `dev.json`, executor base
  `{base}/workflows/http`) and `JTest/global-configuration.elsa4.json` (template search path `templates-elsa4/*`)
  per contracts/run-hosts.md.
- [ ] T024 [P] [US3] Write `templates-elsa4/elsa-api-login-template.json`: `elsa-authenticate` per
  contracts/templates.md.
- [ ] T025 [P] [US3] Write `templates-elsa4/elsa-api-workflow-definition-templates.json` and
  `templates-elsa4/elsa-api-composite-templates.json`: import as lookup, publish as published-check, delete, and the
  composite, per contracts/templates.md.
- [ ] T026 [P] [US3] Write `templates-elsa4/elsa-api-workflow-instance-templates.json`: get, search, not-exists and
  variable value, with the status and version translations.
- [ ] T027 [P] [US3] Write `templates-elsa4/elsa-api-activity-instance-templates.json`: the eight
  `elsa-activity-instance-*` templates, with node-id suffix matching and execution-list counts.
- [ ] T028 [US3] Start a fresh Workbench with the T007 override on the HTTP definition (`http-endpoints.json`), run the
  six `tests/http/*` suites with the Elsa 4 configuration, and fix templates until they pass or a failure is shown to
  be an engine or conversion result; record the run and receipt.
- [ ] T029 [US3] **Approval gate** for suite edits: any change to a suite file needed on Elsa 4 (including a raised
  `wait`) is added to `JTest/ledger/suite-changes.md` with its reason, approved by the coordinator, and verified on
  3.6.3 before it lands (FR-012).
- [ ] T030 [US3] Run `tests/single-outcome/single-outcome-tests.json` the same way; record the run.
- [ ] T031 [US3] Start a fresh Workbench with all 36 definitions; copy the reconciliation receipt into
  `JTest/ledger/runs.md` and fill the Reconciliation column of `JTest/ledger/dispositions.md`.
- [ ] T032 [US3] Run all 42 suites on that host; record the Elsa 4 result per suite in `JTest/ledger/suites.md`.
- [ ] T033 [US3] For each failing suite, find the cause (template, converter, engine, prerequisite) and record it; a
  template cause is fixed in `templates-elsa4/` and rerun.
- [ ] T034 [US3] For a converter or engine cause: if the definition cannot convert faithfully, confirm it is refused
  with guidance and bucket it `incompatible`; if a genuine Elsa 4 gap or converter defect, file a `needs-triage` issue
  (sub-issue of the initiative) and record it; never add a converter workaround (FR-017).
- [ ] T035 [US3] For each suite whose definition contains a join, decide from the failure whether the join is what stops
  it; mark `blocked: JOIN` with the construct and failure only then (FR-018), and keep the rest in the main run.
- [ ] T036 [US3] **Approval gate** for adjustments: for a definition that needs an adjusted copy, add it under
  `JTest/adjusted/` with the exact change and reason in `JTest/ledger/dispositions.md`, get the coordinator's
  approval, and run the Elsa 4 host with the adjusted copy in place of the original via the reconciliation `Files`
  list (FR-014, FR-015).
- [ ] T037 [US3] Bucket every definition in `JTest/ledger/dispositions.md` (SC-003).
- [ ] T038 [US3] Once spec 1207 is merged into the initiative branch, merge the initiative branch into this branch and
  make the qualifying run: all suites on a fresh Workbench from that head; record it as the non-JOIN milestone run
  (FR-020, SC-004).

**Checkpoint**: non-JOIN milestone. The specification stays Draft/in progress while J-tasks are open.

---

## Phase 6: User Story 4 - JOIN-blocked suites (Priority: P2)

**Goal**: JOIN-blocked suites pass after the JOIN initiative delivers. These tasks stay OPEN until their evidence
exists.

- [ ] T039 [US4] J-001: list every `blocked: JOIN` suite with its definition, join construct and observed Elsa 4
  failure in `JTest/ledger/suites.md`.
- [ ] T040 [US4] J-002: reconcile those suites with the JOIN initiative's agreed contract and delivered implementation
  once it lands on the initiative branch; agree any adjustment through T036.
- [ ] T041 [US4] J-003: make the JOIN-dependent templates and waits work on Elsa 4 without changing expectations;
  suite edits go through T029.
- [ ] T042 [US4] J-004: run the JOIN-blocked suites on a fresh Workbench from the qualified head and record the results.
- [ ] T043 [US4] J-005: rerun the whole corpus on the final initiative head, reconcile `JTest/ledger/dispositions.md`
  and `JTest/ledger/suites.md`, and record the coordinator's full-acceptance verdict (SC-005).

---

## Phase 7: Polish and cross-cutting

- [ ] T044 [P] Finish `JTest/README.md`: what the suite is, layout, both run procedures, the ledger, and the rule that
  suite files are identical between engines.
- [ ] T045 [P] Add a pointer to the suite and its ledger in `docs/reference/elsa3-migration-readiness.md`.
- [ ] T046 Review the delivered work to convergence (local review loop), fix verified findings, and keep failed
  runs in the ledger (FR-021).
- [ ] T047 After T043, prepare the hand-over note for the Nexxbiz-specific workspace: the engine-neutral suites, the
  added template parameters and the approved suite fixes to bring back to the original repository (FR-022). Do not
  change that repository from here.
- [ ] T048 Set the spec status to Implemented in the PR that closes T043, not before.

---

## Dependencies and execution order

- Phase 1 → Phase 2 → US1 → US2 → US3 → US4 → Polish (T044 and T045 may start any time after US3 starts).
- Within US1: T008 to T015 in order; T016 gates T017; T018 closes the phase.
- Within US2: T019 → T020 → T021 → T022.
- Within US3: T023 to T027 can run in parallel; T028 needs T007 and T023 to T027; T030 to T037 follow; T038 waits for
  spec 1207 on the initiative branch.
- US4 waits for the JOIN initiative's delivery; T039 can be filled as soon as T035 is done.
- T047 and T048 come last.

## Parallel example (US3)

```text
T023 environment and configuration
T024 authenticate template
T025 definition templates
T026 instance templates
T027 activity templates
```

## Implementation strategy

1. **First increment**: Phases 1, 2 and US1. A green, approved Elsa 3 baseline on its own is useful: it shows which
   behaviour the suites encode.
2. **Second**: US2, then the HTTP suites on Elsa 4 (T023 to T028), which proves the template approach on the smallest
   slice.
3. **Third**: the remaining suites, dispositions and the qualifying run after spec 1207 (non-JOIN milestone).
4. **Last**: US4 after the JOIN initiative, then the hand-over and the Implemented status.
