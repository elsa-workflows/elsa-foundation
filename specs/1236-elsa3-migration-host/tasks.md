# Tasks: Elsa 3 migration host

**Input**: [spec.md](spec.md), [plan.md](plan.md), [research.md](research.md), [data-model.md](data-model.md),
[contracts/](contracts/), [quickstart.md](quickstart.md)

**Tests**: The Elsa constitution declares no test-first cadence (framework §2.21.2), so tests sit next to the code they
cover. The required tests are the host tests in R-5 (constitution §2.23, risk-based). No feature class is added, so no
feature-registration test applies. Acceptance is the JTest run (US2).

**Builds**: Build and test only the projects a task touches (AGENTS.md, *Builds on a shared machine*). On Windows, use a
short worktree path (MSB3030).

## Format: `[ID] [P?] [Story] Description`

## Phase 1: Setup

- [ ] T001 **Blocking.** Confirm a coordinator main sync has reached the initiative branch and brought
  `Elsa.Foundation.Identity.OpenIddict.EntityFrameworkCore` (`AddOpenIddictEntityFrameworkCoreStore`) with it (R-7).
  If it hasn't, stop and ask the coordinator. Don't copy or move store code. After the sync, use the guard lists and
  Workbench paths as they are then (`apps/Elsa.Workbench.Host` on main).
- [ ] T002 Create the project and its test project:
  - `apps/Elsa.Migration.Host/Elsa.Migration.Host.csproj`: `Microsoft.NET.Sdk.Web`, `IsPackable` false, a
    `UserSecretsId`, `<RunWorkingDirectory>$(MSBuildProjectDirectory)</RunWorkingDirectory>`, and a
    `Properties/launchSettings.json` with `ASPNETCORE_ENVIRONMENT=Development` and one fixed local URL not used by
    another host in the repository (R-2, R-3, R-6);
  - `essentials/tests/Migration/Host/Tests/Elsa.Migration.Host.Tests.csproj`: xunit,
    `Microsoft.AspNetCore.TestHost`.

  Then:
  - add both to `Elsa.Server.slnx`, and regenerate the solution filters and the dependency map;
  - register the test project as PR tier in `tools/ci/test-tiers.json`, in exactly one PR shard;
  - add `Elsa.Migration.Host` to `ArchitectureGuardTests.ExpectedProjectPath` and `ExpectedSolutionFolder` the way
    `Elsa.Essentials.Host` is listed, and run `ArchitectureGuardTests` (R-1).

## Phase 2: Foundational (the host)

- [ ] T003 Derive the composition (R-4):
  - For each candidate, record the suite, template, Studio need or `required by <feature>` that justifies it. Read the
    S-15 acceptance definitions and Elsa 4 templates in `extensions/Elsa3/tests/JTest/`, and each feature's `DependsOn`.
  - Settle R-4's "to confirm" items, and drop every candidate without a reason.
  - Write the result to `apps/Elsa.Migration.Host/shells.json`, and the reasons to
    `composition-reasons.json` next to it (feature name → reason).
  - Add a project reference for every composed feature's project, and nothing else beyond hosting (FR-003).
