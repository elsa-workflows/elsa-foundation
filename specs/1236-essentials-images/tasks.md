# Tasks: Elsa essentials images

**Input**: [spec.md](spec.md), [plan.md](plan.md), [research.md](research.md), [data-model.md](data-model.md),
[contracts/](contracts/), [quickstart.md](quickstart.md)

**Tests**: The Elsa constitution declares no test-first cadence (framework §2.21.2), so tests sit next to the code they
cover. Required tests: the guard and merge tests (R-6, constitution §2.23 risk-based); no feature class is added, so no
feature-registration test applies. Acceptance is the JTest run (US2) and the Studio UX acceptance (US3).

**Builds**: build and test only the projects a task touches (AGENTS.md, *Builds on a shared machine*); use a short
worktree path on Windows (MSB3030).

## Format: `[ID] [P?] [Story] Description`

## Phase 1: Setup

- [ ] T001 Create `apps/Elsa.Essentials.Composition/Elsa.Essentials.Composition.csproj` (no feature class, internal
  by default) and `essentials/tests/Essentials/Tests/Elsa.Essentials.Tests.csproj` (xunit); add
  both to `Elsa.Server.slnx`, register the test project as PR tier in `tools/ci/test-tiers.json`, add
  `Elsa.Essentials.Composition` and the test project to `ArchitectureGuardTests.ExpectedProjectPath` and
  `ExpectedSolutionFolder` the way `Elsa.Foundation.Host` is listed, add `Elsa.Essentials` to the host and test groupings
  in constitution §E2.1, add the library's reviewed EF closure (it resolves EF through the listed features) to
  `EfCoreDependencyGuardTests.AllowedEfConsumers`, and run `ArchitectureGuardTests.Project_paths_match_domain_tree_convention` (R-2).
- [ ] T002 [P] Create `apps/Elsa.Essentials.Server/Elsa.Essentials.Server.csproj` referencing
  `Elsa.Essentials.Composition`, `Elsa.Foundation.Hosting` and the Nuplane/CShells hosting packages the Workbench uses,
  importing `apps/ComputedProductionSettings.targets` as the Workbench does; add it to
  `HostProvidedPackagesGuardTests.SharingHosts`, to `Elsa.Server.slnx`, to the guard's `ExpectedProjectPath` and
  `ExpectedSolutionFolder`, and its reviewed EF closure to `EfCoreDependencyGuardTests.AllowedEfConsumers`.
- [ ] T003 [P] Create `Studio/src/apps/Elsa.Studio.Essentials/Elsa.Studio.Essentials.csproj` modelled on
  `Studio/src/apps/Elsa.Studio.Web/Elsa.Studio.Web.csproj`, referencing only the Studio modules R-8 lists; add it to
  the Studio solution.

## Phase 2: Foundational (the list and the merge)

- [ ] T004 Derive the engine feature list (R-7): for each candidate, record the suite, template, Studio need or
  `required by <feature>` that justifies it, reading the S-15 acceptance definitions and Elsa 4 templates in
  `extensions/Elsa3/tests/JTest/` and the `DependsOn` of each feature; settle the R-7 "to confirm" items; drop every
  candidate without a reason. Write `apps/Elsa.Essentials.Composition/essentials.features.json` in the format of
  [contracts/feature-list.md](contracts/feature-list.md), with `JsonWorkflowReconciliation` and `JsonActivityReconciliation` set as R-4 describes, no
  secret values and `IsDevelopmentOrDemo: false` on both identity features (R-5). Bake what the S-15 run sets outside
  its environment file: checkpoint persistence in `Durable` mode, the HTTP activity base path the Elsa 4 environment's
  executor URL uses, the seeded admin user name `admin`; and confirm which feature provides the `Variable` expression
  type.
- [ ] T005 Add the project references of every listed feature to `Elsa.Essentials.Composition.csproj` and list their
  assemblies in `apps/Elsa.Essentials.Composition/EssentialsFeatureAssemblies.cs`.
- [ ] T006 Implement `EssentialsFeatureList` (reads `essentials.features.json` first, then every other `*.features.json` in the composition folder in ordinal file-name order, R-12) and
  `EssentialsConfigurationExtensions.AddEssentialsConfiguration` (list → baked settings → optional user file, R-3) in
  `apps/Elsa.Essentials.Composition/`.
- [ ] T007 Write the guard and merge tests in `essentials/tests/Essentials/Tests/` (R-6): every entry has a
  reason and resolves in `EssentialsFeatureAssemblies`; a user key overrides a baked key; an environment variable
  overrides the user key; an added feature appears under `CShells:Shells:default:Features`; no assembly in the
  library's closure is an Elsa 3 assembly.
