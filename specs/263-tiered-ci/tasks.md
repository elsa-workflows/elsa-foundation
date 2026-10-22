---

description: "Tasks for spec 263: tiered CI with parallel test shards"
---

# Tasks: Tiered CI with parallel test shards

**Input**: [spec.md](spec.md), [plan.md](plan.md), [research.md](research.md), [data-model.md](data-model.md), [contracts/cli-and-ci.md](contracts/cli-and-ci.md), [quickstart.md](quickstart.md)

**Tests**: This unit has no Foundation feature class, so framework §2.23 feature-registration tests do not apply. Logic-bearing tooling gets risk-based self-tests in the repository's existing tooling pattern:

- `--self-test` modes in the Python scripts, as in `tools/ci/change_scope.py`;
- `*-self-test` generator commands, as in `ef-suites-self-test`.

They cover shard selection, the retry decision, quarantine, the tier traits, collection integrity, and the documentation and literal checks. No test-first cadence is declared for repository tooling, so each self-test sits beside its implementation task. That gap is the owner's to resolve and is not inferred here. No product test project is changed (spec Out of Scope; framework §2.21.1).

**Ordering constraints**:

- **FR-011.** T037 (the switch) runs only after T036 (the ruleset requires `Full tier`) and after `Full tier` has reported green on `main` at least once.
- **T009 and T010 land together.** The generator's EF commands move to the new manifest in the same commit that deletes `tools/ci/ef-suites.json`.

## Phase 1: Setup

