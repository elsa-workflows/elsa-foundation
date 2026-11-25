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
  - `extensions/Elsa3/src/Migration/Host/Elsa3.Migration.Host.csproj`: `Microsoft.NET.Sdk.Web`, `IsPackable` false, a
    `UserSecretsId`, and `<RunWorkingDirectory>$(MSBuildProjectDirectory)</RunWorkingDirectory>` (R-3, R-6);
  - `extensions/Elsa3/tests/Migration/Host/Tests/Elsa3.Migration.Host.Tests.csproj`: xunit,
    `Microsoft.AspNetCore.TestHost`.

  Then:
  - add both to `Elsa.Server.slnx`, and regenerate the solution filters and the dependency map;
  - register the test project as PR tier in `tools/ci/test-tiers.json`;
  - add the host to `HostProvidedPackagesGuardTests.SharingHosts`, and teach `NuplaneHostSettings.HostDirectory` and
    `HostsThatShareAssemblies` the host's path outside `apps/` (R-6);
  - run `ArchitectureGuardTests`.

  The path guard derives both paths from the `Elsa3.` bucket, so it should need no entry (R-1). If it does, raise that
  on the PR.

## Phase 2: Foundational (the host)

- [ ] T003 Derive the composition (R-4):
  - For each candidate, record the suite, template, Studio need or `required by <feature>` that justifies it. Read the
    S-15 acceptance definitions and Elsa 4 templates in `extensions/Elsa3/tests/JTest/`, and each feature's `DependsOn`.
  - Settle R-4's "to confirm" items, and drop every candidate without a reason.
  - Write the result to `extensions/Elsa3/src/Migration/Host/shells.json`, and the reasons table to the host's
    `README.md`.
  - Add a project reference for every composed feature's project, and nothing else beyond hosting and package loading
    (FR-003).
- [ ] T004 Admit the host in `EfCoreDependencyGuardTests` once T003's references are final: its name in
  `AllowedEfConsumers`, its reviewed package array (computed from the host's restore graph), and an arm in
  `ReviewedEfPackages` (R-9). Run the guard.
- [ ] T005 Write `MigrationHost.Build(string[] args)` and a `Program.cs` that calls it (R-2), with:
  - the configuration order of R-3 (`shells.Local.json` and user secrets re-added above the shells files);
  - eager default-shell activation and `/health/ready`;
  - the root health endpoint;
  - `AddEfPersistenceResources`, with `MigrationHostEfToolingShellDefaults` and its
    `[assembly: EfToolingShellDefaults(...)]` attribute;
  - `AddEfPendingMigrationActivationGuard`, `AddConfiguredDataProtection` and `AddShellStartupValidation`;
  - authentication, authorization and `AddOpenIddictEntityFrameworkCoreStore`;
  - Nuplane;
  - the Studio CORS policy;
  - creating `data/` when missing, and logging the absolute workflows folder, activities file and data paths.

  Use main's shared hosting helpers where the sync brought them. Copy nothing else that is specific to the Workbench.
- [ ] T006 Write `appsettings.json` and the settings in `shells.json` (R-6):
  - the `Elsa` resource at `data/elsa.db`, with `Migrate:Policy` `AutoMigrate`;
  - the `local-packages` directory feed on `packages/`, with the Nuplane loading settings narrowed to the host's
    closure;
  - development identity with the seeded administrator (the OpenIddict store is in memory in development mode);
  - `AllowedReturnUrlOrigins` and CORS for the Studio host's local address;
  - the two reconciliation features with `SourceId` `elsa3-migration`, `PublishOnReconcile` `true`, and the sample
    folder paths.

  Add `.gitignore` entries for `data/`, `shells.Local.json` and `packages/*` (keeping `.gitkeep`). Add the sample
  `migrated/workflows/hello.json` (an HTTP trigger and `WriteHttpResponse`) and `migrated/activities/activities.json`
  (`[]`) (FR-011).

**Checkpoint**: `dotnet run --project extensions/Elsa3/src/Migration/Host`, started from the repository root and from
another folder, starts; `/health/ready` returns 200; and the sample trigger answers.

## Phase 3: User Story 1 - Run my migrated workflows (P1)

- [ ] T007 [US1] Host tests through `MigrationHost.Build` with `UseTestServer`, each on a temporary data folder and
  migrated folder (R-5):
  - scenario 1: the sample is published and answers;
  - scenario 2: an activities file holds a composite with an activity dependency, a workflow uses it, both load, and
    running the workflow runs the composite (PR #5685 is merged);
  - scenario 4: an environment variable overrides a committed setting;
  - scenario 5: an unknown feature means the host refuses to start, naming it;
  - scenario 6: a missing workflows folder means the host refuses to start, naming it; so does a missing activities
    file.
- [ ] T008 [US1] Check by hand with the quickstart, and record the steps and results in the PR:
  - scenario 3, once for each route: a feature from a project reference, and a feature from a package in `packages/`;
  - SC-002: from a fresh clone, run a workflow from another `migrated/` folder by setting only the two folder settings
    (in `shells.Local.json` and in user secrets), with `git status` clean afterwards.

## Phase 4: User Story 3 - See which features my workflows need (P2)

- [ ] T009 [P] [US3] Host tests (R-5):
  - every feature in `shells.json` has a README row, and every README row names a composed feature; either failure
    names the feature;
  - every listed feature resolves, and a test fails naming any that doesn't;
  - no `Elsa3.*` assembly or Elsa 3 package is in the host's dependency closure;
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
  folder. Run `jtest run` with the S-15 Elsa 4 templates and an environment that differs only in base URL and
  credentials. Record the run in `extensions/Elsa3/tests/JTest/ledger/runs.md` in the ledger's existing columns
  (data-model.md, FR-015).
- [ ] T013 [US2] For each suite that passes on the Workbench and fails on the host, find the cause and route it as
  FR-014 says. Add a missing feature or setting to the composition with its reason, then rerun from T012. Keep every
  failed run's row.

## Phase 7: Polish

- [ ] T014 [P] Finish the host's README:
  - "local migration tool, don't deploy" (FR-006);
  - the two folder settings and where to set them (FR-011, R-3);
  - the package folder and the project-reference route (FR-005);
  - the development sign-in;
  - the reasons table;
  - the Studio host to use;
  - a pointer to #5800 for the GA composition.

  Link the README from the migration docs that spec 1238 owns, if they exist by then.
- [ ] T015 [P] Confirm FR-017 (no file under the Workbench host or its settings changed, and the S-15 Workbench run is
  unchanged) and FR-016 (no JTest project or step in `tools/ci/test-tiers.json` or the CI workflows).
- [ ] T016 Amend constitution §E2.7 to name `Elsa3.Migration.Host` as the local migration host, which references no
  Elsa 3 project, and add "and hosts the local migration target" to the `Elsa3` purpose in
  `docs/reference/elsa-domain-tree.md` (R-11).
- [ ] T017 Set this spec's status to `Implemented` in the finishing PR.

## Dependencies

- T001 blocks every other task.
- T002 blocks T003–T009.
- T003 blocks T004. T003–T006 block every user-story phase.
- T011 needs spec 1237's `inventory` and the merged S-15.
- US3 (T009) and US4 (T010) can run in parallel with US1 once T006 is done.
