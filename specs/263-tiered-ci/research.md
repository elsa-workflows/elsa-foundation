# Research: Tiered CI with parallel test shards

All findings were measured on 2026-10-22 against `main` and the GitHub API, unless a section says otherwise.

**Sources:**

- **Per-project durations**: the `Build & test` logs of 50 successful `CI` runs (push and pull request) from 2026-10-19 to 2026-10-22. Each project's `Passed! … Duration:` line was parsed, and the median is used below.
- **Flakes**: every `Build & test` job that failed from 2026-10-15 to 2026-10-22 (53 logs), plus the failed first attempt of every run that was re-run in that window (19 runs).
- **Per-class durations and build sharing**: one instrumented run on this branch ([R3](#r3--per-class-durations-in-the-two-long-projects), [R4](#r4--build-once-and-share-versus-rebuild-per-shard)). Its two commits (`5197b793f`, `2abc9c699`) were reverted afterwards.

## R1 — Where the waiting goes today

- **`Build & test` job**: median 41.9 min, p90 48.4 min.
  - Restore takes about 0.5 min, the full solution build 3.4 min (max 3.9), and the test step 39.1 min (max 44.5).
  - The test projects' medians add up to 93 runner-minutes. `dotnet test` runs projects in parallel on the 4-core runner, at about 2.4× here.
- **Queue wait** from run creation to `Build & test` starting: median 0.9 min, p75 3.5 min, p90 21 min, max 228 min.
  - The organization shares 20 concurrent jobs, and elsa-core uses many of them.
  - A snapshot at 14:02Z on 2026-10-22 had 14 runs of this repository queued, the oldest `CI` run waiting since 11:41Z (2 h 20 min). Only 3 runs of this repository were in progress, and elsa-core had 12.
  - Fewer, shorter jobs therefore matter as much as parallelism. This is why FR-022's budgets are kept (see [R9](#r9--job-budgets-and-layout)).
- **Long projects** (median / max in minutes):

  | Project | Median | Max | Tier (R5) |
  |---|---|---|---|
  | Elsa.Workbench.Tests | 27.6 | 31.1 | integration |
  | Elsa.Cluster.EntityFrameworkCore.Tests | 15.5 | 16.8 | integration |
  | Elsa.Activities.Compensation.Tests | 8.1 | 11.7 | PR |
  | Elsa.Activities.Http.IntegrationTests | 7.2 | 8.7 | integration |
  | Elsa.Cli.Tests | 5.2 | 6.5 | integration |
  | Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests | 3.9 | 4.9 | integration |
  | Elsa.Architecture.Tests | 3.8 | 5.7 | PR (own job) |
  | Elsa.Secrets.Workflows.Tests | 3.5 | 4.4 | PR |
  | Elsa3.Activities.Design.Import.Integration.Tests | 2.8 | 3.6 | PR |
  | Elsa.Etl.Tests | 1.8 | 4.3 | PR (child host, under 2 min) |
  | Elsa.Persistence.EntityFrameworkCore.Migrations.Tests | 1.8 | 2.1 | PR |

  Every other project's median is under 0.8 min.

- **`Elsa.Architecture.Tests` runs twice on every PR.** It runs once inside `Build & test`'s solution filter and again in the `Architecture guards` job. The plan runs it once, in `Architecture guards`.
- **EF container suites** (nightly run 39743917813): 23 legs plus the Secrets EF composition proof.
  - Each leg takes 1.1–6.1 min including about 1 min of checkout, setup and restore, about 62 runner-minutes in all.
  - The longest legs are `migrations` (6.1), `shared-persistence-fixture` (6.1), `cli-acceptance` (5.0) and `compensation` (4.8).

## R2 — Flaky tests

A test counts as flaky when it failed on a head that later passed unchanged, or when it failed on unrelated branches and on `main`.

| Test | Evidence | Tracking | Plan |
|---|---|---|---|
| `Elsa.Workbench.Tests.NativeHostingConformanceTests.Original_uncancelled_checkpoint_is_rejected_after_a_real_successor_acquires` | First attempt failed in 11 re-run runs, 4 of them on `main`; also 4 failed runs | #3147 (open) | quarantine |
| `Elsa.Workbench.Tests.NativeHostingGracefulHandoffTests.Draining_owner_hands_off_accepted_execution_while_survivor_admits_work` | 5 re-run first attempts, 1 failed `main` run | #3252 (open) | quarantine |
| `Elsa.Workbench.Tests.NativeHostingConformanceTests.Departed_routing_owner_is_reclaimed_and_its_real_transport_command_completes_on_survivor` | 2 re-run first attempts (one on `main`), 1 failed run | none | file an issue, quarantine |
| `Elsa.Workbench.Tests.WorkbenchBackgroundActivityRecoveryTests.Killed_workers_are_reclaimed_with_stable_invocations_and_one_native_continuation` | 4 failed runs on 4 branches, 2026-10-17 to 19 ("Timed out waiting for the real EF receipt") | none | file an issue, quarantine |
| `Elsa.Workbench.Tests.WorkbenchCompensationSampleTests.The_sample_workflows_end_as_the_readme_says` | Failed PR #3173's first attempt, timeout after 2 m 31 s | none (#3187 is a related compensation defect) | file an issue, quarantine |
| `Elsa.Activities.Http.IntegrationTests.ResponseReplaySafetyProcessTests.NormalPublication_PreservesExternalBaselineAndExecutesReplaySafeCandidate` | 2 failed runs on 2 branches, plus 1 re-run first attempt | none | file an issue, quarantine |
| `Elsa.Activities.Http.IntegrationTests.DurableDrainCycleTests.An_HTTP_triggered_Durable_sequence_of_200_WriteLines_completes_under_the_default_drain_cycle_bound` | 13 failed runs on 7 branches and `main`, 2026-10-16 to 17, none since `f3e5a96e9` (#2926) | #3213 (open, follow-up) | watch: no failure since the fix; quarantine only if it recurs |
| `Elsa.Expressions.JavaScript.Jint.Tests.StatementBindingTests.Statement_body_honors_cancellation_during_evaluation` | 2 failed runs on 2 branches, one on `main`, 2026-10-19 | none | file an issue, quarantine |

**Not flaky.** These failed on more than one branch, but the failures were real and the authors fixed them on their branches:

- `ActivityContractSurfaceSnapshotTests` (a baseline the PR changed);
- `ClrAssemblyScannerTests.BuiltInDescriptorHashes_MatchMainSnapshot` (hash snapshots);
- `EfCommandBudgetTests` (a budget the PR exceeded);
- `ActivityDriveHealthTests` and `ActivityOutcomeReachabilityTests` (a new activity without coverage).

A retry would not have hidden any of them: each fails deterministically on its head.

**Other failure causes** among the 53 failed jobs: 8 were locked-restore failures (NU1004, a stale `packages.lock.json`) and 4 were compile errors. Retries do not apply to either.

**Decision**: FR-014 is met by quarantining the seven tests marked "quarantine" with a tracking issue each. Five of those issues still need filing, as `needs-triage` with the evidence above. `DurableDrainCycleTests` is watched, not quarantined.

**Rationale**: quarantine keeps these tests running and visible (FR-012), and fixing them is test work outside this spec (Out of Scope).

**Alternatives**: quarantining by class was rejected, because it would hide the other tests in `NativeHostingConformanceTests`, which pass reliably.

## R3 — Per-class durations in the two long projects

- **Source**: TRX files from run 39790463068 (`Build & test`, 2026-10-22 14:19–15:00Z), on a 4-core runner shared with every other project, as in normal runs.
- **How to read the numbers**: "sum" adds a class's test durations. xUnit runs the tests of one class, and all classes of one collection, one after another, so a class's or collection's sum is the floor for the shard that runs it. Classes in different collections run in parallel.

### `Elsa.Workbench.Tests` (wall 26.0 min, 62 tests, 17 classes)

| Group | Classes (sum, min) | Serial floor |
|---|---|---|
| Collection `WorkbenchBackgroundActivityRecoveryTests.Name`, non-parallel | `WorkbenchBackgroundActivityRecoveryTests` 10.62, `NativeHostingConformanceTests` 4.54, `NativeHostingGracefulHandoffTests` 1.19, `WorkbenchProcessTests` 0.75 | **17.1** |
| Collection `WorkbenchCompensationRecoveryTests.Name`, non-parallel | `WorkbenchCompensationRecoveryTests` 2.41, `WorkbenchCompensationSampleTests` 0.48 | 2.9 |
| No shared collection, run in parallel | `CompositionHostAttestationProbeTests` 5.92, `CompositionActivationObservationTests` 3.35, `WorkbenchExtensionBuilderTests` 2.84, `WorkbenchShellActivationTests` 2.68, `WorkbenchModuleManagementReconcileTests` 1.47, `WorkbenchActivationGuardCompositionTests` 1.07, `WorkbenchDataProtectionTests` 1.04, `WorkbenchStudioCorsTests` 0.99, `WorkbenchShutdownTests` 0.49, `AuthoringFixtureHostEvidenceTests` 0.22, `NativeHostingScenarioTests` 0.00 | 5.9 (20.1 runner-min) |

- **Shards**: two.
  - `workbench-native-hosting`: the first collection, alone, about 17 min of tests.
  - `workbench-rest`: the other 13 classes, about 6–7 min in parallel plus the 2.9 min serial collection, about 10 min.
- **Consequence**: splitting further gains nothing. The first collection is the floor, and it holds 4 of the 7 quarantine candidates (R2). The project has 17 classes, not the 13 the spec's Evidence gives; that is corrected in the same change.

### `Elsa.Cluster.EntityFrameworkCore.Tests` (wall 13.2 min, 161 tests, 19 classes)

- **The collection**: `FoundationHostCollection.Name` holds 7 classes, which run one after another for 12.3 min: `WorkbenchHostProcessTests` 3.66, `FoundationHostReconcileTests` 2.87, `FoundationHostReloadRefusalTests` 2.17, `FoundationHostBootTests` 1.75, `FoundationHostClusterBootTests` 1.26, `FoundationHostDataProtectionBootTests` 0.40 and `FoundationHostEagerActivationRetryTests` 0.21.
- **The rest**: the other 12 classes add up to 1.0 min.
- **Shards**: `cluster-foundation-host` (the collection, about 12.3 min) gets a shard of its own. `cluster-rest` joins the other integration shard. The project is split as FR-002 requires, but the gain is only about 1 minute.

### `Elsa.Activities.Compensation.Tests` (PR tier; wall 7.7 min, 428 tests, 20 classes, no shared collection)

- **Largest classes**: `CompensationNestingJourneyTests` 7.12, `CompensationProviderSubsetTests` 6.41, `CompensationOperatorActionJourneyTests` 4.32, `CompensationScopeRestartTests` 3.71, `CompensationBackoffJourneyTests` 2.62 and `CompensationRetentionJourneyTests` 1.66. The other 14 classes add up to 1.7.
- **Groups**:
  - `compensation-nesting`: `CompensationNestingJourneyTests` alone, 7.1 min;
  - `compensation-provider`: `CompensationProviderSubsetTests` and `CompensationBackoffJourneyTests`, about 6.4 min in parallel;
  - `compensation-rest`: the other classes, about 4.3 min in parallel.
- **Consequence**: `CompensationNestingJourneyTests` (84 tests, 7.1 min) is the PR tier's floor. FR-002 splits by class, and test refactoring is out of scope, so no shard can finish its tests in less than about 7 minutes.

### Other integration projects

- `Elsa.Activities.Http.IntegrationTests` (wall 5.8): the largest classes are `ResponseReplaySafetyProcessTests` 4.83 and `DrainCyclesWithoutProgressTests` 4.35.
- `Elsa.Cli.Tests` (wall 4.4): the largest is `StatusClusterCliTests` 2.84.
- `Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests` (wall 3.1): the largest is 1.73.
- **Shard**: all three fit one shard, `integration-other`, together with `cluster-rest`, at about 6–8 min. None needs splitting.

## R4 — Build once and share, versus rebuild per shard

**Measured** in run 39790463068:

- **Archiving the outputs**: `Build & test` archived every `bin/Release` and `obj` directory under `Essentials`, `Extensions` and `tools` (642 directories) with `zstd -3`.
- **The probe job**: a separate job, `Spec 263 build-share probe`, downloaded and extracted that archive on a fresh runner, then ran `Elsa.Workflows.Runtime.Tests` from it with `--no-build`. It passed (2,530 tests), so a shared output is usable.

| Step | Build once and share | Rebuild per shard |
|---|---|---|
| Restore | in the build job only | 0.55 min per shard |
| Build | 3.1 min, once | 3.1 min per shard |
| Archive (`tar` + `zstd`, 4.2 GB result) | 0.55 min | — |
| Upload | 0.55 min | — |
| Download, per shard | **6.2 min** | — |
| Extract, per shard | 0.35 min | — |
| Added to the critical path | 3.1 + 0.55 + 0.55 + 6.2 + 0.35 = **10.8 min** before a shard can test | **3.65 min** |
| Runner time per shard | 6.55 min | 3.65 min |

- **Decision**: each shard restores and builds the full solution itself.
- **Rationale**: it is 7 minutes faster on the critical path and 3 minutes cheaper per shard in job-minutes. The second point removes the throughput argument for sharing raised under Risks in the plan. Building the full solution keeps every sibling output that tests read from disk, such as the `ExecutionProbe` that `Elsa.Workflows.Publishing.Tests` launches.
- **Alternatives**:
  - **sharing only each shard's test outputs**: smaller archives, but test projects copy most of the solution into their `bin`, so the saving is small and the download still lands on the critical path;
  - **building only the shard's projects**: rejected, because tests read sibling build outputs that their project references do not build. The `ExecutionProbe` above is one example.

## R5 — Tier of each test project

FR-017's hard traits are Testcontainers and starting a child process. Both were checked against every test project the CI test filter discovers (`Essentials/tests/**` and `Extensions/*/tests/**` outside `studio/`).

- **Testcontainers**: 25 projects reference it directly or through a referenced test project, read with the same `PackageReference`/`ProjectReference` parsing the solution-filter generator uses (`solution-filter-roots Elsa.Server.Persistence.Integration.slnf`). All of them are already the EF container suites in `tools/ci/ef-suites.json`, and they are integration tier.
- **Child processes.** A source scan for `Process.Start` and `ProcessStartInfo` in test code finds three kinds of launch:
  - **`dotnet` running an assembly**: a host, a worker, a probe or the `dotnet-elsa` CLI, through `"dotnet"`, `DOTNET_HOST_PATH` or a `DotnetMuxer()` helper. This is what FR-017 means by "a Workbench host, a child host process, the CLI process". It is the hard trait.
  - **`git`**: Architecture, Versioning.Calculator, Workflows.Design, Secrets.Workflows and ExtensionBuilder.Api tests run git against a temporary repository. These are millisecond tool calls, not hosts.
  - **`bash`** running a repository script: the Migrations tests run `tools/ef/module-migrate.sh`, and `SecretsEfMigrationGeneratorTests` runs a migration generator script. These are tools, not hosts.
- **Decision** (Joey Barten, 2026-10-22; FR-017 amended to match): the hard trait is a `dotnet` launch of an assembly or tool in a project whose median is **2 minutes or more**. The launch is detected in the project's own sources and in those of every test project it references. `git` and `bash` launches are never hard traits. A project with a short-lived child host and a median under 2 minutes stays in the PR tier.
- **Rationale**: the objective is less waiting without weakening per-commit feedback. The five child-host projects of 2 minutes or more hold nearly all the waiting (59 of the 61 child-host runner-minutes). The three under 2 minutes cost the PR tier about 2 minutes in all, and moving them would only take draft feedback away. The bar sits at 2 minutes rather than 1 because `Elsa.Etl.Tests` has a median of 1.8.
- **Alternatives**:
  - counting every `Process.Start`: rejected, because it would move about ten fast projects, among them `Elsa.Architecture.Tests`, out of the PR tier;
  - every `dotnet` launch regardless of duration, the plan's first draft: rejected for the three fast projects above;
  - a per-project opt-out: rejected, because it is a second way to assign a tier (FR-018).

**Duration rule** for projects without a hard trait: PR tier, unless the project's median would on its own exceed the PR-tier shard target ([R6](#r6--shard-target-and-class-splitting)) after splitting by class. No project meets that today, so every project without a hard trait is PR tier.

**Resulting integration tier** (non-container; the 25 container projects are integration too):

| Project | Hard trait | Median (min) |
|---|---|---|
| Elsa.Workbench.Tests | Workbench host processes | 27.6 |
| Elsa.Cluster.EntityFrameworkCore.Tests | Foundation and Workbench host processes | 15.5 |
| Elsa.Activities.Http.IntegrationTests | `ResponseReplayHost` child process | 7.2 |
| Elsa.Cli.Tests | the `dotnet-elsa` CLI process | 5.2 |
| Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests | `WorkerOidcHostFixture` child host | 3.9 |

**Child host, under 2 minutes, so PR tier**: `Elsa.Etl.Tests` (1.8, `EtlWorkerProcess`), `Elsa.Secrets.Persistence.EntityFrameworkCore.Tests` (0.2, `PersistenceCliProcessRunner`) and `Elsa.Workflows.Publishing.Tests` (0.1, the runtime-only execution probe). `rebalance` updates the medians; when one reaches 2 minutes, `test-tiers-check` fails until the project is moved.

**Correction to the spec's Assumptions.** `Elsa3.Activities.Design.Import.Integration.Tests` starts no child process: it uses the in-memory `Microsoft.AspNetCore.TestHost` (`Support/Elsa3ImportHost.cs`). At 2.8 min it stays PR tier by duration. The spec's Assumptions line is corrected in the same change.

**What counts as a test project.** A test project is a project under the test roots that references `Microsoft.NET.Test.Sdk`. Fixture and support projects under `tests/`, such as `Essentials/tests/Cli/Fixtures/*`, `Essentials/tests/Workbench/Fixtures/*` and `Essentials/tests/Cluster/Fixtures/*`, have no tests and carry no tier. The guard still follows their sources when a test project references them (FR-017's "through a referenced test project"), so a child host launched from a shared `Testing` or fixture project would count. Today every `dotnet` launch sits in the test project's own sources.

## R6 — Shard target and class splitting

- **Decision**: the PR-tier shard target is **6 minutes of test step** per shard, measured as the sum of the shard's project and class medians divided by the observed in-runner parallelism (2.4). A project whose median exceeds the target, or which would be the only unit on a shard and still exceed it, is split by test class.
  - **PR tier after FR-011's move**: only `Elsa.Activities.Compensation.Tests` (8.1) needs splitting.
  - **Before the move**: `Elsa.Workbench.Tests` and `Elsa.Cluster.EntityFrameworkCore.Tests` need it too.
  - **Floors**: a shard can never finish its tests faster than its largest class or shared collection ([R3](#r3--per-class-durations-in-the-two-long-projects)). Today the PR tier's floor is `CompensationNestingJourneyTests` (7.1) and the full tier's is Workbench's native-hosting collection (17.1). Those shards hold their floor plus only small projects, so the floor stays the bound.
- **Mechanism**:
  - A split project lists class groups in the manifest. A shard runs a group with `dotnet test <project> --filter "FullyQualifiedName~<Namespace.Class>.|…"`; the trailing dot stops `FooTests` from matching `FooTestsExtra`.
  - One group per split project is the **rest** group, filtered as the negation of all named classes, so a newly added class always runs somewhere.
  - A guard fails when two classes that share an `[Collection]` are in different groups. Members are read from `[Collection(…)]` attributes and resolved through `const string Name` fields.
- **Collections found**:
  - In `Elsa.Workbench.Tests`, 4 classes share `WorkbenchBackgroundActivityRecoveryTests.Name`: `NativeHostingConformanceTests`, `NativeHostingGracefulHandoffTests`, `WorkbenchBackgroundActivityRecoveryTests` and `WorkbenchProcessTests`. It is declared `DisableParallelization = true`. 2 classes share `WorkbenchCompensationRecoveryTests.Name` (`WorkbenchCompensationRecoveryTests`, `WorkbenchCompensationSampleTests`), also non-parallel.
  - In `Elsa.Cluster.EntityFrameworkCore.Tests`, 7 classes share `FoundationHostCollection.Name`, and 1 class has `ProcessExitCodeCollection.Name`.
  - The spec's Evidence said 3 Workbench classes share a collection; it is 4 plus 2, corrected in the same change.
- **Consequence**: the classes of one collection run one after another, so the collection's total is a floor for its shard. [R3](#r3--per-class-durations-in-the-two-long-projects) gives the totals: 17.1 and 2.9 min in Workbench, 12.3 min in Cluster EF.
- **Rationale**: test-class filters need no change to the tests, which matches the spec's assumption. The rest group protects against silent drops.
- **Alternatives**:
  - per-test hashing: rejected, because it splits collections;
  - xUnit's own parallelism settings: rejected, because they are per runner, not across runners.

## R7 — Retry and quarantine mechanics

- **Decision**: one script, `tools/ci/test_results.py`, owns retry, quarantine and reporting. Each shard runs:
  1. `dotnet test … --logger trx --results-directory <dir>/try1` with the shard's filter;
  2. `test_results.py failures <dir>/try1` → the failed tests' fully qualified names;
  3. when there is at least one failure, a single retry: `dotnet test … --filter "FullyQualifiedName=<a>|FullyQualifiedName=<b>"` → `<dir>/try2`;
  4. `test_results.py evaluate --quarantine tools/ci/test-tiers.json <dir>`. It exits non-zero only when a test that is not quarantined failed both attempts. It writes the job summary (failures, retried passes, quarantined failures with their issue links, tests over 10 s) and a `test-outcomes.json` artifact.
- **Theories**: xUnit's VSTest adapter gives a theory's cases one fully qualified name without arguments, so a retry re-runs every case of a failing theory. This is acceptable and simpler than case-level filters.
- **Hangs**: `--blame-hang-timeout 10m --blame-hang-dump-type none` turns a hung test into a named failure instead of a cancelled job. A test host that aborts fails the shard with the hanging test's name; the remaining tests of that project are not retried.
- **Recording retried passes (FR-004)**: pull requests from forks get a read-only token, so a shard cannot comment on an issue.
  - A new workflow, `ci-flake-record.yml`, runs on `workflow_run` (completed) for `CI` and `CI full tier`. It downloads the `test-outcomes` artifacts and updates one standing flake-tracking issue, labeled `ci-flake`, with a comment per retried-pass test.
  - It treats the artifact as data: test names are validated against a strict pattern before use, and nothing from it is executed.
  - This follows the `main-red-alert.yml` pattern of one deduplicated issue.
- **Quarantine guard (FR-013)**: `test_results.py check-quarantine` reads an entry's issue with `gh issue view --json state` and fails on a missing or closed issue. It needs only `issues: read`, which fork PRs have.
  - **On a PR** it runs with `--base <sha>` and checks only the entries that differ from the base's manifest. Checking the whole list there would make a required check on every PR depend on issue state no PR author controls: closing a flake issue, or merging a `Fixes #n` PR that leaves the entry in place, would fail `Build & test` on every open PR until a manifest PR lands and each PR re-runs (Code Review round 1, blocker 2).
  - **On pushes to `main` and nightly** it checks the whole list. A failure fails that run, and the existing `main is red` alert files or updates its issue, which names the entry.
- **Alternatives**:
  - `--retry` support in xUnit v2: not available; xUnit v3 changes the runner model, and migrating is out of scope;
  - third-party retry actions: not on the organization's allow-list (`github_owned_allowed` and verified creators only);
  - re-running the whole shard: rejected, because it doubles the slowest shard on any failure.

## R8 — Triggers, the required full-tier check, and draft handling

- **GitHub's rule**: a job skipped by `if:` reports as successful for a required check. Any design that can create a skipped check run under a required name, on a head whose real run failed, would let a failure merge. Three consequences follow.
  1. **The full tier lives in its own workflow, `ci-full.yml`** (name `CI full tier`), with `pull_request` types `[opened, reopened, synchronize, ready_for_review]`. `ci.yml` keeps its default types. Marking a PR ready therefore runs only the full tier; it does not re-run the PR tier on a head that already passed it.
  2. **No `labeled` trigger.** Any label added to a PR, such as `auto-fix` or `ready-for-agent`, would start a run, cancel the real one through the concurrency group, and leave a skipped or cancelled result. A `ci:full` label would have needed a third workflow with its own, never-required check name to avoid that. The spec drops the label (FR-008, as amended), and the manual run covers the early full tier.
  3. **On a draft, `CI full tier` reports no `Full tier` check at all.** A placeholder success would stay green on the head after it is marked ready, until GitHub delivers `ready_for_review` and creates the new run. When runners queue (2 h 20 min on 2026-10-22, R1), that window is long, and a merge that only checks "required checks green on head" would skip the full tier (Code Review round 1, blocker 1). The aggregate job therefore takes its name from the outcome:

     ```yaml
     name: ${{ (needs.plan.outputs.run == 'true' || !github.event.pull_request.draft) && 'Full tier' || 'Full tier (not run: draft)' }}
     ```

     A job's `name` may read the `github` and `needs` contexts. On a draft whose full tier did not run, the head carries only `Full tier (not run: draft)`. The required `Full tier` context is missing, so GitHub shows "Expected" and blocks the merge until a real run reports, with no red mark on the draft. If the draft's diff changes `tools/ci/test-tiers.json` or the shard tooling (the spec's "Manifest change" edge case), the full tier runs for real on that push and reports as `Full tier`.
     - Rejected: a failing placeholder. It closes the window too, but every draft shows a red check, which reads as a broken PR to authors and agents.
     - Rejected: a distinguishable placeholder that the merge docs and skills must check. It leaves the hole open for every merge path that does not check, including GitHub's own merge button.
- **Required context**: the aggregate job of `ci-full.yml` is named `Full tier` and calls nothing reusable, so the context is exactly `Full tier`. The test jobs come from a reusable workflow, `full-tier.yml` (`on: workflow_call`), shared by `ci-full.yml` and `ci.yml`'s push, schedule and dispatch runs. The aggregate is a plain job in each caller, so its context name is not prefixed.
- **Manual run (FR-008)**: `ci.yml` gains a `workflow_dispatch` input `tier` with choices `full` (default, today's behavior: everything, including the EF container suites) and `pr`. It is the one way to run the full tier on a draft. Its results appear in the Actions run and on the commit's checks, not under the required `Full tier` context. That is intended: marking the PR ready runs the required one.
- **Push to `main`, nightly, dispatch (FR-010)**: unchanged coverage.
  - Push runs the PR tier plus the non-container integration projects, which is what `Build & test` runs today.
  - Nightly and `tier: full` dispatch also run the EF container suites and the Secrets EF proof.
  - The alert job's `needs` gain the new full-tier jobs.
- **Ruleset**: `main-2` gains `Full tier` as a required context. This is an admin action (Joey Barten or Sipke Schoorstra). FR-011's move waits for it.
- **Alternatives**:
  - one workflow with all triggers: rejected, because `ready_for_review` would re-run the PR tier;
  - a `ci:full` label: dropped with the spec's FR-008 amendment, because a third workflow and a second check name cost more than the manual run they duplicate;
  - reporting the full tier through the Checks API from a `workflow_run`: rejected as more moving parts for the same result.

## R9 — Job budgets and layout

PR tier (`ci.yml`, required `Build & test`), after FR-011's move:

| Job | Kind | Notes |
|---|---|---|
| `Plan tiers` | plan, about 20 s | Python only: change-scope classifier, existing Python guards, and `test_tiers.py select` reading the manifest into a matrix. No .NET build on the critical path. |
| `PR shard (1..3)` | test ×3 | each: restore, full solution build ([R4](#r4--build-once-and-share-versus-rebuild-per-shard)), its projects or class groups, retry, evaluation |
| `Architecture guards` | test | unchanged job; `Elsa.Architecture.Tests` leaves the shards |
| `Core-only build & test` | test | unchanged |
| `Build & test` | aggregate | `needs` all of the above, `if: ${{ !cancelled() }}`; success only when the plan succeeded and every shard, `Architecture guards` and `Core-only` succeeded, or the change is documentation-only |

That is 5 test jobs, meeting FR-022's PR-tier limit of 5.

- **Shard sizing after the move** (R3 numbers):
  - **pr-1**: `compensation-nesting` (7.1) plus small projects.
  - **pr-2**: `compensation-provider` (about 6.4) with `Elsa.Secrets.Workflows.Tests` and `Elsa3.Activities.Design.Import.Integration.Tests`.
  - **pr-3**: `compensation-rest` with `Elsa.Persistence.EntityFrameworkCore.Migrations.Tests`, `Elsa.Etl.Tests` and the remaining small projects.
  - Each shard's test step is about 7–7.5 min, bounded by pr-1's floor.
  - **Critical path**: plan (0.5), runner setup and cache (1.0), restore (0.55), build (3.1) and tests (about 7.5) add up to about **12–13 min**. That is at SC-001's 12-minute target, not comfortably inside it, and queue wait comes on top.
  - **What could beat the floor**:
    - `CompensationNestingJourneyTests` was measured while every project shared the runner; on a shard with less contention it may run faster, and the first 20 runs after the move will show it.
    - Splitting that one class by test method would beat the floor, but it needs an FR-002 change, because FR-002 splits by class only. Speeding up the class itself is test refactoring, which is out of scope.

    Neither is done in this plan. SC-001 is measured as written, and a miss is reported for an owner decision.
- **Before the move**: the PR tier also carries the integration projects, in 6 shards plus the two unchanged jobs, so 8 test jobs (FR-022).
  - The 6 shards are `workbench-native-hosting`, `workbench-rest`, `cluster-foundation-host`, and pr-1 to pr-3, with `integration-other` and `cluster-rest` spread over pr-1 to pr-3.
  - The PR tier's wall time is about 22 min, bounded by `workbench-native-hosting`. SC-001 is measured after the move, because before it the bound is that collection, not the PR-tier design.

Full tier (`full-tier.yml`), at most 8 jobs:

| Job | Count | Content | Estimated wall time |
|---|---|---|---|
| `Plan` | 1 | classifier, draft decision, matrix | 0.5 |
| `Integration shard` | 4 | `workbench-native-hosting` (17.1); `workbench-rest` (about 10); `cluster-foundation-host` (12.3); `integration-other`: Http.IntegrationTests, Cli, Workflows.Runtime.Persistence.EF and `cluster-rest` (about 6–8) | test time + 4.65 setup, restore and build: about 22, 15, 17 and 13 |
| `EF container shard` | 3 | the 23 suites plus the Secrets EF proof, balanced by leg duration. Each shard restores and builds its container projects once, then runs its suites in sequence, arming each suite's required-provider mode for that suite only. About 37 runner-minutes of tests in all, about 12 per shard. | about 16 |

- **Job count**: 1 + 4 + 3 = 8, meeting FR-022's full-tier limit of 8. The `Full tier` aggregate lives in the caller and takes a few seconds; see [data-model](data-model.md#job-budget-accounting).
- **SC-002** (full tier within 25 min of ready): about **22 min**, bounded by `workbench-native-hosting`.
  - A retry inside that shard adds the retried test's own time, up to about 4.5 min for a `NativeHostingConformanceTests` case. That can push a run past 25 min.
  - Quarantined tests are retried like any other (FR-004), so this happens whenever one of them fails once. SC-002 is measured as written, and runs that retried are named in the measurement.

**SC-006**: a PR-tier run (5 test jobs + plan + aggregate) and a full-tier run (8) total 15 concurrent jobs at peak, within 20. The plan and aggregate jobs are short.

## R10 — Studio CI path filter

- **Decision**: `studio-ci.yml`'s `pull_request` trigger gains `paths:` for:
  - `Studio/**`;
  - `Extensions/*/studio/**`;
  - `Extensions/Weaver/**`, which `Studio/Elsa.Studio.slnx` references directly;
  - `global.json`, `NuGet.config`, `Directory.Packages.props`, `Directory.Build.props` and `.editorconfig`;
  - `.github/workflows/studio-ci.yml`;
  - `tools/ci/verify-studio-assets.py`;
  - `tools/ci/studio*`.

  `push` to `main` keeps no filter (FR-026).
- **Rationale**: none of Studio CI's checks is required, so a workflow-level path filter cannot leave a required check pending (the spec 257 R2 concern does not apply). `Studio/Directory.Build.props` does not import the root one, but the root `Directory.Packages.props` and `global.json` affect the shared SDK and package graph, so they are included to stay on the safe side.
- **Alternatives**: `dorny/paths-filter` is not on the allow-list and is not needed.

## R11 — Where the manifest and guards live

- **Decision**:
  - **`tools/ci/test-tiers.json`** is the manifest ([data-model](data-model.md)). It absorbs `tools/ci/ef-suites.json`: the container suites become `containerSuites` entries, so one file lists every test project (FR-015). `ef-suites-select` and `ef-suites-self-test` read the new file.
  - **`tools/ci/test_tiers.py`** (Python, no build) provides `select --tier pr|full` (the matrix), `check-docs` (FR-024 and FR-025) and `--self-test`. It runs in `Plan tiers`, off the .NET critical path.
  - **The generator** (`tools/maps/Elsa.Maps.Generator`) gets `test-tiers-check`: every test project named exactly once, no unknown project (FR-016); the Testcontainers and `dotnet`-launch traits through the test-project reference closure (FR-017); and collection integrity. It sits beside `ef-suites-self-test` in the `Generated maps fresh` job, which already scans the project graph and is required.
- **Rationale**: the reference-closure walk and `PackageReference` parsing already live in the generator (`solution-filter-roots`). Reusing them avoids a second parser that could disagree. Selection needs only the JSON, so it stays in Python and keeps the plan job fast.
- **Alternatives**:
  - putting everything in the generator: rejected, because it adds about a minute of build before every shard can start;
  - an `Elsa.Architecture.Tests` guard: rejected, because it runs in its own job and cannot gate `Build & test`'s plan.

## R12 — Constitution and documentation

- **FR-021** asks for the tier rule in "the constitution's testing section".
  - The framework's §2.23 is about unit-test shape, and §2.23.6 (integration testing) is Deferred. The Elsa constitution has no testing section (D9: no Elsa test-cadence gate).
  - **Decision**: add one row to the Elsa constitution's Gate index for the tier gate, naming `test-tiers-check` and the manifest. That is a MINOR bump (6.1.0 → 6.2.0, new Elsa guidance) and needs owner approval under Governance.
  - Also add the tier rule to `docs/contributing/review-checklist.md` and to the `elsa-add-unit-tests` skill.
- **FR-023**: a new section, "CI tiers", in `docs/contributing/merging.md`, between "PR evidence" and "Review and merge". It covers the two tiers, the draft rule, and `gh workflow run ci.yml --ref <branch> -f tier=full`.
- **FR-024**:
  - `elsa-auto-review-merge`, `elsa-quality-fix`, `speckit-implement` and `elsa-add-unit-tests` link to `docs/contributing/merging.md#ci-tiers`.
  - `elsa-quality-fix` changes from "open a ready PR" to "open a draft PR, then `gh pr ready` before requesting review".
  - `.specify/integrations/claude.manifest.json` hashes are refreshed for the changed skills.
  - The link guard is `test_tiers.py check-docs`.
