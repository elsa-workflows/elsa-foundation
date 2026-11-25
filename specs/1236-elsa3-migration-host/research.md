# Research: Elsa 3 migration host

Facts come from `origin/initiative/3052-elsa3-to-elsa4-conversion` at `2db2d8a899` unless a line says otherwise, and
from the S-15 implementation branch `agent/claude/3052-s15-jtest-impl` (PR #5662, not yet merged). T003 re-reads the
acceptance set and templates from the merged S-15, or from the head current at that time.

## R-1 Host shape and location

- **Decision.** One ASP.NET Core project, `Elsa3.Migration.Host`, at
  `extensions/Elsa3/src/Migration/Host/Elsa3.Migration.Host.csproj`. It references the projects of the composed
  features directly. It is not packed, has no Dockerfile, and is not built by an image workflow (spec Q1, Q3, Q11).
- **Why this path.** `ArchitectureGuardTests` registers `Elsa3.` as an extension bucket, so `Elsa3.Migration.Host`
  gets the name-derived path `extensions/Elsa3/src/Migration/Host/`. Its tests get
  `extensions/Elsa3/tests/Migration/Host/Tests/`, the same way `Elsa3.Mapping` and `Elsa3.Mapping.Tests` get theirs.
  The guard needs no new entry, and the constitution's host groupings (§E2.1) need no amendment.
- **Elsa 3 boundary.** The project sits in the Elsa 3 bucket but references no `Elsa3.*` project and no Elsa 3
  package (FR-003). A test checks its dependency closure (R-5).
- **Alternatives.**
  - A Workbench profile: rejected with Q1.
  - A host in `apps/`: that is where the product and demo hosts live (`Elsa.Essentials.Host` and the Workbench on
    main). It would need a guard entry, and would sit next to the GA story that #5800 owns.
  - Published images and a base image (the earlier design): replaced by Q11.

## R-2 Host wiring

- **Decision.** Two public methods hold the host: `MigrationHost.CreateBuilder(string[] args)` returns the configured
  `WebApplicationBuilder`, and `MigrationHost.Build(WebApplicationBuilder builder)` builds and maps the app. The
  top-level statements call both. The tests call `CreateBuilder`, add `UseTestServer` to the builder's web host, then
  call `Build` (R-5), because the test server must be set before the app is built. The wiring starts from main's
  `apps/Elsa.Essentials.Host/Program.cs` after the sync (the closest host to what this one composes); each piece it
  drops is named with its reason in the implementing PR. It contains at least:
  - CShells shell hosting with the default shell activated eagerly at startup, and readiness at `/health/ready` (the
    Workbench's `DefaultShellWarmup` and `MapShellReadiness`);
  - the root health endpoint;
  - `AddEfPersistenceResources`, with a migration-host `IEfToolingShellDefaults` type and its
    `[assembly: EfToolingShellDefaults(...)]` attribute, as `WorkbenchEfToolingShellDefaults` is for the Workbench;
  - `AddEfPendingMigrationActivationGuard`, `AddConfiguredDataProtection` and `AddShellStartupValidation`;
  - `AddAuthentication`/`AddAuthorization`, and the OpenIddict token store from main's
    `Elsa.Foundation.Identity.OpenIddict.EntityFrameworkCore` (`AddOpenIddictEntityFrameworkCoreStore`, R-7);
  - a CORS policy for the repository's Studio host (R-8);
  - the services the composed features resolve from the host, as main's essentials host registers them
    (`AddEfSchemaReadability`, `AddConfiguredClusterMembership`, `AddWorkflowRuntimeRunnabilityReport` and the CShells
    ASP.NET Core endpoints), kept or dropped by what the composed features need.

  It leaves out the boot timeline, the composition observation endpoints, Weaver, the OpenTelemetry bridge, Nuplane
  package loading (R-6) and the module-management API.
- **Environment.** `Properties/launchSettings.json` sets `ASPNETCORE_ENVIRONMENT=Development` and one fixed local URL.
  `DevelopmentOrDemoGuard` refuses to start a host whose identity features run in development mode outside the
  Development environment, and the Workbench gets Development the same way. The tests set the builder's environment to
  Development. The fixed URL is what the Studio CORS policy, the return URLs and the JTest environment file use.
- **After the main sync.** If the sync brings in main's shared hosting helpers (main's `apps/Elsa.Essentials.Host`
  uses `AddFoundationShellHosting` and `MapFoundationHealth`), the host uses them instead of copying the Workbench's.
  The behaviour above stays the same.

## R-3 Host composition

- **Decision.** The composition is the host's `shells.json`, in the CShells shape the Workbench uses
  (`CShells:Shells:default:Features`). It is one file that tools read by path (FR-004).
- **Reasons.** A machine-readable `composition-reasons.json` next to `shells.json` maps each composed feature name to
  its reason. CShells' feature section has no room for comments: the Workbench's `shells.json` has none, and a
  `// key` inside `Features` would be read as a feature name. A host test compares the two JSON files and fails,
  naming the feature, on any difference (FR-004, SC-003). Framework §2.23.2 forbids tests that assert on markdown, so
  the README shows the reasons (rendered from the file, or linking to it) and is not what the test reads.
- **Every composed feature is listed**, including the ones another feature's `DependsOn` brings in. Their reason is
  "required by X". The test can then compare the composed set with the list exactly.
- **Configuration layers.** On the Workbench, `CreateBuilder` adds `appsettings*.json`, user secrets (in
  Development, when the project has a `UserSecretsId`), environment variables and the command line. `Program.cs` then
  adds `shells.json` and `shells.{Environment}.json`, and re-adds only environment variables and the command line
  (`apps/Elsa.Workbench/Program.cs` lines 112–124). So user secrets and `appsettings*.json` sit below the shells files
  and can't override a feature setting. The migration host keeps that order and then re-adds, before environment
  variables and the command line:
  1. a git-ignored `shells.Local.json` (optional);
  2. user secrets, from a `UserSecretsId` set in the project.

  Lowest first, the host's order is: `appsettings*.json`, `shells.json`, `shells.{Environment}.json`,
  `shells.Local.json`, user secrets, environment variables, command line. A local, uncommitted source can then change
  any setting or add a feature (FR-005). This is ordinary `IConfigurationBuilder` calls; no merge library.

## R-4 Engine feature candidates

The candidates come from four sources:
- **The S-15 acceptance definitions.** Activity types used: Flowchart, FlowDecision, FlowFork, FlowJoin, FlowSwitch,
  If, Switch, For, ForEach, ParallelForEach, While, WriteLine, SetVariable, SetOutput, SetName, Correlate,
  RunJavaScript, HttpEndpoint, WriteHttpResponse, DispatchWorkflow, BulkDispatchWorkflows, ExecuteWorkflow,
  PublishEvent, Event, Complete, and one reusable workflow used as an activity. Expressions: Literal, JavaScript, Object
  and Variable. No Liquid.
- **The S-15 Elsa 4 templates.** Identity login and token, `runtime/workflows/executables`, the instances page and
  detail, activity executions, and `runtime/workflows/diagnostics/settings`.
- **The S-15 run configuration.** Checkpoint persistence in `Durable` mode.
- **The Studio designer's backend needs** (Q8).

Names are CShells feature names, most as in the Workbench's `shells.json`. `JsonWorkflowReconciliation` is in the
Workbench's catalog but not enabled there.

| Group | Candidates | Reason |
|---|---|---|
| Host infrastructure | Primitives, Serialization, Mediator, Events, MemoryCache, Tasks, FileSystemDistributedLocking, ApiCapabilities | Required by the features below. `Tasks` resolves `IDistributedLockProvider` in `TaskExecutor`, so a lock provider must be composed. |
| Identity | FoundationIdentityAbstractions, FoundationIdentityApi, FoundationIdentityAspNetCoreIdentity, FoundationIdentityAspNetCoreIdentityEntityFrameworkCore, FoundationIdentityOpenIddict, IdentityIamEntityFrameworkCore | The `elsa-authenticate` template (login, token); Studio sign-in. |
| Expressions | Expressions, ExpressionsApi, JavaScriptExpressions, JavaScriptJintEngine, JavaScriptActivities, JavaScriptWorkflowsDesign, JavaScriptEndpoints | JavaScript expressions and `RunJavaScript`; the designer's expression editor. |
| HTTP | Http, HttpJavaScript, ActivitiesHttp, WorkflowsRuntimeHttp | Six HTTP suites; `HttpEndpoint`, `WriteHttpResponse`. |
| Activities | ActivitiesRuntime, ActivitiesPrimitives, ActivitiesControlFlow, ActivitiesSequence, ActivitiesFlowchart, ActivitiesGraphDesign, ActivitiesGraphRuntime, ActivitiesScripting, ActivitiesDispatchWorkflowRuntime, ActivitiesDispatchWorkflowDesign, ActivitiesDesignApi, ActivitiesDesignReconciliation, ClrActivityReconciliation, ActivitiesDesignEntityFrameworkCore | The activity types above; the activity catalog the designer and publishing read. |
| Workflows design | WorkflowsDesignApi, WorkflowDesignValidations, WorkflowsDesignEntityFrameworkCore, JsonWorkflowReconciliation, JsonActivityReconciliation | The migrated folder; the designer. |
| Publishing | WorkflowsPublishing, WorkflowsPublishingApi, WorkflowsPublishingEntityFrameworkCore | Publish on reconcile; publish from Studio. |
| Runtime | WorkflowsRuntimeEntityFrameworkCore, WorkflowsRuntimeApi, WorkflowsRuntimeTriggers, WorkflowsRuntimeResumption, WorkflowsRuntimeScheduling, WorkflowsRuntimeCheckpointPersistence, WorkflowsRuntimeHistory, WorkflowsExecutionEvidence | Reads of executables, instances and activity executions; correlation and event resumption; dispatch; diagnostics settings. |
| Events | The feature that provides `PublishEvent` and the `Event` trigger | The events suite. |

**To confirm in T003:**
- whether dispatch needs `WorkflowsRuntimeDistributed` and its two EF features;
- which feature provides `PublishEvent`/`Event`: a standalone event trigger feature, or the runtime triggers;
- whether the designer needs `JavaScriptEndpoints`, `ApiCapabilities` and `IdentityIamEntityFrameworkCore`, or nothing
  does.

A candidate without a confirmed reason is dropped.

**Not composed (FR-002):** everything BPMN, Weaver, Agent, Decisions and Connections; Secrets (unless an identity
feature requires it); dashboards; Studio preferences; standalone trigger authoring that no suite needs; the diagnostics
OpenTelemetry and structured-log features; compensation; attention; `ModularityApi`; Liquid. An engineer who needs one
of these for their own workflows adds it as in FR-005.

## R-5 Tests

A PR-tier test project, `extensions/Elsa3/tests/Migration/Host/Tests/Elsa3.Migration.Host.Tests.csproj` (xunit). It
builds the host through `MigrationHost.CreateBuilder`, `UseTestServer` and `MigrationHost.Build` (R-2), with
`Microsoft.AspNetCore.TestHost`, which is already pinned in the root `Directory.Packages.props`.
`Microsoft.AspNetCore.Mvc.Testing` is pinned only for Studio, and a top-level `Program` can't be booted by TestHost
alone, hence the two methods. The builder's environment is Development (R-2). Each test uses a temporary data
folder and a temporary migrated folder. Its tests:

- **Composition.** The shell activates with the committed `shells.json`. A listed feature that doesn't resolve fails,
  naming it (SC-005).
- **Reasons.** `composition-reasons.json` and `shells.json` list the same features, in both directions: a composed feature
  without a row, and a row for a feature that isn't composed, each fail naming the feature (FR-004).
- **No Elsa 3.** No assembly in the host's dependency closure is an `Elsa3.*` assembly or an Elsa 3 package (SC-004).
  This is in addition to `Elsa3MigrationBoundaryTests`.
- **No extras.** None of the feature families FR-002 names is composed.
- **US1 scenarios.**
  - 1: the sample workflow is published and its HTTP trigger answers.
  - 4: an environment variable overrides a committed setting.
  - 5: an unknown feature means the host refuses to start, naming the feature.
  - 6: a missing workflows folder means the host refuses to start; so does a missing activities file.
  - 2: a composite with an activity dependency loads and runs (PR #5685 is merged).

US1 scenario 3 (the engineer's own feature project) and US4 (Studio) are checked by hand with the quickstart. The evidence
goes in the PR.

Constitution §2.23 risk-based tests: the risks are drift between the composition and its reasons, Elsa 3 leaking in,
and the folder settings. `FeatureCompositionTests` already proves each feature composes on its own.

## R-6 Settings and data

- **No runtime packages.** The host loads no Nuplane packages: its audience adds their own features as project
  references in their clone (FR-005). This keeps it out of `HostProvidedPackagesGuardTests`. On main every host in
  `SharingHosts` must declare each shared assembly or exempt it, and an exemption needs a computed production
  configuration that only image builds ship. A source-only host can satisfy neither without changing the guard. If the
  hosting helpers the host uses (R-2) make it share assemblies after all, T005 raises it on the PR before going on.
- **Working directory.** `JsonWorkflowReconciliationSource.ScanFolder` calls `Directory.Exists` on the raw path, so
  relative paths resolve against the process's working directory, and `dotnet run --project` from the repository root
  makes that the repository root. The project sets `<RunWorkingDirectory>$(MSBuildProjectDirectory)</RunWorkingDirectory>`,
  so `dotnet run` uses the project folder wherever it is started from, and the host logs the absolute folder paths it
  uses at startup. An engineer who starts the built assembly another way passes absolute paths.
- **Data.** The persistence resource `Elsa` points at `Data Source=data/elsa.db`. Nothing in the persistence code
  creates the folder, so the host creates `data/` at startup when it is missing. `data/` is git-ignored.
  `Migrate:Policy` is `AutoMigrate`. In development mode the OpenIddict store is in memory (main,
  `OpenIddictVendorStoreRegistration`), so no identity database file is configured.
- **Identity.**
  - The identity features run in development mode with the Workbench's seeded development administrator (FR-006).
  - `AllowedReturnUrlOrigins` and the CORS policy list the local address of the repository's Studio host (R-8).
  - The workflow alteration payload key falls back to an ephemeral development key. That is acceptable for a local
    tool, and it is the Workbench's Development behaviour.
- **Migrated folder.**
  - `JsonWorkflowReconciliation` has `Options.FolderPath` `migrated/workflows`, `SourceId` `elsa3-migration`, and
    `PublishOnReconcile` `true` (spec Q4). `PublishOnReconcile` needs `WorkflowsPublishing` in the same shell.
  - `JsonActivityReconciliation` has `Options.FilePath` `migrated/activities/activities.json` and the same
    `SourceId`. It runs before workflow reconciliation (order 1 against 2), which its feature documentation requires.
  - The two settings are the ones the README documents (FR-011). An engineer overrides them with absolute paths to
    their own `migrated/` folder.
- **Sample.** `extensions/Elsa3/src/Migration/Host/migrated/workflows/hello.json` holds an HTTP trigger and a
  `WriteHttpResponse`. `migrated/activities/activities.json` holds `[]`.

## R-7 OpenIddict token store (spec Q10)

- **Fact (main).** Commit `e2e9550461` (2026-11-24) moved the Workbench's OpenIddict token store into
  `essentials/src/Foundation/Identity/OpenIddict/EntityFrameworkCore/Elsa.Foundation.Identity.OpenIddict.EntityFrameworkCore`.
  It is registered with `AddOpenIddictEntityFrameworkCoreStore(configuration)`, and main's `apps/Elsa.Essentials.Host`
  calls it. `561a5099a9` kept its implementations internal.
- **On the initiative branch** the store is still Workbench host code. The branch is 1,084 commits behind main
  (2026-11-25).
- **Decision.** The migration host calls main's library. Implementation waits for a coordinator main sync that brings
  it to the initiative branch (T001). The sync also renames the Workbench to `apps/Elsa.Workbench.Host` and changes
  the guard lists the tasks edit (`AllowedEfConsumers` on main lists `Elsa.Essentials.Host`, `Elsa.Foundation.Host`
  and `Elsa.Workbench.Host`), so every task starts after it. This specification moves no store code, and the Workbench doesn't change
  (FR-017).
- **Alternative.** Copying or moving the store on the initiative branch would conflict with main's move and duplicate
  its migrations.

## R-8 Studio

- **Decision.** No new Studio app. The engineer runs the repository's Studio host: `Studio/src/apps/Elsa.Studio.Web`
  on the initiative branch today. Main has renamed it and added Studio flavors (#5768); after the sync the quickstart
  names whichever host fits. The migration host composes the designer's backend features (Q8). The task confirms which
  Studio modules expect capabilities the host doesn't advertise, and lists them in the README.
- **UX acceptance.** No Studio code changes, so the Studio UX acceptance doesn't apply. US4 is checked by hand.

## R-9 EF admission (framework §2.20)

`EfCoreDependencyGuardTests` reads each project's full restore graph, and the host resolves EF through the composed
features. So the host is admitted, with the package array computed after the composition (T004) is final:
- its name goes in `AllowedEfConsumers`;
- a reviewed package array, plus an arm in `ReviewedEfPackages`.

The host references no EF vendor package directly, so `OpenIddictPersistenceArchitectureTests` pins nothing new.

## R-10 Acceptance run

- **Decision.** Use the S-15 Elsa 4 templates and environment unchanged, except base URL and credentials (the
  development administrator from R-6), in an environment file committed next to the S-15 Elsa 4 one, with the host's
  fixed URL (R-2). The run record is a row in the S-15 ledger,
  `extensions/Elsa3/tests/JTest/ledger/runs.md`, in its existing columns: Engine `Elsa 4`; Source names the migration
  host, the CLI version that wrote `migrated/`, and whether spec 1207 is implemented; Notes names the Workbench run it is
  compared with. Both runs on one head then sit side by side, and spec 1235's ledger format doesn't change. Capture is set through the diagnostics settings API after start, as in S-15.
- **Before the CLI exists**, the sample workflow (R-6) checks the host (FR-011).
- **Ordering.** The final acceptance task waits for the CLI's `inventory` command and S-15's Elsa 4 run on the same
  head.

## R-11 Constitution

§E2.7 names the `Elsa3` projects as `Elsa3.<Domain>.Import` adapters plus shared support (`Elsa3.Models`,
`Elsa3.Mapping`). The domain tree (`docs/reference/elsa-domain-tree.md`) gives `Elsa3` the purpose "Imports Elsa 3
definitions". A host is neither an adapter nor shared support. The implementing PR therefore amends both in the same
unit: §E2.7 names `Elsa3.Migration.Host` as the local migration host, which references no Elsa 3 project, and the
domain-tree purpose adds "and hosts the local migration target". §E2.1 is unchanged, because the host is a project of
the `Elsa3` domain, not a new grouping.
