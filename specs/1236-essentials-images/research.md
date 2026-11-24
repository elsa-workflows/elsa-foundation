# Research: Elsa essentials images

Facts are from `origin/initiative/3052-elsa3-to-elsa4-conversion` at `2db2d8a899` and the S-15 implementation branch
`agent/claude/3052-s15-jtest-impl` at `14d70f4d99` (PR #5662, not yet merged; it has moved since, so T004 re-reads the
acceptance set and templates from the merged S-15 or the then-current head).

## R-1 Host shape

- **Decision**: two new host projects, `apps/Elsa.Essentials.Server` and `Studio/src/apps/Elsa.Studio.Essentials`
  (spec Q1).
- **Rationale**: the Workbench compiles in every feature assembly it lists in `Program.cs` (about a hundred features in
  `shells.json`, BPMN, Weaver, agents and decisions among them). A profile would only switch the shells file; the
  binary would still carry the extras, and the migration CLI could not take its closure from it.
- **Alternatives**: a Workbench build argument choosing a baked shells file (rejected with Q1); the
  `Elsa.Foundation.Host` image with the essentials packages baked into a second Nuplane feed (every feature loaded as a
  package at startup: slower boot, and the CLI would have to restore the same packages to compose its closure).
- **Name**: `Elsa.Server` and `elsaworkflows/elsa-server` are reserved for the clean product host (constitution §E5).
  `Elsa.Essentials.Server` and `elsaworkflows/elsa-server-essentials` do not take that name and do not claim to be that
  host.

## R-2 One feature list

- **Decision**: one JSON file, `essentials.features.json`, kept in a small library
  `apps/Elsa.Essentials.Composition`. Each entry holds the feature name, its baked
  settings and its reason. The library also holds the project references of every listed feature, an
  `EssentialsFeatureAssemblies` list for feature discovery, and the settings merge (R-3). The engine host and the
  migration CLI's Elsa 3 entry point both reference this library.
- **Rationale**: the list, the assemblies that implement it and the merge must move together. A library both consumers
  reference keeps them in one place without either consumer referencing the other, and keeps the CLI free of the
  engine host (FR-018). The library carries no feature of its own.
- **Every composed feature is listed**, including those another feature's `DependsOn` brings in, with the reason
  "required by X". The guard (R-6) can then compare the composed set with the list exactly.
- **Alternatives**: a CShells-shaped `shells.json` (no place for a reason per feature, and users would recognise and
  edit it); the list in the host project (the CLI would reference a web host); an MSBuild items file (the CLI could not
  read settings from it).
- **Placement**: next to the hosts, `apps/Elsa.Essentials.Composition`, because the library groups features from every
  domain the way a host does; placed under a domain such as `Elsa.Foundation` it would make that domain depend on all
  the others (constitution §E2.1 treats hosts as groupings, not domains). Its tests sit in `essentials/tests/Essentials/`
  like the Workbench's in `essentials/tests/Workbench/`. If `ArchitectureGuardTests.Project_paths_match_domain_tree_convention`
  or the migration CLI's reference rules refuse this, the implementation raises it on the PR before moving it.

## R-3 Settings merge

- **Decision**: configuration layers, lowest first: the essentials feature list (as `CShells:Shells:default:Features`
  keys), the image's baked `appsettings.json` (host defaults: logging, paths, persistence resource, Nuplane feed), the
  user's `/app/config/appsettings.json`, environment variables, command line. One extension method in
  `Elsa.Essentials.Composition` adds the first three layers to an `IConfigurationBuilder`; the host and the CLI both call
  it.
- **Rationale**: the user file overrides any baked key and adds features under the same `CShells` path the composition
  uses, which is how the Workbench layers `shells.json`, `shells.{Environment}.json` and environment variables today
  (`apps/Elsa.Workbench/Program.cs` lines 106–118). Mounting the user file in its own folder keeps it from shadowing the
  baked `appsettings.json`.
- **No `shells.Production.json`**: the essentials image has one environment shape; the secrets FR-008 names are
  blank in the list and validated at start (R-5).

## R-4 The migrated folder

