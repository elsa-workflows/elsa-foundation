# Tasks: Final JTest acceptance for Elsa 3 to Elsa 4 conversion

**Input**: [spec.md](spec.md), [plan.md](plan.md), [research.md](research.md), [data-model.md](data-model.md),
[contracts/](contracts/), [quickstart.md](quickstart.md).

**Suite root**: `extensions/Elsa3/tests/JTest/` (written `JTest/` below). **Corpus**: the JTest-Nexxbiz repository at
commit `b1cde13`.

**Tests**: The JTest suites are the tests. The one production change (T004, T005) adds a project reference and a
feature-catalog entry with no logic; the existing feature-composition and Elsa 3 boundary tests cover it, so no new
unit test is required (framework §2.23). No test cadence beyond that applies.

**Approval gates**: T016 (baseline failure list), T029 and T036 (suite changes and adjustments) and T034 (every
`incompatible` bucket) need the coordinator's recorded approval before the tasks after them start.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: US1 to US4 from spec.md

---

## Phase 1: Setup

- [X] T001 Add `/extensions/Elsa3/tests/JTest/** -text` to the repository `.gitattributes` first (the corpus stores
  `import-and-publish-workflows.json` with CRLF line endings, which `core.autocrlf` would otherwise change), then copy
  the corpus at `b1cde13` unchanged into `JTest/` from the commit, not a working tree (for example
  `git -C <corpus> -c core.autocrlf=false archive b1cde13 <paths> | tar -x -C extensions/Elsa3/tests/JTest`; line-ending
  conversion must be off, or LF files come out as CRLF): `workflow-definitions/`, `tests/`, `templates/`,
  `environments/`, `test-files/`, `global-configuration.json`, `import-and-publish-workflows.json`; leave out
  `pipelines/` and the repository README. Commit the attribute and the copy alone, with the source commit in the message, and check with
  `git ls-files --eol` that every file matches the source index (FR-001). Then, in a second commit, give the Elsa 3
  side the same naming as Elsa 4: rename `templates/` to `templates-elsa3/`, `environments/dev.json` to
  `environments/elsa3.json` and `global-configuration.json` to `global-configuration.elsa3.json`, and point its
  template search path at `templates-elsa3/*` (renames plus that one search-path line).
- [X] T002 Check `JTest/` for secrets: credentials and base URLs are `${VARIABLE}` references; other literals in the
  environment files (dispatch channels, wait times, engine-specific expected values) are not secrets (FR-004).
- [X] T003 [P] Create the ledger files with their table headers from data-model.md: `JTest/ledger/suites.md` (one row
  per suite, all `not run` except the excluded one, spec Q13), `JTest/ledger/baseline-failures.md`, `JTest/ledger/refactor-scope.md`,
  `JTest/ledger/suite-changes.md`,
  `JTest/ledger/dispositions.md` (one row per definition, bucket `unclassified`, or `excluded` for the two of spec Q13), `JTest/ledger/runs.md`.

---

## Phase 2: Elsa 4 host composition (parallel with US1 and US2)

- [X] T004 Add a `ProjectReference` to the Elsa 3 reconciliation project
  (`extensions/Elsa3/src/Activities/Design/Import/Reconciliation/`) in `apps/Elsa.Workbench/Elsa.Workbench.csproj`.
- [X] T005 Add `typeof(Elsa3JsonReconciliationFeature).Assembly` to the Elsa 3 feature assemblies in
  `apps/Elsa.Workbench/Program.cs`, next to the four existing Elsa 3 entries; leave the feature disabled in
  `apps/Elsa.Workbench/shells.json`.
- [X] T006 Run `dotnet restore Elsa.Server.slnx` and commit every `packages.lock.json` it changes (at least
  `apps/Elsa.Workbench/`, `Essentials/tests/Modularity/Tests/` and `Essentials/tests/Modularity/ProviderTests/`; CI
  restores in locked mode); regenerate the solution filters (`dotnet run --project tools/maps/Elsa.Maps.Generator -- solution-filters`),
  commit the changed `Elsa.Server.Workbench.slnf` and confirm with `-- solution-filters-check`; build the Workbench and run the architecture tests that cover feature
  composition and the Elsa 3 boundary (`Essentials/tests/Architecture/Tests/`).
- [X] T007 Find and verify the configuration override that enables `Elsa3JsonReconciliation` (`Options:SourceId`, and
  `Options:Files` with `{Order, FilePath}` entries listing the 32 definitions of the acceptance set, spec Q13 and Q15) together with `Elsa3ImportActivitiesEntityFrameworkCore`, and sets `WorkflowsRuntimeCheckpointPersistence` to `Durable` for one Workbench run without editing the
  committed `shells.json` (environment variables or a run-specific shells file, R-5); after start, set activity input
  and output capture to snapshot level and a `variableWrites` override with `PUT /runtime/workflows/diagnostics/settings`
  (in-memory, so after every start). Record the exact commands in `JTest/README.md`.