- [ ] T004 Admit the host in `EfCoreDependencyGuardTests` once T003's references are final: its name in
  `AllowedEfConsumers`, its reviewed package array (computed from the host's restore graph), and an arm in
  `ReviewedEfPackages` (R-9). Run the guard.
- [ ] T005 Write `MigrationHost.CreateBuilder(string[] args)`, `MigrationHost.Build(WebApplicationBuilder builder)` and
  a `Program.cs` that calls both (R-2). Start from main's `apps/Elsa.Essentials.Host/Program.cs`, and name in the PR
  each piece dropped and why. Include:
  - the configuration order of R-3 (`shells.Local.json` and user secrets re-added above the shells files);
  - `AddFoundationShellHosting` with `WithHostAssemblies()` and `MapFoundationHealth` (`/health/live`,
    `/health/ready`);
  - `AddEfPendingMigrationActivationGuard`, `AddConfiguredDataProtection` and `AddShellStartupValidation`;
  - the JSON baseline: `AddElsaJsonResponseHeaders()` and `ConfigureHttpJsonOptions` with `JsonBaseline.Configure`
    and `StrictDateTimeConverters.Configure(..., apiMode: true)`, as the Workbench registers them (R-2);
  - authentication, authorization and `AddOpenIddictEntityFrameworkCoreStore`;
  - the Studio CORS policy;
  - `AddEfSchemaReadability`, `AddConfiguredClusterMembership`, `AddWorkflowRuntimeRunnabilityReport` and the
    shell-activation attention contributor (R-2);
  - the host added to every host list of `HostOwnedServicesAreSharedWithShellsTests` (`Hosts`,
    `FoundationNuplaneHosts` and each theory's `[InlineData]` host set), with a project reference from
    `essentials/tests/Modularity/Tests/Elsa.Modularity.Tests.csproj` (lock file updated), and an
    `appsettings.Development.json` beside `appsettings.json`;
  - creating `data/` when missing, and logging the absolute workflows folder, activities file and data paths.

  `AddFoundationNuplane` with no feed and no shared assemblies, so no package loads (R-6). Every host on main has at
  least one feed, so confirm on the first run that Nuplane starts without one; if it doesn't, configure one empty
  directory feed under the git-ignored `data/` folder, still with no shared assemblies, and say so in the PR.
- [ ] T006 Write `appsettings.json` and the settings in `shells.json` (R-6):
  - `ConnectionStrings:Elsa` set to `Data Source=data/elsa.db`, with `Migrate:Policy` `AutoMigrate`;
  - development identity with the seeded administrator (the OpenIddict store is in memory in development mode);
  - the development-only demo keys R-6 lists (runtime recovery continuation and hierarchy cursor signing keys, the
    workflow alteration payload protection keys);
  - `AllowedReturnUrlOrigins` and CORS for the Studio host's local address (the Studio host's own fixed URL);
  - `WorkflowsRuntimeCheckpointPersistence` with `Mode` `Durable`, as the S-15 run sets it (R-10);
  - the two reconciliation features with `SourceId` `elsa3-migration`, `PublishOnReconcile` `true`, and the sample
    folder paths.

  Add `.gitignore` entries for `data/`, `shells.Local.json` and `nuplane.lock.json`. Add the sample
  `migrated/workflows/hello.json` (an HTTP trigger and `WriteHttpResponse`, with a fixed `definitionId`) and `migrated/activities/activities.json`
  (`[]`) (FR-011).

**Checkpoint**: `dotnet run --project apps/Elsa.Migration.Host`, started from the repository root and from
another folder, starts; `/health/ready` returns 200; and the sample trigger answers.

## Phase 3: User Story 1 - Run my migrated workflows (P1)

- [ ] T007 [US1] Host tests through `MigrationHost.CreateBuilder`, `UseTestServer` and `MigrationHost.Build`, with
  `--environment=Development` and `--contentRoot` set, each on a temporary data folder and migrated folder (R-5):
  - scenario 1: the sample is published and answers;
  - scenario 2: an activities file holds a composite with an activity dependency, a workflow uses it, both load, and
    running the workflow runs the composite (PR #5685 is merged);
  - scenario 4: an environment variable overrides a committed setting;
  - scenario 5: an unknown feature means the host refuses to start, naming it;
  - scenario 6: a missing workflows folder means the host refuses to start, naming it; so does a missing activities
    file.
- [ ] T008 [US1] Check by hand with the quickstart, and record the steps and results in the PR:
  - scenario 3: a feature from a project reference the engineer adds to the host project, listed in
    `shells.Local.json`;
  - SC-002: from a fresh clone, run a workflow from another `migrated/` folder by setting only the two folder settings
    (in `shells.Local.json` and in user secrets), with `git status` clean afterwards.

## Phase 4: User Story 3 - See which features my workflows need (P2)

- [ ] T009 [P] [US3] Host tests (R-5):
  - every feature in `shells.json` has an entry in `composition-reasons.json`, and every entry names a composed
    feature; either failure names the feature (no test reads the README, framework §2.23.2);
  - every listed feature resolves, and a test fails naming any that doesn't;
  - the host's `project.assets.json` has no `Elsa3.*` package or project and no `Elsa*` package of major version 3
    (R-5);
  - none of the feature families FR-002 names is composed.

## Phase 5: User Story 4 - Studio (P3)

- [ ] T010 [US4] Start the repository's Studio host against the migration host. Sign in, open a definition, edit an
  expression, publish, and see the host run the new version. Record it in the PR, with the Studio host used and any
  Studio module that expects a capability the host doesn't advertise (R-8). Name that Studio host in the README.

## Phase 6: User Story 2 - JTest acceptance (P1)

- [ ] T011 [US2] Wait for all three:
  - the CLI's `inventory` command (spec 1237);
  - the merged S-15 acceptance with a Workbench run on the head under test;
  - T007.

  Then convert the S-15 acceptance definitions with `inventory`, with the approved adjusted copies in place of their
  originals (FR-013).
- [ ] T012 [US2] Start the host from that head with an empty `data/` folder and the `inventory` output as its migrated
  folder. Set capture through `PUT runtime/workflows/diagnostics/settings` as the S-15 README does. Run `jtest run` with
  the S-15 Elsa 4 templates and `environments/elsa4.json`, with its variables set as R-10 lists
  (`ELSA_API_BASE_URL`, `ELSA_EXECUTOR_BASE_URL` to `<host URL>/workflows/http`, `JTEST_CONFIG_FILE`, the admin variables, `CORRELATION_ID`,
  `DATE_NOW_ISO_STRING`). Record the run in `extensions/Elsa3/tests/JTest/ledger/runs.md` in the ledger's existing columns
  (data-model.md, FR-015).
- [ ] T013 [US2] For each suite that passes on the Workbench and fails on the host, find the cause and route it as
  FR-014 says. Add a missing feature or setting to the composition with its reason, then rerun from T012. Keep every
  failed run's row.

## Phase 7: Polish

- [ ] T014 [P] Finish the host's README:
  - "local migration tool, don't deploy" (FR-006), and that running the built assembly outside `dotnet run` needs
    `ASPNETCORE_ENVIRONMENT=Development`;
  - the two folder settings and where to set them (FR-011, R-3);
  - adding your own features by project reference (FR-005);
  - the development sign-in;
  - the reasons, shown from `composition-reasons.json`;
  - the Studio host to use;
  - a pointer to #5800 for the GA composition.

  Link the README from the migration docs that spec 1238 owns, if they exist by then.
- [ ] T015 [P] Confirm FR-017 (no file under the Workbench host or its settings changed, and the S-15 Workbench run is
  unchanged) and FR-016 (no JTest project or step in `tools/ci/test-tiers.json` or the CI workflows).
- [ ] T016 Set this spec's status to `Implemented` in the finishing PR.

## Dependencies

- T001 blocks every other task.
- T002 blocks T003–T009.
- T003 blocks T004. T003–T006 block every user-story phase.
- T011 needs spec 1237's `inventory` and the merged S-15.
- US3 (T009) and US4 (T010) can run in parallel with US1 once T006 is done.