- [ ] T008 Restore the new projects' `packages.lock.json` (only the affected projects) and run `EfCoreDependencyGuardTests`
  and `FeatureCompositionTests`; admit the library where §2.20 requires it, with the reason.

- [ ] T008a Move the OpenIddict vendor store out of the Workbench into `apps/Elsa.Essentials.OpenIddict/` (R-13): the
  SQLite, PostgreSQL and SQL Server DbContexts with their migrations (identifiers unchanged), the initializer, the store
  and platform providers, the migration policy and pruning, with neutral registration names. Make
  `apps/Elsa.Workbench/Program.cs` call the library and keep its direct `OpenIddict.EntityFrameworkCore` reference;
  repoint `essentials/tests/Modularity/ProviderTests/WorkbenchOpenIddictEngineTests.cs` and `OpenIddictEngineDatabases.cs`;
  change `OpenIddictPersistenceArchitectureTests.Workbench_owns_the_vendor_package` to "only host projects in `apps/`
  reference the vendor package"; add the library's reviewed EF closure to `EfCoreDependencyGuardTests` and leave the
  Workbench's unchanged; add it to the guard's `ExpectedProjectPath`/`ExpectedSolutionFolder`. Confirm the Workbench's
  tests and an existing Workbench SQLite token store still migrate and issue tokens unchanged (FR-017).

**Checkpoint**: the list, its assemblies and the merge exist and are guarded; the CLI spec can reference the library.

## Phase 3: User Story 1 - Start the engine with my own files (P1) 🎯 MVP

**Goal**: the engine composes the list, reads `/app/config/appsettings.json`, loads user packages and imports and
publishes `/app/migrated/workflows`.

**Independent Test**: User Story 1 scenarios 1–7 with the example settings and `hello.json`.