- **Decision**: `JsonWorkflowReconciliation` in the list with `Options.FolderPath` `/app/migrated/workflows`, `SourceId`
  `essentials`, `PublishOnReconcile` `true` (spec Q4). The folder exists in the image and is owned by the runtime user.
- **Rationale**: the feature reads a flat folder in ordinal file-name order and fails the pass when the folder is
  missing (`JsonWorkflowReconciliationOptions.FolderPath`); an empty folder contributes nothing. `PublishOnReconcile`
  needs `WorkflowsPublishing` in the same shell, which the list carries.
- **Activities file**: `JsonActivityReconciliation` with `Options.FilePath` `/app/migrated/activities/activities.json`
  and `SourceId` `essentials`, next to `ActivitiesDesignReconciliation`, which its feature documentation says it needs.
  The feature takes a file, not a folder, and the reader needs the file to exist, so the image ships
  `/app/migrated/activities/activities.json` containing `[]` and an empty `/app/migrated/workflows/`; a mounted
  `migrated/` replaces both. T017 confirms what a missing file does when a user mounts a folder without it.
- **Interface to the CLI spec**: `migrated/workflows/` is a flat folder of files in this feature's catalog format (an array of
  workflow version models). If the CLI spec chooses another layout, one of the two specs changes before either is
  implemented.

## R-5 Secrets and refusal at start

- **Decision**: the secrets blanked by the Workbench's `shells.Production.json` (`Secrets:EncryptionKey`,
  `SeedAdminPassword`, `FoundationIdentityOpenIddict` signing key, `WorkflowsRuntimeEntityFrameworkCore`
  `RecoveryContinuationSigningKey` and `HierarchyCursorSigningKey`, the workflow alteration payload protection key)
  have no baked value, `Secrets:EncryptionKey` only if the list ends up composing Secrets. The OpenIddict signing key
  is not blanked by that file, which only turns `IsDevelopmentOrDemo` off; the essentials list bakes
  `IsDevelopmentOrDemo: false` for both identity features and no signing key, so a production key must come from the
  user. `Elsa:ModuleManagement:ApiKey` is not required: it serves only Studio's feature management, which the
  essentials Studio does not compose. Features already refuse activation without them; the task
  checks each refusal names the setting and adds a startup check only where one does not.
- **Rationale**: build nothing for a refusal that already exists; the spec only requires that it names the setting.
- **Example settings**: `docker/compose/essentials/appsettings.example.json` lists every required key with a
  "change me" value and a one-line comment per key.

## R-6 Guard

- **Decision**: a PR-tier test project `essentials/tests/Essentials/Tests` with:
  - every listed feature resolves in `EssentialsFeatureAssemblies` (fails naming it);
  - the composed shell's feature set equals the list (fails naming the difference);
  - no assembly in the engine host's dependency closure is an Elsa 3 assembly (in addition to
    `Elsa3MigrationBoundaryTests`);
  - the merge order: a user key overrides a baked key, an environment variable overrides the user key, an added
    feature appears in `CShells:Shells:default:Features`;
  - every entry has a non-empty reason.
- **Rationale**: these are the risks the spec names (drift, Elsa 3 leaking in, wrong merge order); constitution
  §2.23 risk-based tests. `FeatureCompositionTests` already proves each feature composes.

## R-7 Engine feature list, candidates

Derived from the S-15 acceptance definitions (activity types used: Flowchart, FlowDecision, FlowFork, FlowJoin,
FlowSwitch, If, Switch, For, ForEach, ParallelForEach, While, WriteLine, SetVariable, SetOutput, SetName, Correlate,
RunJavaScript, HttpEndpoint, WriteHttpResponse, DispatchWorkflow, BulkDispatchWorkflows, ExecuteWorkflow, PublishEvent,
Event, Complete, one reusable workflow used as an activity; expressions Literal, JavaScript and Object, no Liquid),
the S-15 Elsa 4 templates (identity login and token, `runtime/workflows/executables`, instances page and detail,
activity executions, `runtime/workflows/diagnostics/settings`), the S-15 run configuration (checkpoint persistence in
`Durable` mode) and the essentials Studio's backend needs. Names are CShells feature names (most as in the Workbench's `shells.json`; `JsonWorkflowReconciliation` is in its catalog but
not enabled there).

