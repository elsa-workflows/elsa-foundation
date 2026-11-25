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

- **Decision.** `Program.cs` takes the Workbench's host wiring that the composed features need, and nothing else:
  - CShells shell hosting with the default shell activated eagerly at startup, and readiness at `/health/ready` (the
    Workbench's `DefaultShellWarmup` and `MapShellReadiness`);
  - the root health endpoint;
  - `AddEfPersistenceResources`, with a migration-host `IEfToolingShellDefaults` type and its
    `[assembly: EfToolingShellDefaults(...)]` attribute, as `WorkbenchEfToolingShellDefaults` is for the Workbench;
  - `AddEfPendingMigrationActivationGuard`, `AddConfiguredDataProtection` and `AddShellStartupValidation`;
  - `AddAuthentication`/`AddAuthorization`, and the OpenIddict token store from main's
    `Elsa.Foundation.Identity.OpenIddict.EntityFrameworkCore` (`AddOpenIddictEntityFrameworkCoreStore`, R-7);
  - Nuplane loading from a directory feed (R-6);
  - a CORS policy for the repository's Studio host (R-8).

  It leaves out the boot timeline, the composition observation endpoints, Weaver and the OpenTelemetry bridge.
- **After the main sync.** If the sync brings in main's shared hosting helpers (main's `apps/Elsa.Essentials.Host`
  uses `AddFoundationShellHosting` and `MapFoundationHealth`), the host uses them instead of copying the Workbench's.
  The behaviour above stays the same.

## R-3 Host composition

- **Decision.** The composition is the host's `shells.json`, in the CShells shape the Workbench uses
  (`CShells:Shells:default:Features`). It is one file that tools read by path (FR-004).
- **Reasons.** The host's README has a table with one row per composed feature and its reason. CShells' feature
  section has no room for comments: the Workbench's `shells.json` has none, and a `// key` inside `Features` would be
  read as a feature name. A host test compares the table with `shells.json` and fails, naming the feature, on any
  difference (FR-004, SC-003).
- **Every composed feature is listed**, including the ones another feature's `DependsOn` brings in. Their reason is
  "required by X". The test can then compare the composed set with the list exactly.
- **Configuration layers**, lowest first, as the Workbench layers them today (`apps/Elsa.Workbench/Program.cs` lines
  106–118):
  1. `appsettings.json`, `appsettings.{Environment}.json`;
  2. `shells.json`, `shells.{Environment}.json`;
  3. user secrets in Development;
  4. environment variables;
  5. the command line.

  No new merge code is needed, so FR-005 needs nothing beyond the standard sources.

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
starts the host in-process with `Microsoft.AspNetCore.TestHost`, which is already pinned in `Directory.Packages.props`,
on a temporary data folder and a temporary migrated folder. Its tests:

- **Composition.** The shell activates with the committed `shells.json`. A listed feature that doesn't resolve fails,
  naming it (SC-005).
- **Reasons.** The README table and `shells.json` list the same features (FR-004).
- **No Elsa 3.** No assembly in the host's dependency closure is an `Elsa3.*` assembly or an Elsa 3 package (SC-004).
  This is in addition to `Elsa3MigrationBoundaryTests`.
- **No extras.** None of the feature families FR-002 names is composed.
- **US1 scenarios.**
  - 1: the sample workflow is published and its HTTP trigger answers.
  - 4: an environment variable overrides a committed setting.
  - 5: an unknown feature means the host refuses to start, naming the feature.
  - 6: a missing workflows folder means the host refuses to start.
  - 2 (composite with dependencies) is added once PR #5685 has merged.

US1 scenario 3 (the engineer's own package) and US4 (Studio) are checked by hand with the quickstart. The evidence
goes in the PR.

Constitution §2.23 risk-based tests: the risks are drift between the composition and its reasons, Elsa 3 leaking in,
and the folder settings. `FeatureCompositionTests` already proves each feature composes on its own.

## R-6 Settings, packages and data

- **Packages.** A Nuplane directory feed named `local-packages`, reading the host's `packages/` folder, as the
  Workbench's `appsettings.json` configures it. The folder is git-ignored except for a `.gitkeep`. The Nuplane loading
  settings the Workbench bakes (`Nuplane:Loading:SharedAssemblies`, `Nuplane:HostProvidedPackages`) are narrowed to the
  host's closure. `HostProvidedPackagesGuardTests` adds the host to `SharingHosts` if it shares assemblies.
- **Data.** The persistence resource `Elsa` points at `Data Source=data/elsa.db`. The OpenIddict store's SQLite file
  is `data/elsa-identity.db`. `data/` is git-ignored. `Migrate:Policy` is `AutoMigrate`.
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
  - Relative paths resolve against the host's content root. T005 confirms this and makes them absolute at startup if
    the features resolve them against the working directory.
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
  it to the initiative branch (T002). This specification moves no store code, and the Workbench doesn't change
  (FR-017).
- **Alternative.** Copying or moving the store on the initiative branch would conflict with main's move and duplicate
  its migrations.

## R-8 Studio

- **Decision.** No new Studio app. The engineer runs the repository's Studio host: `Studio/src/apps/Elsa.Studio.Web`
  on the initiative branch today. Main has renamed it and added Studio flavors (#5768); after the sync the quickstart
  names whichever host fits. The migration host composes the designer's backend features (Q8). The task confirms which
  Studio modules expect capabilities the host doesn't advertise, and lists them in the README.
- **UX acceptance.** No Studio code changes, so the Studio UX acceptance doesn't apply. US4 is checked by hand.

## R-9 EF admission

`EfCoreDependencyGuardTests` reads each project's full restore graph, and the host resolves EF through the composed
features. So the host is admitted:
- its name goes in `AllowedEfConsumers`;
- a reviewed package array, plus an arm in `ReviewedEfPackages`.

The host references no EF vendor package directly, so `OpenIddictPersistenceArchitectureTests` pins nothing new.

## R-10 Acceptance run

- **Decision.** Use the S-15 Elsa 4 templates and environment unchanged, except base URL and credentials (the
  development administrator from R-6). The run record is a row in the S-15 ledger,
  `extensions/Elsa3/tests/JTest/ledger/runs.md`, with engine `migration-host`, so both runs on one head sit side by
  side. Capture is set through the diagnostics settings API after start, as in S-15.
- **Before the CLI exists**, the sample workflow (R-6) checks the host (FR-011).
- **Ordering.** The final acceptance task waits for three things: the CLI's `inventory` command, S-15's Elsa 4 run on
  the same head, and PR #5685.