- [ ] T009 [US1] Write `apps/Elsa.Essentials.Server/Program.cs`: configuration via `AddEssentialsConfiguration`
  (baked `appsettings.json`, user file `config/appsettings.json` under the content root, which is
  `/app/config/appsettings.json` in the image), then environment variables and command line; CShells with `EssentialsFeatureAssemblies` and Nuplane
  feature discovery over `/app/packages`, as `apps/Elsa.Workbench/Program.cs` does for its feature catalog; and this host
  wiring, which is not a feature: the readiness endpoint `/health/ready` with eager activation of the default shell at
  startup (the Workbench's `Readiness/` `DefaultShellWarmup` and `MapShellReadiness`, not its lazy default), the CORS
  policy reading `Cors:AllowedOrigins`, the root health endpoint, and the host registrations the listed features rely
  on: `AddEfPersistenceResources` (with an essentials `IEfToolingShellDefaults` type and its
  `[assembly: EfToolingShellDefaults(...)]` attribute, as `WorkbenchEfToolingShellDefaults` is for the Workbench), `AddEfPendingMigrationActivationGuard`, `AddConfiguredDataProtection`,
  `AddShellStartupValidation`, `AddAuthentication`/`AddAuthorization`, and the OpenIddict vendor store (EF DbContext,
  SQLite migrations, store initializer, store provider, migration policy, pruning) from the shared library (T008a, Q10). Copy nothing
  else Workbench-specific (boot timeline, composition observation endpoints, Weaver, OpenTelemetry bridge).
- [ ] T010 [US1] Write `apps/Elsa.Essentials.Server/appsettings.json`: persistence resource `Elsa` on
  `Data Source=/app/data/elsa.db`, every EF feature on that resource, OpenIddict's SQLite token store at `/app/data/elsa-identity.db`,
  `Migrate:Policy` `AutoMigrate`, Data Protection keys under `/app/data` (R-9), and the Nuplane loading settings the
  Workbench bakes (`Nuplane:Loading:SharedAssemblies`, `Nuplane:HostProvidedPackages`), narrowed to the essentials
  closure, so user packages load against the image's shared assemblies (FR-007).
- [ ] T011 [US1] Add a guard test in `essentials/tests/Essentials/Tests/` that composes the shell from
  `Elsa.Essentials.Composition` alone (the list, `EssentialsFeatureAssemblies` and CShells, no web listener and no
  reference to `Elsa.Essentials.Server`) and asserts the composed feature set equals the list, naming
  any difference (FR-018).
- [ ] T012 [US1] Check each required setting of [contracts/engine-image.md](contracts/engine-image.md): start the host
  with it empty and confirm it refuses to start as spec.md defines it (`/health/ready` never 200, the log
  names the setting); where one does not, add a startup check in the host that
  names it. Cover each refusal in a test in `essentials/tests/Essentials/Tests/`. Update the contract's
  table to the confirmed list.
- [ ] T013 [US1] Confirm that an added feature without a package refuses start and names the feature (scenario 4);
  add a host check and a test only if the existing refusal does not name it.
- [ ] T014 [P] [US1] Write `docker/compose/essentials/appsettings.example.json` with every required setting as a
  `change-me` value and a comment key per setting, and `docker/compose/essentials/migrated/activities/activities.json` (`[]`), and
  `docker/compose/essentials/migrated/workflows/hello.json`, an Elsa 4
  JSON workflow reconciliation catalog with one HTTP-triggered workflow writing a fixed response (FR-022).
- [ ] T015 [US1] Write `apps/Elsa.Essentials.Server/Dockerfile` from the Workbench Dockerfile (locked restore,
  ReadyToRun, computed versions, non-root user, health check), with the health check on `/health/ready`, creating `/app/config`, the `/app/migrated` skeleton (empty `workflows/`, `activities/activities.json` of `[]`),
  `/app/packages` and `/app/data` owned by the runtime user; build it locally.
- [ ] T016 [US1] Check FR-003 and SC-004: list every assembly in the built image's `/app` and confirm none is an Elsa 3 assembly and every Elsa assembly belongs to a listed feature or to hosting and
  package loading;
  record the command and result in the PR evidence.
- [ ] T017 [US1] Run User Story 1 scenarios 1–7 (scenario 7 once the engine fix for source-owned composites with
  dependencies has landed on the initiative branch) against the local image (example settings, `hello.json`, the
  locally packed Liquid feature package for scenario 2 with a definition that uses a Liquid expression, a changed baked
  setting for scenario 3 (`PublishOnReconcile: false`, checking `hello.json` is imported but not published, FR-009), a missing package, a missing secret, no `migrated/` mount, a mounted `migrated/` without an
  activities file, and for scenario 7 a composite in `activities.json` used by a workflow), plus the import-failure
  edge case (a malformed file next to `hello.json`, and a mounted `migrated/` without an activities file: shell activation
  fails, `/health/ready` never returns 200, the log names the file); record each
  result in the PR evidence.

**Checkpoint**: the engine image runs a user's workflow with only a settings file (SC-002 for the engine).

## Phase 4: User Story 2 - JTest acceptance against the engine image (P1)

**Goal**: the S-15 acceptance set gives the same per-suite result on the engine image as on the Workbench.

**Independent Test**: the acceptance run in [quickstart.md](quickstart.md).

**Prerequisites**: US1; spec 1235's Elsa 4 templates and a Workbench run on the same head; the migration CLI's
`inventory` command; the engine fix for source-owned composites with dependencies (reusable-using suites).

- [ ] T018 [US2] Write the essentials run section of `extensions/Elsa3/tests/JTest/README.md`: build the image, run
  the CLI's `inventory` over the acceptance definitions (adjusted copies in place of originals), start the container
  with `migrated/` at `/app/migrated` and a run settings file, wait for `/health/ready` = 200, set capture, run the S-15 Elsa 4 command with the
  container's base URL and credentials.
- [ ] T019 [US2] Run it on the initiative head; record the run in `extensions/Elsa3/tests/JTest/ledger/runs.md` with
  engine `essentials` (data model, *Acceptance run record*).
- [ ] T020 [US2] For every suite that passes on the Workbench and fails here, find the cause; add the missing feature
  or setting to `essentials.features.json` with its reason (and its project reference, T005), or fix the host; run
  again. A converter cause belongs to S-15's failure rule, and an export or `migrated/` writing cause to the
  migration CLI's specification (FR-020), not here.
- [ ] T021 [US2] Record the final comparison (SC-001): zero suites pass on one engine and fail on the other; S-15
  exemptions carried as they are.

## Phase 5: User Story 3 - Designer-only Studio (P2)

**Goal**: Studio with only the workflow designer, baked, against the engine.

**Independent Test**: User Story 3 scenarios with the essentials compose file.

- [ ] T022 [US3] Before coding, check the Studio UX acceptance record in spec.md (User Story 3) against the
  confirmed R-8 module list and correct it in the same PR if the list changed a state.
- [ ] T023 [US3] Write `Studio/src/apps/Elsa.Studio.Essentials/Program.cs` and its feature assemblies (R-8 list, the
  sign-in module confirmed), `shells.json` baked (not mounted), `appsettings.json` reading `Studio:BackendBaseUrl`, and the user file at `/app/config/appsettings.json`.
- [ ] T024 [US3] Write `Studio/src/apps/Elsa.Studio.Essentials/Dockerfile` from the Studio Web Dockerfile; build it
  locally.
- [ ] T025 [US3] Write `docker/compose/docker-compose.essentials.yml`: both images, the four engine mounts, the Studio
  origin in the engine's CORS and identity return-URL settings through the example settings file (FR-016). Then walk [quickstart.md](quickstart.md) from a clean folder, changing only the
  `change-me` values, and confirm `hello.json` runs and Studio signs in (SC-002).
- [ ] T026 [US3] Composed proof: add an essentials host selection to `tools/ux-acceptance/run.py` and a journey for
  T022's states, or, if that grows beyond a host selection, run the journey by hand with screenshots and accessibility
  snapshots retained as a downloadable archive bound to the commit; check that navigation shows no module the engine
  lacks (SC-006).
- [ ] T027 [US3] Independent UX acceptance review in the review loop, as `docs/contributing/studio-ux-acceptance.md`
  requires.

## Phase 6: User Story 4 - One feature list (P2)

**Goal**: the CLI can consume the list without the engine host.

**Independent Test**: User Story 4 scenarios.

- [ ] T028 [US4] Add a test in `essentials/tests/Essentials/Tests/` that reads the list and composes the
  configuration through `Elsa.Essentials.Composition` alone, with no reference to `Elsa.Essentials.Server` (scenario 3);
  confirm the T011 guard fails naming a feature when one is removed from `EssentialsFeatureAssemblies` (scenario 2).
- [ ] T029 [US4] Leave a hand-over note for the migration CLI specification in the PR: the library name and path, the
  three public members, that it is not packed and must be shared in the migration tooling's Nuplane setup (R-12), and
  the `migrated/` layout (R-4, FR-010, FR-025).

- [ ] T030 [US4] In the Dockerfile, copy `essentials.features.json` to `/app/composition/`; make
  `apps/Elsa.Essentials.Server/Program.cs` read it from there through `EssentialsFeatureList.Read`. Add a test in
  `essentials/tests/Essentials/Tests/` that a second list file in the folder adds a feature and merges key by key over
  an essentials entry, and that the essentials list is read first whatever the other file is called (scenario 4).
- [ ] T031 [US4] Check the built image against the base-image layout in
  [contracts/engine-image.md](contracts/engine-image.md) (list path, library and feature assemblies in `/app`, the
  entry point replaced with `docker run --entrypoint`) and record it in the PR evidence (SC-007). Document the layout in
  `docs/docker.md` (T033).

## Phase 7: Polish

- [ ] T032 [P] Add `elsa-server-essentials` to `.github/workflows/docker.yml` and `elsa-studio-essentials` to
  `.github/workflows/studio-docker.yml`, same tags and version stamping as their siblings (FR-015).
- [ ] T033 [P] Add an essentials section to `docs/docker.md`: what the images contain, the four mounts, the required
  settings, how to add a feature, and that the composition cannot be edited.
- [ ] T034 Regenerate the solution filters and check maps (`dotnet run --project tools/maps/Elsa.Maps.Generator -- check`;
  on Windows compare with `git diff --stat`).
- [ ] T035 Confirm the Workbench's behaviour, its image contents, its compose files and S-15's Workbench run are
  unchanged after the token-store move (FR-017).
- [ ] T036 Set the spec status to Implemented in the PR that finishes the work.

## Dependencies

- Phase 1 → Phase 2 → US1. US2 needs US1, S-15's Elsa 4 run on the same head and the CLI's `inventory`. US3 needs
  US1 (an engine to sign in to). US4 needs Phase 2 and T011; T030–T031 need T009 and T015.
- T004 before T005–T007. T011 needs T009. T020 may change T004's list; rerun T007 and T011 after it.

## Parallel examples

- Phase 1: T002 and T003 together after T001.
- US1: T014 while T009–T013 are in progress.
- US3 (T022–T025) and US4 (T028–T029) can run while US2 waits for the CLI.
- Polish: T032 and T033 together.

## Implementation strategy

1. MVP: Phases 1–3. The engine image runs user workflows; the CLI spec can reference the library.
2. US3 and US4 next; they do not wait for the CLI.
3. US2 last: it closes SC-001 once the CLI's `inventory` exists.