| Group | Candidates | Reason |
|---|---|---|
| Host infrastructure | Primitives, Serialization, Mediator, Events, MemoryCache, Tasks, FileSystemDistributedLocking, ApiCapabilities | required by the features below (reconciliation is a single-node task and needs a lock provider) |
| Identity | FoundationIdentityAbstractions, FoundationIdentityApi, FoundationIdentityAspNetCoreIdentity, FoundationIdentityAspNetCoreIdentityEntityFrameworkCore, FoundationIdentityOpenIddict, IdentityIamEntityFrameworkCore | `elsa-authenticate` template (login, token); Studio sign-in |
| Expressions | Expressions, ExpressionsApi, JavaScriptExpressions, JavaScriptJintEngine, JavaScriptActivities, JavaScriptWorkflowsDesign, JavaScriptEndpoints | JavaScript expressions and `RunJavaScript`; the designer's expression editor |
| HTTP | Http, HttpJavaScript, ActivitiesHttp, WorkflowsRuntimeHttp | six HTTP suites; `HttpEndpoint`, `WriteHttpResponse` |
| Activities | ActivitiesRuntime, ActivitiesPrimitives, ActivitiesControlFlow, ActivitiesSequence, ActivitiesFlowchart, ActivitiesGraphDesign, ActivitiesGraphRuntime, ActivitiesScripting, ActivitiesDispatchWorkflowRuntime, ActivitiesDispatchWorkflowDesign, ActivitiesDesignApi, ActivitiesDesignReconciliation, ClrActivityReconciliation, ActivitiesDesignEntityFrameworkCore | the activity types above; the activity catalog the designer and publishing read |
| Workflows design | WorkflowsDesignApi, WorkflowDesignValidations, WorkflowsDesignEntityFrameworkCore, JsonWorkflowReconciliation, JsonActivityReconciliation | the workflows folder and the activities file; the designer |
| Publishing | WorkflowsPublishing, WorkflowsPublishingApi, WorkflowsPublishingEntityFrameworkCore | publish on reconcile; publish from Studio |
| Runtime | WorkflowsRuntimeEntityFrameworkCore, WorkflowsRuntimeApi (with its workflow alteration payload protection key settings), WorkflowsRuntimeTriggers, WorkflowsRuntimeResumption, WorkflowsRuntimeScheduling, WorkflowsRuntimeCheckpointPersistence, WorkflowsRuntimeHistory, WorkflowsExecutionEvidence | executables, instances and activity-execution reads; correlation and event resumption; dispatch; diagnostics settings |
| Events | the feature that provides `PublishEvent` and the `Event` trigger | the events suite |

**To confirm in implementation** (T-tasks): whether dispatch needs `WorkflowsRuntimeDistributed` and its two EF
features; which feature provides `PublishEvent`/`Event` (a standalone event trigger feature or the runtime triggers);
whether `JavaScriptEndpoints`, `ApiCapabilities` and `IdentityIamEntityFrameworkCore` are needed by the essentials
Studio or by nothing. A candidate without a confirmed reason is dropped. Not in the list (FR-002): everything BPMN,
Weaver, Agent, Decisions, Connections, Secrets (unless an identity feature requires it), dashboards, Studio
preferences, standalone trigger authoring not needed by a suite, the diagnostics OpenTelemetry and structured-log
features, compensation, attention, `ModularityApi` and Liquid.

**Liquid**: no acceptance definition uses Liquid, so it is not in the list. A user adds it in `appsettings.json` with
its package in the packages folder, as any other feature (FR-005, FR-007).

## R-8 Studio feature list

- **Decision**: `StudioApi`, `BackendsStudio`, `WorkflowsStudio`, `JavaScriptExpressionEditorStudio`,
  `ThemeManagement`, `ThemePicker`, `ThemeStoreCore`, plus the sign-in module the essentials engine's identity needs
  (to confirm: built-in sign-in of `StudioApi`/`BackendsStudio`, or `ExternalAuthenticationStudio`). The baked list
  lives in the Studio essentials app itself; Studio has no CLI consumer, so it needs no shared library.