- [X] T001 Confirm the baseline on the branch tip:
  - `python3 tools/ci/change_scope.py --self-test`, `python3 tools/contributor-checkpoints/test_workflow_guards.py` and `dotnet run --project tools/maps/Elsa.Maps.Generator -c Release -- ef-suites-self-test` pass;
  - `gh api repos/elsa-workflows/elsa-foundation/rulesets` lists ruleset `main-2` with required contexts `Build & test` and `Generated maps fresh`.

  Record the outputs in the PR notes. *(Done 2026-10-22: all pass. `ef-suites-select` returns 25 suites, not the 23 of the planning nightly: #3192 added `rules`, so there are 26 container legs with the Secrets proof.)*
- [X] T002 [P] File four flake-tracking issues, `needs-triage` only, one per test in [research R2](research.md#r2--flaky-tests) that has none:
  - `NativeHostingConformanceTests.Departed_routing_owner_is_reclaimed_and_its_real_transport_command_completes_on_survivor`
  - `WorkbenchBackgroundActivityRecoveryTests.Killed_workers_are_reclaimed_with_stable_invocations_and_one_native_continuation`
  - `WorkbenchCompensationSampleTests.The_sample_workflows_end_as_the_readme_says`
  - `StatementBindingTests.Statement_body_honors_cancellation_during_evaluation`

  Each body carries R2's evidence (run IDs, dates, error line). *(Done: #3259–#3262. No CI program issue or epic exists, so they have no parent; each references spec 263. `ResponseReplaySafetyProcessTests` was dropped: it is a real failure, not a flake, research R2.)*
- [X] T003 [P] Create the label `ci-flake`. File one standing issue, "CI flake tracker" (`needs-triage`, `ci-flake`), whose body explains that `ci-flake-record.yml` comments on it. Record its number for T021. *(Done: #3263.)*

---

## Phase 2: Foundational (blocks every story)

- [X] T004 Create `tools/ci/test-tiers.json` with the schema in [data-model](data-model.md#tier-manifest--toolscitest-tiersjson):
  - `schema: 1`, `integrationLeavesPrTier: false`, `shardTargetMinutes: 6`, `slowTestSeconds: 10`, `slowTestMode: "report"`;
  - one `projects` entry for each of the 120 test projects (every `*.csproj` under `Essentials/tests/**` and `Extensions/*/tests/**`, outside `studio/`, that references `Microsoft.NET.Test.Sdk`), with its `tier`, its `trait` and its `medianMinutes` from research R1 and R5.

  *(Done: 122 test projects, since #3192 added `Elsa.Rules.Tests` and a Rules provider suite. 31 are integration tier: 26 Testcontainers roots, Secrets PostgreSQL included, plus the 5 below. `Elsa.Rules.Tests` has no measured median yet (`null`). `Elsa.Architecture.Tests` is in `hostLaunchExemptions`: it launches SDK commands, not a host; research R5.)*

  The integration tier is the Testcontainers projects plus `Elsa.Workbench.Tests`, `Elsa.Cluster.EntityFrameworkCore.Tests`, `Elsa.Activities.Http.IntegrationTests`, `Elsa.Cli.Tests` and `Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests`. Every other project is `pr`.
- [X] T005 In `tools/ci/test-tiers.json`, add `splits` from [research R3](research.md#r3--per-class-durations-in-the-two-long-projects):
  - **Workbench**: `workbench-native-hosting` (`WorkbenchBackgroundActivityRecoveryTests`, `NativeHostingConformanceTests`, `NativeHostingGracefulHandoffTests`, `WorkbenchProcessTests`) and `workbench-rest` (rest);
  - **Cluster EF**: `cluster-foundation-host` (the 7 `FoundationHostCollection` classes) and `cluster-rest` (rest);
  - **Compensation**: `compensation-nesting` (`CompensationNestingJourneyTests`), `compensation-provider` (`CompensationProviderSubsetTests`, `CompensationBackoffJourneyTests`) and `compensation-rest` (rest).

  Use fully qualified class names.
- [X] T006 In `tools/ci/test-tiers.json`, add `shards` from [research R9](research.md#r9--job-budgets-and-layout):
  - `pr`: pr-1, pr-2 and pr-3 as listed there. While the switch is off, they also hold `integration-other` and `cluster-rest`, and `pr` has three more shards: `workbench-native-hosting`, `workbench-rest` and `cluster-foundation-host`.
  - `integration`: the 4 shards.
  - `containers`: 3 shards that balance the 26 legs (25 suites, `rules` included, plus `secrets-ef-composition`) by the nightly durations in research R1.

  `Elsa.Architecture.Tests` is in no shard; it runs in `Architecture guards`.
- [X] T007 Move the 25 suites from `tools/ci/ef-suites.json` into `containerSuites` in `tools/ci/test-tiers.json`, with fields and order unchanged.
- [X] T008 In `tools/ci/test-tiers.json`, add `quarantine` entries for the six tests R2 marks "quarantine": #3147, #3252 and #3259–#3262, each with `since: <merge date>`. Add an empty `slowAllowList`.
- [X] T009 Retarget the generator's EF suite code to `containerSuites` in `tools/ci/test-tiers.json`, keeping the `ef-suites-select` output and every failure message's meaning:
  - `tools/maps/Elsa.Maps.Generator/EfSuiteSelector.cs:12` (`ManifestPath`) and its JSON reading;
  - `tools/maps/Elsa.Maps.Generator/EfSuiteSelectorContractTests.cs:39`.

  Run `ef-suites-self-test` and `ef-suites-select`, and diff the matrix against T001's output; it must be identical. *(Done: byte-identical, 25 suites.)*
- [X] T010 Delete `tools/ci/ef-suites.json` in the same commit as T009, and update the references that point at it:
  - `.github/workflows/ci.yml:573` (comment);
  - `Extensions/README.md:130`;
  - `docs/reference/developer-solution-filters.md:25,112`;
  - `Essentials/tests/Activities/Http/CommandCost/Tests/README.md:13`;
  - the XML comment at `Essentials/tests/Activities/Http/CommandCost/Tests/CommandCostMeasurementTests.cs:17`;
  - the comment in `Essentials/tests/Activities/Compensation/Persistence/EntityFrameworkCore/ProviderTests/Elsa.Activities.Compensation.Persistence.EntityFrameworkCore.ProviderTests.csproj:5`.

  Leave the historical reports under `docs/reports/ci/` as they are.
- [X] T011 Create `tools/ci/test_tiers.py` with `select --tier pr|full|push|nightly`, per [contracts](contracts/cli-and-ci.md#toolscitest_tierspy):
  - **Output**: it reads the manifest and writes `pr_matrix`, `integration_matrix`, `container_matrix` and their counts to `--github-output`, with a table in `--summary`.
  - **Filters**: a split unit's filter is `FullyQualifiedName~<Class>.|…`; a rest group's is the `&`-joined negation `FullyQualifiedName!~<Class>.` of every class its siblings name.
  - **Switch**: `--tier pr` drops integration units when `integrationLeavesPrTier` is true. When the PR diff (`--base`/`--head`) changes that field to true, it calls `gh api repos/{repo}/rulesets/<id>` and exits 1 unless `main-2` requires `Full tier`.
- [X] T012 Add `--self-test` to `tools/ci/test_tiers.py`, with in-memory manifests covering:
  - a plain shard;
  - a split with a rest group, where the exact filter strings are asserted;
  - the switch on and off;
  - malformed JSON (exit 1);
  - a refusal when the ruleset fixture lacks `Full tier`.
- [X] T013 Create `tools/maps/Elsa.Maps.Generator/TestTierManifest.cs`, the manifest model and loader. It names R6 failures by JSON path.
- [X] T014 Create `tools/maps/Elsa.Maps.Generator/TestTierCheck.cs` (`test-tiers-check`), per [contracts](contracts/cli-and-ci.md#elsamapsgenerator). It checks:
  - every test project appears once and every entry exists (FR-016);
  - the Testcontainers trait, through the same `PackageReference` and `ProjectReference` closure that `solution-filter-roots` uses;
  - the `dotnet`-launch trait, by a scan of the project's `.cs` files and those of referenced test and fixture projects for `ProcessStartInfo`/`Process.Start` whose file name is the literal `"dotnet"`, `DOTNET_HOST_PATH` or a `DotnetMuxer()` call (git and bash are ignored);
  - Testcontainers requires `integration`, and a `dotnet` launch requires `integration` when `medianMinutes >= 2` (FR-017);
  - split classes exist (class declarations in the project);
  - `[Collection(…)]` members, resolved through `const string Name`, share one group;
  - shard coverage per tier, and container-suite coverage.

  Each failure names the project, `tools/ci/test-tiers.json` and both tiers.
- [X] T015 Create `test-tiers-self-test` in `tools/maps/Elsa.Maps.Generator/TestTierCheck.cs`, with fixture trees for each T014 failure, including:
  - a `dotnet` launch in a project under 2 min (allowed);
  - the same launch at 2 min (refused);
  - a git launch (allowed).

  Wire both commands into `tools/maps/Elsa.Maps.Generator/Program.cs` (usage comment `:10`, dispatch near `:58`, and the unknown-command list `:128`).
- [X] T016 Create `tools/maps/Elsa.Maps.Generator/TestTierRebalance.cs` (`rebalance <trx-dir> [--write]`). It computes per-project medians and per-class sums from TRX, and proposes `shards` and `splits` by longest-first bin packing within `shardTargetMinutes`, never splitting a collection. It is a maintainer tool, not run in CI. Run it on the TRX set from run 39790463068 and confirm it reproduces T005 and T006, or explain the difference in the PR. *(Done: it reproduces R3's class floors and R9's three PR bins of about 10 project-minutes. `--write` records the median over every run given, never one run: in run 39790463068 `Elsa.Etl.Tests` took 3.6 min against a 50-run median of 1.8, which would have crossed the 2-minute bar. The shard grouping stays a reviewed hand edit.)*
- [X] T017 Add `test-tiers-check` and `test-tiers-self-test` steps to the `Generated maps fresh` job in `.github/workflows/ci.yml`, beside `ef-suites-self-test` (`:573-578`). They follow the same `if: ${{ !cancelled() && steps.generator.outcome == 'success' }}` pattern, and each failed step is added to the "Explain a failed generator check" condition. Keep the job name. *(Done with a step of its own, "Explain a failed test tier check", because the generator explanation talks about maps and lock files.)*

**Checkpoint**: the manifest validates; `ef-suites-select` output is unchanged; no workflow behaviour has changed yet.

---

## Phase 3: User Story 1 — Fast feedback on every PR commit (P1) 🎯 MVP

**Goal**: `Build & test` reports from parallel shards, with one retry per failing test.

**Independent test**: quickstart "Story 1", steps 1–5.

- [X] T018 [US1] Create `tools/ci/test_results.py` with `failures`, `retry-filter` and `evaluate`, per [contracts](contracts/cli-and-ci.md#toolscitest_resultspy) and the [data-model](data-model.md#shard-result--test-outcomesjson):
  - it parses TRX `UnitTestResult` and `TestMethod` (className, name);
  - `evaluate` exits 1 on a non-quarantined double failure, on a test missing from try2, or on an aborted host;
  - it writes `test-outcomes.json` and a summary with retried passes, quarantined failures with issue links, and tests over `slowTestSeconds` that are not on `slowAllowList`.

  *(Done. The shard orchestration went into a separate `tools/ci/run_shard.py`, which runs each unit as its own `dotnet test` with its own results directory, in parallel up to the core count. A shared directory or one `.slnf` invocation would let VSTest overwrite TRX files that share a timestamped name, which the local smoke run showed. Abort detection reads only Error-level run messages; a Warning naming a test like `…_crashed_incumbent…` gave a false positive.)*
- [X] T019 [US1] Add `--self-test` to `tools/ci/test_results.py`, with small TRX fixtures written in code:
  - pass;
  - fail then pass;
  - fail twice;
  - a quarantined fail;
  - a theory (FQN without arguments);
  - an aborted run;
  - a slow test, on and off the allow-list.
- [X] T020 [US1] Restructure the PR tier in `.github/workflows/ci.yml`.
  - **`Plan tiers` job**: it takes the current `Build & test` steps up to `Check catalog and module README links` (`:104-137`), adds `python3 tools/ci/test_tiers.py --self-test`, `python3 tools/ci/test_results.py --self-test` and `python3 tools/ci/test_tiers.py select --tier pr …`, and outputs `docs_only` and the matrix.
  - **`PR shard (${{ matrix.shard }})` job**: a matrix job, skipped when `docs_only`, with steps per [contracts "Shard job steps"](contracts/cli-and-ci.md#shard-job-steps-pr-and-integration):
    - restore with `restore-ci-project-graph.sh`;
    - full `Elsa.Server.slnx` build (research R4);
    - `dotnet test` with TRX, `--blame-hang-timeout 10m --blame-hang-dump-type none` and the unit filter;
    - one retry through `retry-filter`;
    - `evaluate`;
    - upload of `test-outcomes-${{ matrix.shard }}`.

    It has `timeout-minutes: 40` and `fail-fast: false`.
  - **`Build & test` job**: becomes the aggregate. It `needs` the plan job, the shards, `architecture-guards` and `core-only`, uses `if: ${{ !cancelled() }}`, and succeeds when the plan succeeded and the rest succeeded, or the change is `docs_only`. Keep the name and the `docs_only` output, and repoint `architecture-guards` and `core-only` (`:385-386`, `:428-429`) from `needs: build-and-test` to the plan job.
  - **Remove** the "Generate container-free test solution filter" step (`:153-190`); `select` replaces it.
  - **Header comment**: update `:1-63` to describe the tiers, and keep the `python3 tools/contributor-checkpoints/test_workflow_guards.py` literal.
- [X] T021 [US1] Create `.github/workflows/ci-flake-record.yml`, a `workflow_run` workflow, per [research R7](research.md#r7--retry-and-quarantine-mechanics):
  - triggers: `CI` and `CI full tier`, on `completed`;
  - permissions: `actions: read`, `issues: write`;
  - it downloads every `test-outcomes-*` artifact of the triggering run, validates each name against `^[A-Za-z_][A-Za-z0-9_.]*$` and the SHA against 40 hex characters, and adds one comment per run to the T003 issue (#3263) listing `passedOnRetry` with the run URL;
  - no step interpolates artifact content into a shell command; names are passed through files or environment variables.

  *(Done. The tracker's number is `flakeTrackerIssue` in the manifest, and the validation lives in `test_results.py record-flakes`, with self-test cases for an injected name, a path-like shard and malformed JSON. GitHub runs `workflow_run` workflows from the default branch only, so it first fires after this PR merges.)*
- [X] T022 [US1] Extend `tools/contributor-checkpoints/test_workflow_guards.py`, or add `tools/ci/test_tiers.py --self-test` cases if the guards' scope does not fit, to assert:
  - `ci.yml` still has a job named `Build & test` whose `needs` include every shard job;
  - no job named `Build & test` or `Full tier` sits behind an `if:` that can skip it.

  *(Done as `test_tiers.py check-workflows`, run in `Plan tiers`. It also refuses a `labeled` trigger (comments excluded) and a `Full tier` job that does not fail on a failed plan.)*
- [ ] T023 [US1] Open this work's PR as a draft and run quickstart "Story 1". Record the push-to-`Build & test` time and the shard durations in the PR evidence comment.

**Checkpoint**: `Build & test` is sharded and green, and every project that ran before still runs, the integration projects included (switch off).

---

## Phase 4: User Story 2 — Expensive tests gate the merge, not every WIP commit (P1)

**Goal**: a required `Full tier` runs on ready PRs, on request, on `main` and nightly.

**Independent test**: quickstart "Story 2", steps 1–9.

- [X] T024 [US2] Create `.github/workflows/full-tier.yml` (`on: workflow_call`, inputs `run: boolean` and `containers: boolean`):
  - **`Integration shard (${{ matrix.shard }})`**: from `test_tiers.py select --tier full`, with the same steps as T020's shard.
  - **`EF container shard (${{ matrix.shard }})`**: restore and build its suites' projects once. For each suite, run `dotnet test` with that suite's `require` variable set in that step's `env` only (never `GITHUB_ENV`), then retry and evaluate. The shard that holds `secrets-ef-composition` also starts PostgreSQL and runs the Secrets proof with the steps now at `ci.yml:347-375`.
  - **Docs-only and draft**: when the caller passes `run: false`, every job is skipped. *(Done: the callers skip the whole `uses:` job instead, so the workflow takes only the matrices and their counts. Container suites run one at a time per shard through `run_shard.py`, each with only its own `require` variable armed.)*
- [X] T025 [US2] Create `.github/workflows/ci-full.yml` (`name: CI full tier`):
  - triggers: `pull_request` types `[opened, reopened, synchronize, ready_for_review]` on `main`; concurrency `ci-full-pr-<n>`, cancel in progress;
  - **`Plan` job**: runs `change_scope.py`, and sets `run=true` when the PR is not a draft, or its diff touches `tools/ci/test-tiers.json`, `tools/ci/test_tiers.py`, `tools/ci/test_results.py` or `.github/workflows/full-tier.yml`, and is not docs-only;
  - calls `full-tier.yml` with `containers: true`;
  - **`Full tier` job**: the aggregate, a plain job with `if: ${{ !cancelled() }}`, whose `name` follows research R8 point 3: `Full tier` when `run=true` or the PR is not a draft, otherwise `Full tier (not run: draft)`. On a draft it succeeds under the draft name, so the head carries no required `Full tier` context. It fails unless `needs.plan.result == 'success'`. On a ready PR it succeeds with summary "documentation only" only on an explicit `run == 'false'` from that successful plan, never on an empty output. Otherwise it succeeds only when every shard succeeded. Add a quickstart case for a failing plan on a ready PR (Code Review round 2).
- [X] T026 [US2] In `.github/workflows/ci.yml`:
  - add the `workflow_dispatch` input `tier` (choice `full`/`pr`, default `full`);
  - on `push`, `schedule` and `tier=full` dispatch, call `full-tier.yml`, with `containers` true for schedule and dispatch and false for push (FR-010);
  - remove the now-duplicated jobs `select-ef-suites`, `ef-container-suites`, `ef-container-result` and `secrets-ef-composition` (`:225-375`);
  - repoint the `alert` job's `needs` (`:461-472`) to the aggregate jobs;
  - keep `!cancelled()` so a superseded run files nothing.
- [ ] T027 [US2] Run quickstart "Story 2", steps 1–6, 8 and 9, on throwaway PRs, and record each check context with its conclusion in the PR evidence comment. In particular, confirm that `ready_for_review` does not re-run `Build & test`, that adding any label starts no `CI full tier` run, and that a draft head shows `Full tier` as expected (missing) and stays unmergeable after `gh pr ready` until the real run reports.

**Checkpoint**: `Full tier` reports correctly in every PR state in the [data-model state table](data-model.md#state-a-pull-requests-checks); `main` and nightly coverage is unchanged.

---

## Phase 5: User Story 3 — Flaky tests stop costing merges (P1)

**Goal**: known flakes don't block, and every retry is visible and tracked.

**Independent test**: quickstart "Story 3".

- [X] T028 [US3] Add `check-quarantine` to `tools/ci/test_results.py`. It runs `gh issue view <n> --json state` for each entry it checks and exits 1 on a missing or closed issue, naming the entry, the issue and the fix. With `--base <sha>` it reads the manifest at `<sha>` and checks only new or changed entries. It first checks that the commit is readable (`git cat-file -e <sha>^{commit}`) and exits 2 if not; only "path not in that commit" counts as an absent file, which means every entry is new. Any other `git show` failure exits 2, so a shallow checkout can never fall back to checking the whole list (Code Review round 2). Without `--base` it checks every entry. Add self-test cases with a stubbed `gh`, through a `GH` environment override: an unchanged entry with a closed issue passes with `--base`, a new entry with a closed issue fails with `--base`, the same closed issue fails without `--base`, and an unreadable base commit exits 2.
- [X] T029 [US3] In `.github/workflows/ci.yml`, with `permissions: issues: read` on the running job only:
  - on `pull_request`, fetch the base commit explicitly first (`git fetch --no-tags --depth=1 origin <PR base sha>`; `actions/checkout` fetches one commit by default), then run `test_results.py check-quarantine --manifest tools/ci/test-tiers.json --base <PR base sha>` in `Plan tiers`; it runs on documentation-only PRs too;
  - on `push` to `main` and `schedule`, run it without `--base` in a job that the `alert` job's `needs` include, so a closed issue files or updates the `main is red` issue.
- [ ] T030 [US3] Run quickstart "Story 3", steps 1–4, and the retry case from "Story 1" step 4, and confirm a `ci-flake-record` comment appears on the T003 issue. Record the links in the PR.

**Checkpoint**: FR-012 to FR-014 are met. Each of R2's six tests is quarantined with an open issue, and `DurableDrainCycleTests` is watched.

---

## Phase 6: User Story 4 — Every test project has a known tier and stays in it (P2)

**Goal**: drift fails CI with a message that says what to do.

**Independent test**: quickstart "Story 4", steps 1–5.

- [ ] T031 [US4] Run quickstart "Story 4" steps 1–5 on a throwaway draft PR, and record the failing messages in the PR evidence. They exercise T014's checks and T018's slow-test report end to end.
- [ ] T032 [P] [US4] Add the tier rule to `.claude/skills/elsa-add-unit-tests/SKILL.md`:
  - a new test project needs a `tools/ci/test-tiers.json` entry;
  - Testcontainers, or a child host in a project of 2 min or more, means integration tier;
  - otherwise the project is PR tier;
  - a link to `docs/contributing/merging.md#ci-tiers`.
- [ ] T033 [P] [US4] Add a "Test tier" row to `docs/contributing/review-checklist.md` that points at the manifest and `test-tiers-check`. Add the Gate-index row "§— Test tier: every test project in one tier; Testcontainers or a ≥2 min child host is integration tier | `Elsa.Maps.Generator test-tiers-check`" to `.specify/memory/constitution.md`. Bump it to 6.2.0 (MINOR) with a Sync Impact Report naming spec 263 and leaving the approval line for the owner; Joey Barten approves in the PR.

**Checkpoint**: SC-005. Each drift check has a self-test case (T015, T019) and a PR-level demonstration (T031).

---

## Phase 7: User Story 5 — Agents and humans know the draft rule (P3)

**Goal**: one documented section, linked from every PR-driving skill, guarded.

**Independent test**: quickstart "Story 5".

- [ ] T034 [US5] Add `## CI tiers` to `docs/contributing/merging.md`, between "PR evidence" and "Review and merge". It covers:
  - the two tiers and what each runs;
  - the draft rule: open as draft, then `gh pr ready <n>` before requesting review;
  - `gh workflow run ci.yml --ref <branch> -f tier=full`;
  - the quarantine list and how to add an entry.

  Then make the skill changes:
  - add a link to it in `.claude/skills/elsa-auto-review-merge/SKILL.md` and `.claude/skills/speckit-implement/SKILL.md` (its finishing step);
  - in `.claude/skills/elsa-quality-fix/SKILL.md:27`, change "open a ready PR" to "open a draft PR, then run `gh pr ready` before requesting review", with the link;
  - add `check-docs` to `tools/ci/test_tiers.py` (with self-test cases for each missing link and each mismatched literal) and run it in `Plan tiers`;
  - refresh the changed skills' hashes in `.specify/integrations/claude.manifest.json`.

**Checkpoint**: FR-023 to FR-025. Removing a link or renaming the dispatch input fails `Plan tiers`.

---

## Phase 8: User Story 6 — Studio CI runs only when Studio changes (P3)

**Goal**: backend-only PRs start no Studio jobs.

**Independent test**: quickstart "Story 6".

- [ ] T035 [P] [US6] Add `paths:` to the `pull_request` trigger of `.github/workflows/studio-ci.yml`, with the list from [research R10](research.md#r10--studio-ci-path-filter). Leave `push` unfiltered. Add a header comment explaining that none of its checks is required, so a path filter is safe. Run quickstart "Story 6" and record the results.

**Checkpoint**: SC-007.

---

## Phase 9: The switch (FR-011) and outcomes

- [ ] T036 After the implementation PR (Phases 1–8) merges and `Full tier` is green on a `main` push, ask the admin (Joey Barten or Sipke Schoorstra) to add `Full tier` to ruleset `main-2`'s required contexts. Confirm with `gh api repos/elsa-workflows/elsa-foundation/rulesets/<id> --jq '.rules[] | select(.type=="required_status_checks")'`, and record the output on the program issue. In the same step, update every merge path that names the required checks, so it waits for `Full tier` instead of trying a merge the ruleset refuses: `docs/contributing/merging.md`, `docs/contributing/review-checklist.md`, and the `elsa-auto-review-merge` and `elsa-quality-fix` skills (refresh `.specify/integrations/claude.manifest.json`).
- [ ] T037 In a separate PR, set `integrationLeavesPrTier: true` in `tools/ci/test-tiers.json` and drop the integration units from `shards.pr`, leaving pr-1 to pr-3 per research R9. Its `Plan tiers` must show the ruleset check passing. Afterwards the PR tier runs at most 5 test jobs (FR-022).
- [ ] T038 Measure the outcomes after T037 and post them on the program issue:
  - **SC-001**: the median push-to-`Build & test` time over the first 20 code PRs, from the runs API;
  - **SC-002**: `Full tier` time on each ready PR, naming the runs that retried;
  - **SC-004**: re-runs caused by quarantined tests over 30 days;
  - **the load risk** from the plan: the share of PR pushes made while ready, and the queue wait, against R1's baseline.

  A median of at most 13 minutes meets SC-001; file the method-level split of the slowest class as a `needs-triage` follow-up. A miss above 13 minutes goes to the owner with the options in the plan's Risks section.
- [ ] T039 Set the spec's `**Status**:` line to `Implemented` in the PR that completes T038's measured outcomes, before it merges (docs/reference/spec-lifecycle.md; never a separate status PR). Update `docs/program-goals/` per Program bookkeeping.

---

## Dependencies & Execution Order

- **Phase 1** has no dependencies. T002 and T003 can run in parallel with each other.
- **Phase 2** blocks every story.
  - T004 → T005 → T006 (same file, in order).
  - T007 and T008 follow T004.
  - T009 and T010 come after T007 and land in one commit.
  - T011 and T012 need T004 to T008.
  - T013 → T014 → T015 → T017, with T016 after T013.
- **US1 (Phase 3)** needs Phase 2. T018 → T019 → T020, then T021 and T022, then T023.
- **US2 (Phase 4)** needs T018 and T020, because it reuses the shard steps and scripts. T024 → T025 → T026, then T027.
- **US3 (Phase 5)** needs T018 and T021. It can run in parallel with US2.
- **US4 (Phase 6)**: T032 and T033 can start any time after Phase 2. T031 needs T017 and T020.
- **US5 (Phase 7)** needs T011 for `check-docs` and T026 for the dispatch-input literal.
- **US6 (Phase 8)** is independent of everything else and can go first.
- **Phase 9** is strictly last: T036 → T037 → T038 → T039.

## Parallel Example

```text
After Phase 2:
  T018–T020 (US1, ci.yml + test_results.py)
  T032, T033 (US4 docs and constitution)
  T035     (US6, studio-ci.yml)

After T020:
  T024–T026 (US2, workflow files)
  T028–T029 (US3, test_results.py check-quarantine)
```

T028 and T018 edit the same file, so run them one after the other even though they belong to different stories.

## Implementation Strategy

**One implementation PR (Joey Barten, 2026-10-22).** Phases 1–8 land together on `agent/claude/263-tiered-ci-impl`, in this order, with a commit per phase so a review can follow them:

1. **Phase 2 + US1 (MVP):** the PR tier is sharded and retries are reported, with every test still in `Build & test` while the switch is off.
2. **US2 + US3:** the full tier and quarantine.
3. **US4 to US6:** guards, documentation, skills, the constitution row and Studio.

**Phase 9 is a second, small PR by construction (FR-011).** An admin can only make `Full tier` required once `ci-full.yml` is on `main`. Before that, every other open PR would wait for a check its branch cannot produce. So T036 runs after the implementation PR merges, and T037 to T039 follow in the switch PR.

The scale is 39 tasks in two PRs.