**Checkpoint**: the Workbench starts with the reconciliation enabled for a run and logs its summary.

---

## Phase 3: User Story 1 - A green Elsa 3 baseline (Priority: P1)

**Goal**: Every suite passes on Elsa 3.6.3, or is blocked by a named 3.6.3 defect, with approved suite fixes.

**Independent test**: run the corpus against the 3.6.3 container and read `JTest/ledger/suites.md`.

- [X] T008 [US1] Build the baseline image from elsa-core tag `3.6.3` (`docker/ElsaServer.Dockerfile`) from the tag's
  committed bytes (the Git URL build context in quickstart.md; a Windows working tree breaks `entrypoint.sh`), find its development admin credentials, and record the build command and image digest in
  `JTest/README.md` (FR-006).
- [X] T009 [US1] Install or update `JTest.Cli` to the latest release and run
  `jtest validate "tests/**/*"` from `JTest/`; record the version and any invalid file in `JTest/ledger/runs.md`.
- [X] T010 [US1] Start a fresh baseline container, run `import-and-publish-workflows.json`, and record its result per
  dataset in `JTest/ledger/runs.md`; a definition that fails to import on 3.6.3 is itself a baseline failure.
- [X] T011 [US1] Run the 41 suites of the acceptance set (`performance/baseline-dispatch-tests.json` is excluded,
  spec Q13) on the same container; record the run in `JTest/ledger/runs.md` and the Elsa 3
  result per suite in `JTest/ledger/suites.md`.
- [X] T012 [US1] Rerun every failing suite on a fresh container to separate intermittent failures from stable ones;
  keep both runs in `JTest/ledger/runs.md`.
- [X] T013 [US1] For each failing assertion, add a row to `JTest/ledger/baseline-failures.md` with observed and expected
  values and the suite's description (FR-007).
- [X] T014 [US1] Classify each row as suite defect, corrected behaviour or 3.6.3 defect, with evidence (suite
  description, elsa-core 3.6.3 source or release notes, a minimal reproduction) and the proposed fix or `none
  (blocked)`.
- [X] T015 [US1] Check the If and Switch suites (`tests/branching/if-then-else-tests.json`, `switch-*`,
  `switchflow-*`) are present and cover the corpus's If, Switch and FlowSwitch activities; record any activity the
  corpus definitions use that no suite covers in the *Notes* section of `JTest/ledger/baseline-failures.md`.
- [X] T016 [US1] **Approval gate**: post the classified list on the PR and get the coordinator's approval of the whole
  list; record the approval link in `JTest/ledger/baseline-failures.md`.
- [X] T017 [US1] Apply the approved fixes to `JTest/tests/` and `JTest/templates-elsa3/`, and set `"version": 1` in
  `JTest/workflow-definitions/storage-drivers-variables-root-workflow.json` (spec Q14, approved),
  one commit, with the ledger row named per change; mark 3.6.3-defect suites `blocked: 3.6.3 defect <defect>` in
  `JTest/ledger/suites.md` (FR-008).
- [X] T018 [US1] Rerun the 39 suites of the acceptance set on a fresh container and confirm every unblocked suite passes (SC-001); record the
  run.

**Checkpoint**: green Elsa 3 baseline.

---

## Phase 4: User Story 2 - Engine-neutral suites (Priority: P1)

**Goal**: suite files carry no Elsa 3-specific values; they still pass on 3.6.3.

**Independent test**: Elsa 3 rerun green; the diff of each suite shows only values moved behind parameters.

- [X] T019 [US2] List every Elsa 3-specific value in `JTest/tests/**` that the template contract cannot absorb : direct
  Elsa 3 API calls in `http` steps (the `events` suite and three `performance` suites call
  `/elsa/api/workflow-definitions/{id}/execute` and `/elsa/api/workflow-instances`), response fields read from those
  steps, reads inside template outputs beyond the projected fields (for example
  `instance.workflowState.activityExecutionContexts[0].properties.Variables[...]`), status words the translation table
  does not cover, and Elsa 3 generated identifiers; include the keys suites read inside `activityState` and `outputs`, which the Elsa 4
  templates must supply (contracts/templates.md); write the list to `JTest/ledger/refactor-scope.md`.
- [X] T020 [US2] For each Elsa 4 template that needs a parameter its Elsa 3 version lacks (for example the workflow
  instance id for `elsa-activity-instance-get`, contracts/templates.md), add the parameter to the Elsa 3 template in
  `JTest/templates-elsa3/` without changing its outputs; for each direct API call or internal read listed in T019, add a new
  Elsa 3 template in `JTest/templates-elsa3/` that returns the value with a stated meaning.