- **Not composed**: Dashboard, Attention, UserTasks, ConsoleStream, OpenTelemetry, StructuredLogs, Secrets, Agent,
  Connections, Decisions, Etl, WeaverWorkflows, WorkflowsDashboard, FeatureManagement, ExtensionBuilder, Liquid editor,
  WeatherForecastSample.
- **UX acceptance**: the composed proof (`tools/ux-acceptance/run.py`) builds the Workbench and Studio. The essentials
  journey needs the essentials pair instead; the task adds a host selection to the runner, or records a manual walk
  with screenshots and accessibility snapshots under the same evidence rules if that change grows beyond a flag.

## R-9 Persistence

- **Decision**: the persistence resource `Elsa` points at `Data Source=/app/data/elsa.db`; every EF feature in the
  list uses that one connection (S-15 found that the Elsa 3 import must share the Activities Design store's database;
  sharing one database avoids the same class of mismatch here). OpenIddict's own store also moves under `/app/data`.
  `Migrate:Policy` is `AutoMigrate` in the image, because the essentials image has no separate migration step; a user
  can set `Validate` and run migrations from CI/CD.
- **Rationale**: the user's go-live path is one container; the data folder is the one volume they mount.
- **Other providers**: a user sets the resource's `Provider` and `ConnectionString` (for example PostgreSQL), as the
  Workbench reference stack does. No provider work in this spec.

## R-10 Images and CI

- **Decision**: `apps/Elsa.Essentials.Server/Dockerfile` follows the Workbench Dockerfile (restore with lock files,
  ReadyToRun publish, computed package versions, non-root user, health check) and adds `/app/config` and the `/app/migrated` skeleton (R-4).
  `Studio/src/apps/Elsa.Studio.Essentials/Dockerfile` follows the Studio Web Dockerfile. `docker.yml` and
  `studio-docker.yml` build and push them next to the existing images with the same tags.
- **Images build only from `main`**: an initiative-branch head is checked by a local `docker build` (as S-15 does);
  the images are published once the initiative lands.
- **Compose**: `docker/compose/docker-compose.essentials.yml` with the example settings and the four mounts.

## R-11 Acceptance run

- **Decision**: the S-15 Elsa 4 templates and environment unchanged except base URL and credentials (the essentials
  admin password comes from the user settings, not a seeded development value). The run record is a row in the S-15
  ledger `extensions/Elsa3/tests/JTest/ledger/runs.md` with engine `essentials`, so both runs on one head sit side by
  side. Capture is set through the diagnostics settings API after start, as in S-15.
- **Before the CLI exists** (FR-022): a hand-written Elsa 4 definition `docker/compose/essentials/migrated/workflows/hello.json`
  (HTTP trigger, `WriteHttpResponse`) doubles as the quickstart example.
- **Ordering**: the final acceptance task waits for the CLI's `inventory` command and for S-15's Elsa 4 run on the
  same head.

## R-12 Base image for derived images

- **Decision**: the engine reads its baked composition from a folder, `/app/composition/`: `essentials.features.json`
  first, then every other `*.features.json` in ordinal file-name order (the essentials image holds only the first), and registers
  `/app/composition/packages` as a second Nuplane directory feed next to the user's `/app/packages`. An image built
  `FROM` the engine adds a list file (for example `elsa3.features.json`) and its packages there. Its features are baked
  like the essentials ones; the user's `appsettings.json` applies over both.
- **Rationale**: the Elsa 3 migration image is built `FROM` the engine at the same version (Joey on PR #5669, spec Q7)
  and must add the converter and import features without copying the essentials list. Packages are the extension
  path the engine already has for user features, so a derived image needs no rebuilt host. Keeping baked packages out
  of `/app/packages` means a user's mount there cannot hide them.
- **Proof**: a test Dockerfile, `essentials/tests/Essentials/DerivedImage/Dockerfile`, builds `FROM` the
  local engine image, adds a list naming the Liquid feature and the locally packed Liquid package, and starts; a
  Liquid expression in a definition in the workflows folder then evaluates (SC-007). It is a manual check recorded in
  the PR evidence, not a CI job.
- **Alternatives**: the derived image rebuilds its own host from source with extra project references (it then ships a
  second engine binary and the "same composition" promise rests on two builds staying in step); environment variables
  in the derived Dockerfile (settings only, cannot add assemblies).
