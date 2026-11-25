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

- [ ] T001 Create the project and its test project:
  - `extensions/Elsa3/src/Migration/Host/Elsa3.Migration.Host.csproj` (`Microsoft.NET.Sdk.Web`, `IsPackable` false);
  - `extensions/Elsa3/tests/Migration/Host/Tests/Elsa3.Migration.Host.Tests.csproj` (xunit,
    `Microsoft.AspNetCore.TestHost`).

  Then:
  - add both to `Elsa.Server.slnx`, and regenerate the solution filters and the dependency map;
  - register the test project as PR tier in `tools/ci/test-tiers.json`;
  - admit the host in `EfCoreDependencyGuardTests`: its name in `AllowedEfConsumers`, its reviewed package array, and
    an arm in `ReviewedEfPackages` (R-9);
  - run `ArchitectureGuardTests`, `EfCoreDependencyGuardTests` and `HostProvidedPackagesGuardTests`, adding the host
    to `SharingHosts` if that guard asks for it (R-6).

  The path guard derives both paths from the `Elsa3.` bucket, so it should need no entry (R-1). If it does, raise
  that on the PR.
- [ ] T002 **Blocking.** Confirm a main sync has brought `Elsa.Foundation.Identity.OpenIddict.EntityFrameworkCore`
  (`AddOpenIddictEntityFrameworkCoreStore`) to the initiative branch (R-7). If it hasn't, stop and ask the coordinator.
  Don't copy or move store code.

## Phase 2: Foundational (the host)

- [ ] T003 Derive the composition (R-4):
  - For each candidate, record the suite, template, Studio need or `required by <feature>` that justifies it. Read the
    S-15 acceptance definitions and Elsa 4 templates in `extensions/Elsa3/tests/JTest/`, and each feature's `DependsOn`.
  - Settle R-4's "to confirm" items, and drop every candidate without a reason.
  - Write the result to `extensions/Elsa3/src/Migration/Host/shells.json`, and the reasons table to the host's
    `README.md`.
  - Add a project reference for every composed feature's project, and nothing else beyond hosting and package loading
    (FR-003).
- [ ] T004 Write `Program.cs` with the wiring R-2 lists:
  - eager default-shell activation and `/health/ready`;
  - the root health endpoint;
  - `AddEfPersistenceResources`, with `MigrationHostEfToolingShellDefaults` and its
    `[assembly: EfToolingShellDefaults(...)]` attribute;
  - `AddEfPendingMigrationActivationGuard`, `AddConfiguredDataProtection` and `AddShellStartupValidation`;
  - authentication, authorization and `AddOpenIddictEntityFrameworkCoreStore`;
  - Nuplane;
  - the Studio CORS policy.

  Use main's shared hosting helpers where the sync brought them. Copy nothing else that is specific to the Workbench.
- [ ] T005 Write `appsettings.json` and the settings in `shells.json` (R-6):
  - the `Elsa` resource at `data/elsa.db`, the OpenIddict store at `data/elsa-identity.db`, and `Migrate:Policy`
    `AutoMigrate`;
  - the `local-packages` directory feed on `packages/`, with the Nuplane loading settings narrowed to the host's
    closure;
  - development identity with the seeded administrator;
  - `AllowedReturnUrlOrigins` and CORS for the Studio host's local address;
  - the two reconciliation features with `SourceId` `elsa3-migration`, `PublishOnReconcile` `true`, and the sample
    folder paths.

  Confirm what the two features resolve relative paths against, and make the paths absolute from the content root at
  startup if they resolve against the working directory. Add `.gitignore` entries for `data/` and `packages/*` (keeping
  `.gitkeep`).
- [ ] T006 Add the sample `migrated/workflows/hello.json` (an HTTP trigger and `WriteHttpResponse`) and
  `migrated/activities/activities.json` (`[]`) (FR-011).

**Checkpoint**: `dotnet run --project extensions/Elsa3/src/Migration/Host` starts, `/health/ready` returns 200, and
the sample trigger answers.

## Phase 3: User Story 1 - Run my migrated workflows (P1)

- [ ] T007 [US1] Host tests for scenarios 1, 4, 5 and 6, run in-process on a temporary data folder and migrated folder
  (R-5):
  - the sample is published and answers;
  - an environment variable overrides a committed setting;
  - an unknown feature means the host refuses to start, naming it;
  - a missing workflows folder means the host refuses to start, naming it.
- [ ] T008 [US1] After PR #5685 merges: a host test for scenario 2, where an activities file holds a composite with an
  activity dependency, a workflow uses it, both load, and the run runs the composite.
- [ ] T009 [US1] Check scenario 3 by hand with the quickstart, once for each route:
  - a feature from a project reference;
  - a feature from a package in `packages/`.

  Record the steps and results in the PR.

## Phase 4: User Story 3 - See which features my workflows need (P2)

- [ ] T010 [P] [US3] Host tests (R-5):
  - the README reasons table matches `shells.json`;
  - every listed feature resolves, and a test fails naming any that doesn't;
  - no `Elsa3.*` assembly or Elsa 3 package is in the host's dependency closure;
  - none of the feature families FR-002 names is composed.

## Phase 5: User Story 4 - Studio (P3)

- [ ] T011 [US4] Start the repository's Studio host against the migration host. Sign in, open a definition, edit an
  expression, publish, and see the host run the new version. Record it in the PR, with the Studio host used and any
  Studio module that expects a capability the host doesn't advertise (R-8). Name that Studio host in the README.

## Phase 6: User Story 2 - JTest acceptance (P1)

- [ ] T012 [US2] Wait for all four:
  - the CLI's `inventory` command (spec 1237);
  - the merged S-15 acceptance with a Workbench run on the head under test;
  - PR #5685;
  - T007.

  Then convert the S-15 acceptance definitions with `inventory`, with the approved adjusted copies in place of their
  originals (FR-013).
- [ ] T013 [US2] Start the host from that head with an empty `data/` folder and the `inventory` output as its migrated
  folder. Run `jtest run` with the S-15 Elsa 4 templates and an environment that differs only in base URL and
  credentials. Record the run in `extensions/Elsa3/tests/JTest/ledger/runs.md` with engine `migration-host` (FR-015).
- [ ] T014 [US2] For each suite that passes on the Workbench and fails on the host, find the cause and route it as
  FR-014 says. Add a missing feature or setting to the composition with its reason, then rerun from T013. Keep every
  failed run's row.

## Phase 7: Polish

- [ ] T015 [P] Finish the host's README:
  - "local migration tool, don't deploy" (FR-006);
  - the two folder settings (FR-011);
  - the package folder and the project-reference route (FR-005);
  - the development sign-in;
  - the reasons table;
  - the Studio host to use;
  - a pointer to #5800 for the GA composition.

  Link the README from the migration docs that spec 1238 owns, if they exist by then.
- [ ] T016 [P] Confirm FR-017: no file under the Workbench host or its settings changed, and the S-15 Workbench run is
  unchanged.
- [ ] T017 Set this spec's status to `Implemented` in the finishing PR.

## Dependencies

- T002 blocks T004 and every later task.
- T001 blocks T003–T010.
- T003–T006 block every user-story phase.
- T008 needs PR #5685. T012 needs spec 1237's `inventory`, the merged S-15 and PR #5685.
- US3 (T010) and US4 (T011) can run in parallel with US1 once T006 is done.