- [X] T021 [US2] Move each listed value behind a template output, an environment value in `JTest/environments/elsa3.json`
  or a suite parameter, and pass any new template parameters, in `JTest/tests/**`; no expected business value or
  assertion changes (FR-010).
- [X] T022 [US2] Rerun the 39 suites of the acceptance set on a fresh 3.6.3 container; the same suites pass as after T018 (SC-002 part 1);
  record the run.

**Checkpoint**: the suite files are frozen for the Elsa 4 work; later edits go through T029.

---

## Phase 5: User Story 3 - Converted definitions pass on Elsa 4 (Priority: P1)

**Goal**: the Elsa 4 template set and run; every suite not blocked by JOIN behaviour, value capture, a 3.6.3 defect or
an approved incompatibility passes.

**Independent test**: fresh Workbench with reconciliation, `jtest run` with the Elsa 4 configuration, ledger read.

**Rule for T024 to T027**: each template reads the full value its outputs carry, never a preview, summary or
case-insensitive lookup, and normalizes nothing the contract's translation table does not list; an output Elsa 4
cannot produce with the same meaning fails the template with the output named (FR-011, FR-013).

- [X] T023 [P] [US3] Create `JTest/environments/elsa4.json` (same keys as `elsa3.json`, differing only in
  engine-specific values; the executor base `{base}/workflows/http` comes from `ELSA_EXECUTOR_BASE_URL`) and `JTest/global-configuration.elsa4.json` (template search path `templates-elsa4/*`)
  per contracts/run-hosts.md.
- [X] T024 [P] [US3] Write `templates-elsa4/elsa-api-login-template.json`: `elsa-authenticate` per
  contracts/templates.md.
- [X] T025 [P] [US3] Write the Elsa 4 versions of the templates added in T020, in `templates-elsa4/`, with outputs of
  the same meaning (the execute template follows contracts/templates.md, *Execute*: resolve the published artifact, start, read the
  instance until terminal within a deadline, take its `outputs`). The definition
  import, publish, delete and composite templates are not written for Elsa 4 (contracts/templates.md).
- [X] T026 [P] [US3] Write `templates-elsa4/elsa-api-workflow-instance-templates.json`: get, search, not-exists and
  variable value, with the status and version translations.
- [X] T027 [P] [US3] Write `templates-elsa4/elsa-api-activity-instance-templates.json`: the eight
  `elsa-activity-instance-*` templates, with node-id suffix matching, execution-list counts and the counting rules of
  `assert-all-completed` and `assert-none-executed` in contracts/templates.md.
- [X] T028 [US3] Start a fresh Workbench with the T007 override on the HTTP definition (`http-endpoints.json`), run the
  six `tests/http/*` suites with the Elsa 4 configuration, and fix templates until they pass or a failure is shown to
  be an engine or conversion result; record the run and receipt.
- [ ] T029 [US3] **Approval gate** for suite edits: any change to a suite file or an Elsa 3 template needed on Elsa 4,
  and any raised `wait` in a suite or a template of either set, is added to `JTest/ledger/suite-changes.md` with its reason, approved by the coordinator, and, when it
  touches a suite file or the Elsa 3 set, verified on 3.6.3 before it lands (FR-012).
- [ ] T030 [US3] Run `tests/single-outcome/single-outcome-tests.json` the same way; record the run.
- [X] T031 [US3] Start a fresh Workbench with the 32 definitions of the acceptance set (a `Files` list leaving out the
  four excluded ones, spec Q13 and Q15); copy the reconciliation receipt into
  `JTest/ledger/runs.md` and fill the Reconciliation column of `JTest/ledger/dispositions.md`.
- [X] T032 [US3] Run the 39 suites of the acceptance set on that host; record the Elsa 4 result per suite in `JTest/ledger/suites.md`.
- [X] T033 [US3] For each failing suite, find the cause (template, converter, engine, prerequisite) and record it in
  `JTest/ledger/suites.md`; a template cause is fixed in `templates-elsa4/` and rerun; a missing prerequisite marks
  the suite `blocked: <prerequisite>` (FR-016); a value that comes back truncated or unavailable under the capture
  limits marks it `blocked: value capture` with the key and limit (FR-024).
- [ ] T034 [US3] For a converter or engine cause: if the definition cannot convert faithfully, confirm it is refused
  with guidance and propose the `incompatible` bucket, recorded only after the coordinator approves it in
  `JTest/ledger/dispositions.md`; if a genuine Elsa 4 gap or converter defect, file a `needs-triage` issue
  (sub-issue of the initiative), mark the suite `blocked: <issue>` and leave the bucket `unclassified` until the
  issue is fixed, an approved adjusted copy passes (T036) or the definition is bucketed `incompatible`; never add a
  converter workaround (FR-016, FR-017).
- [ ] T035 [US3] For each suite whose definition contains a join, decide from the failure whether the join is what stops
  it; mark `blocked: JOIN` with the construct and failure only then (FR-018), and keep the rest in the main run.
- [ ] T036 [US3] **Approval gate** for adjustments: for a definition that needs an adjusted copy, add it under
  `JTest/adjusted/` with the exact change and reason in `JTest/ledger/dispositions.md`, get the coordinator's
  approval, and run the Elsa 4 host with the adjusted copy in place of the original via the reconciliation `Files`
  list (FR-014, FR-015).
- [ ] T037 [US3] Bucket every definition in `JTest/ledger/dispositions.md` whose suites are neither JOIN-blocked,
  value-capture-blocked nor issue-blocked; the rest stay `unclassified` with the reason, and T043 completes SC-003.
- [ ] T038 [US3] Once spec 1207 is implemented on the initiative branch (its status Implemented), merge the initiative branch into this branch and
  make the qualifying run: the 39 suites of the acceptance set on a fresh Workbench from that head. Record it as the non-JOIN milestone run
  (FR-020, SC-004) only when every acceptance-set suite that SC-004 does not exempt passed in it; otherwise record it
  as a qualifying run and repeat T038 once the blocking issues or prerequisites are closed. Before recording it, confirm with `git diff` that `JTest/tests/**` differs from the T022 state
  only by approved T029 rows, that `JTest/templates-elsa3/` differs only by approved rows too, and rerun the changed suites
  on 3.6.3 (SC-002).

**Checkpoint**: non-JOIN milestone, once T038 records it. The specification stays Draft/in progress while J-tasks are open.

---

## Phase 6: User Story 4 - Suites blocked by separate initiatives (Priority: P2)

**Goal**: JOIN-blocked and value-capture-blocked suites pass after their initiatives deliver. These tasks stay OPEN
until their evidence exists. T049 to T051 (value capture) run alongside T039 to T042 (JOIN); T043 closes both.

- [ ] T039 [US4] J-001: list every `blocked: JOIN` suite with its definition, join construct and observed Elsa 4
  failure in `JTest/ledger/suites.md`.
- [ ] T040 [US4] J-002: reconcile those suites with the JOIN initiative's agreed contract and delivered implementation
  once it lands on the initiative branch; agree any adjustment through T036.
  Also re-evaluate the two suites spec Q15 excluded (`branching/implicit-tests.json`, `branching/join-wait-all-active-tests.json`)
  against that contract: bring them back, adjusted through T029/T036, or replace them with Elsa 4-only workflows and
  suites that guard the decided behaviour.
- [ ] T041 [US4] J-003: make the JOIN-dependent templates and waits work on Elsa 4 without changing expectations;
  suite edits go through T029.
- [ ] T042 [US4] J-004: run the JOIN-blocked suites on a fresh Workbench from the qualified head and record the results.
- [ ] T049 [US4] V-001: list every `blocked: value capture` suite with the key it reads and the capture limit hit in
  `JTest/ledger/suites.md`.
- [ ] T050 [US4] V-002: once the value-capture initiative delivers on the initiative branch, configure the run to
  capture those values as it provides (update T007's commands in `JTest/README.md`) and adjust `templates-elsa4/`
  without changing expectations; suite edits go through T029.
- [ ] T051 [US4] V-003: run those suites on a fresh Workbench from the qualified head and record the results.
- [ ] T043 [US4] J-005: rerun the 39 suites of the acceptance set on the final initiative head, reconcile `JTest/ledger/dispositions.md`
  and `JTest/ledger/suites.md`, bucket every remaining definition of the acceptance set (SC-003), and record the coordinator's
  full-acceptance verdict (SC-005).

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

- Phase 1 → US1 → US2 → US3 → US4 → Polish. Phase 2 needs only Phase 1, runs alongside US1 and US2, and must be done
  before T028. T044 and T045 may start any time after US3 starts.
- Within US1: T008 to T015 in order; T016 gates T017; T018 closes the phase.
- Within US2: T019 → T020 → T021 → T022.
- Within US3: T023 to T027 can run in parallel; T028 needs T007 and T023 to T027; T030 to T037 follow; T038 waits for
  spec 1207 to be implemented on the initiative branch.
- US4 waits for the JOIN and value-capture initiatives' delivery; T039 and T049 can be filled as soon as T035 and
  T033 are done; T043 needs T042 and T051.
- T047 and T048 come last.

## Parallel example (US3)

```text
T023 environment and configuration
T024 authenticate template
T025 templates added in the refactor
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
