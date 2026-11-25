# Research: Elsa 3 workflows migration CLI

Facts are from the initiative branch at the commit this specification was cut from. Paths are repository-relative.

## R-1 The console project

- Specification 1236 was rescoped on 2026-11-25. There is no essentials engine image and no Elsa 3 migration image,
  and no Nuplane. `apps/Elsa.Migration.Host` is a source-built, plain CShells ASP.NET Core host. Engineers clone the
  repository and add their own feature projects to it as project references (1236 FR-005, FR-018). Its composition
  `shells.json` is readable by path (1236 FR-004). How the CLI composes is now this specification's decision.
- The audience is an engineer who wants a host and project references to debug, and their own activities to port and
  test (Joey, 2026-11-25).
- `dotnet-elsa` (`essentials/src/Cli/Elsa.Cli/`) parses its commands with System.CommandLine, which the repository
  already pins.

**Decision**: add a console project, `Elsa3.Migration.Cli`, at `extensions/Elsa3/src/Migration/Cli/`
(`OutputType` `Exe`, `IsPackable` `false`). It is a thin entry over the tooling library `Elsa3.Migration.Tooling`
(`extensions/Elsa3/src/Migration/Tooling/`), which holds the pipeline. It parses `workflows inventory` and
`workflows assess` with System.CommandLine, calls the tooling in-process, prints the output path or the refusal line,
and returns the exit code (FR-004, FR-005). The engineer runs it with
`dotnet run --project extensions/Elsa3/src/Migration/Cli -- ...` and debugs it from the IDE like the host. The project
does not set `RunWorkingDirectory`, so relative paths resolve against the directory the engineer runs it from.

Its project references are the code closure (FR-006):

- `apps/Elsa.Migration.Host/Elsa.Migration.Host.csproj`, so every feature the host composes, and every feature project
  the engineer has added to the host, is in the CLI's build;
- `Elsa3.Migration.Tooling`, which references the Elsa 3 projects of the addition list (the converter, the collection
  import for reusables, the import's EF ledger and the publishing bridge).

The host is an ASP.NET Core project, so the CLI carries the ASP.NET Core shared framework. It starts no web host.

**Rejected**:

- A `dotnet elsa elsa3-migration` command group on the ADR 0076 worker, reaching a second frozen entry point inside an
  Elsa 3 migration image built `FROM` the essentials engine image, with the engineer's features as Nuplane packages.
  The engine image is no longer planned (1236 Q11), the design needed an ADR 0076 amendment (D14, D10; PR #5680,
  withdrawn), and a container is harder to debug than a project in the clone.
- The engineer adding their feature project to both the host and the CLI. That is two places to keep in sync, and a
  drift would make the test migration run on another composition than the host.
- A `--host` option pointing at another host folder. The migration host is the host (spec Q10).

## R-2 Startup reconciliation already runs the pipeline

- `extensions/Elsa3/src/Activities/Design/Import/Reconciliation/Services/Elsa3FolderImportTask.cs` is an `IStartupTask`.
  It runs `Elsa3JsonFileReader.ReadAsync`, then `Elsa3ImportSelection.SelectInput`, then, inside
  `IPersistenceScopeRunner.RunAsync` with `Elsa3StartupPublishingGrant`, the collection importer's `AnalyzeAsync`.
  `SelectPlan` follows, then `ApplyAsync`, retried after excluding refused items. Last comes
  `ImportedWorkflowPublisher.PublishAsync`, which sends `PublishWorkflow`.
- Skips carry codes `ELS3-RECONCILE-DUPLICATE-INPUT`, `-BLOCKED-BY-DEPENDENCY` and `-PUBLISH-FAILED`
  (`Reconciliation/Models/Elsa3ReconciliationDiagnosticCodes.cs`). Today they reach only the log.
- `Elsa3JsonFileReader` reads one file holding a definition or an array of them.

**Decision**: extract the sequence after reading into one internal service of the reconciliation project, `Elsa3ImportRun`.
It returns a structured result: the plan items with their diagnostics, the skips with their cause, the publication
outcome per workflow, and the publish failure's diagnostics. The startup task keeps its behaviour and logs from that
result. The tooling calls the same service (FR-013). The reconciliation project grants the tooling assembly
`InternalsVisibleTo`, so no public surface is added (ADR 0080).

The run's collaborators (`Elsa3ImportSelection`, `ImportedWorkflowPublisher`, `Elsa3StartupPublishingGrant` and the
`Elsa3StartupPublishingAuthorization` decoration) are registered only by `Elsa3JsonReconciliationFeature`
(`Reconciliation/Elsa3JsonReconciliationFeature.cs`). That feature throws without `SourceId` and a path, and it
registers `Elsa3FolderImportTask`. The extraction therefore also moves their registration into one internal helper,
`Elsa3ImportRunRegistration.Add(IServiceCollection)`. The feature calls it, and the tooling's shell feature `Elsa3MigrationImportRun` calls it
inside the shell (R-5), without enabling the folder import feature.

Two behaviours of the startup sequence matter for the report:

- The collection import's publish phase publishes the activity version of every committed reusable version, older
  pinned ones included (`ReusableActivityCollectionImporter.cs`), so all of them are exported to `activities.json`.
  `ImportedWorkflowPublisher.PublishAsync` acts on workflows only: it groups by workflow definition and publishes
  only the newest imported version. Older selected workflow versions, and the wrappers of older reusable versions, are
  imported but never published. The report marks them `ELS3-MIGRATION-SUPERSEDED-VERSION`.
- The task throws `Elsa3ReconciliationFailedException`, ending the start, for a plan error that names no item and for
  refusals that name no single workflow, such as a credential-literal refusal over the whole selection at apply
  (`Elsa3FolderImportTask.cs`, `ReusableActivityCollectionImporter.cs`). `Elsa3ImportRun` returns these as run-level
  refusals with their codes. The startup task throws on them as before, and the CLI reports them in `runFindings`.

**Rejected**: running the startup task itself in the CLI. It reads only its `FilePath`/`FolderPath` options, it logs
instead of returning a result, and it is registered only by `Elsa3JsonReconciliationFeature`, which throws without
`SourceId` and a path.

## R-3 Diagnostics and the code table

- `Elsa3MigrationDiagnostic` (`extensions/Elsa3/src/Models/Elsa3MigrationBoundary.cs`) carries severity, code,
  message, JSON path, guidance and metadata (`DefinitionId`, `DefinitionVersion`, `ActivityNodeId`, `Elsa3Type`,
  `Property`).
- Code constants: `Elsa3ActivityDiagnosticCodes` (`ELS3-ACTIVITY-*`, `ELS3-BEHAVIOUR-*`), `Elsa3ImportDiagnosticCodes`
  (`VF-IMP-001` to `012`, in `Mapping/Services/Elsa3ExpressionRewriter.cs`), `Elsa3MigrationDiagnosticCodes`,
  `Elsa3ReconciliationDiagnosticCodes`, plus `ELS3-UNTYPED-RESULT` and `ELS3-SCRIPT-OUTCOME-*`.
- Component names travel only in message text: rewrite issues carry no metadata
  (`Elsa3ExpressionRewriteIssue(Code, Message, Guidance)`), and a call of an unknown function shares the
  forbidden-capability code with refused capabilities (`Elsa3ExpressionRewriter.cs`). The converter's member-name matching
  decides per name whether a host function may take it over (`CanBeMatched`): an undeclared custom global and some
  refused Elsa 3 built-ins can be, names on its never-matched list or names that clash with a generated accessor
  cannot. A read of an undeclared global used only as a value gets no diagnostic today (the non-call
  diagnostic needs a registered host function), so it would convert silently and fail at run time. FR-016a adds
  `ComponentName` metadata, and gives every matchable call and value read a component code of its own;
  unmatchable calls and forbidden capabilities keep the forbidden-capability code.
- Several messages, and some guidance, interpolate input text: the strict-mode, JavaScript safe-lowering and Liquid
  safe-lowering parse messages pass the parser's description through (`Elsa3ExpressionRewriter.cs`), the Switch mode and case-condition messages quote
  literals (`Elsa3SwitchValueSynthesisRewrite.cs`), the Fork join-mode message can quote a whole typed value
  (`Elsa3ParallelBranchNamesRewrite.cs`), the HTTP status message of the `EnumNameToInt` value contract quotes the literal
  (`Elsa3ActivityNormalizer.cs`; no built-in map entry uses that contract today), the variable starting-value messages echo the value and one guidance builds a
  suggestion from its items (`Elsa3VariableMapper.cs`), the date-interpretation note quotes the literal, its interpreted value and the
  source time-zone setting (`Elsa3LiteralConversion.cs`, `LiteralDateInterpreted`), as do the other date-interpretation
  notes and guidance (`Elsa3ValueFlowLowerer.cs`, `Elsa3ActivityNormalizer.cs`, `Elsa3ExpressionRewriter.cs`),
  `Elsa3ExpressionRewriter.cs` quotes up to 160 characters of script through `Quote(...)` in its `toJson`,
  zone-dependent date and locale messages, the Fork branches and unreadable-outcomes messages quote raw literals
  (`Elsa3FlowchartGraphBuilder.cs`, `Elsa3TerminalPlacementRewrite.cs`), and several sites pass
  `exception.Message` through
  (`Elsa3ActivityNormalizer.cs`, `Elsa3DefinitionConverter.cs`).
- Publish refusals come from Elsa 4 (`ExpressionPublicationValidationException`, `WorkflowExecutableCompilationException`,
  `PublicationActivationException`). Their diagnostics use Elsa 4 codes.

**Decision**: add one code table, `Elsa3MigrationCodeTable`, in the tooling project. Only that project references
every project that holds code constants: Models, Mapping (`VF-IMP-*`, `ELS3-UNTYPED-RESULT`, `ELS3-SCRIPT-OUTCOME-*`),
Import (`ReusableActivityImportDiagnosticCodes`, the `ELS3-REUSABLE-*` codes) and Reconciliation (`ELS3-RECONCILE-*`).
Some codes are emitted as inline literals, for example `ELS3-INPUT-DEFAULT-REQUIRED` and `ELS3-NULL-POLICIES` in
`Elsa3ActivityNormalizer`. They are promoted to constants so that reflection sees them. Constants carry no severity,
so the table needs an entry for every code, whatever its severity. It also marks routine codes, such as
`ELS3-NULL-POLICIES`, an Info the converter adds to every definition, so that "goes smoothly" can be true. Skips carry
no severity of their own, and the report gives each skip Error severity. It maps every Elsa 3 code to its remedy (`patch-definition`, `build-component`, `none`), and component codes
to a component kind. A reflection test in the migration test project enumerates every code constant and fails when one has no entry
(FR-016). Elsa 4
publish diagnostics get remedy `patch-definition` by default, because a published Elsa 3 definition that Elsa 4 refuses
must be changed or reported as a converter gap. Their messages are not copied. Elsa 4 messages quote script source:
`JavaScriptDotNetUsageAnalyzer` puts the call into its message, and `JavaScriptExpressionToolingProvider` passes the
parser message through. The report therefore takes the text for a code the converter does not own from the code
table's fixed text, and the code table carries a default text for unknown Elsa 4 codes (FR-019). Messages that embed input text are changed at the converter so that
they name the cause without the text (FR-019). The fixture's sentinel test proves only the paths the fixture
triggers, so T028 audits every interpolated message and guidance string in the Elsa 3 sources and gives each changed
site a unit test with a sentinel.

## R-4 In-memory SQLite across modules

- `EfSharedTransaction.EnsureSingleTarget` (`essentials/src/Persistence/EntityFramework/EfSharedTransaction.cs`) throws
  `EfSharedTransactionTargetMismatchException` when providers or connection strings differ. Serializable mode refuses a
  supplied connection. A plain `:memory:` database belongs to one connection.
- Tests use `Data Source=file:<name>;Mode=Memory;Cache=Shared`, for example in
  `essentials/tests/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/BackgroundActivityWorkTests.cs`.

**Decision**: one connection string, `Data Source=file:elsa3-migration-<guid>;Mode=Memory;Cache=Shared`, is set on every
enabled EF feature. The host-wide migrate policy is set to apply migrations, and one connection stays open from
composition until disposal (FR-009). The guid never reaches the report (FR-022).

The host's committed settings put its SQLite file under `data/`, and in development mode its OpenIddict store is in
memory (1236 R-6), so the run leaves the store as the host wires it. The run replaces `ConnectionStrings:Elsa` and
every per-feature persistence connection setting with the in-memory database. The host's root services include its
Data Protection (`AddConfiguredDataProtection`, 1236 R-2): without the EF key store enabled, it keeps keys where
ASP.NET Core keeps them by default, on disk. After `AddHostServices` the run replaces it at root with an ephemeral key
ring. The connection strings are not replaced after the fact: they are set in the run-override configuration layer
before `AddHostServices` runs (R-5). Nothing reaches a disk (FR-010).

## R-5 Composition without a web host

- Non-web compositions exist: `essentials/tests/Modularity/Tests/NuplaneHostTestComposition.cs`
  (`Host.CreateApplicationBuilder` with CShells) and `essentials/src/Foundation/Hosting/FoundationShellHostingExtensions.cs`.
- The migration host discovers its features with CShells `WithHostAssemblies()`, which reads the project-referenced
  assemblies through the `DependencyContext` and fails activation with `FeatureNotFoundException` for an unknown
  feature (1236 R-2).
- The host's configuration order, lowest first, is `appsettings*.json`, `shells.json`, `shells.{Environment}.json`,
  `shells.Local.json` (git-ignored, optional), user secrets (from the project's `UserSecretsId`), environment variables
  and the command line (1236 R-3).
- The host runs only in the Development environment: `DevelopmentOrDemoGuard` refuses identity features in
  development mode elsewhere (1236 R-2). Its `shells.json` commits development-only demo keys, such as the runtime EF
  signing keys, without which shell activation fails (1236 R-6).
- A project reference to the host can copy the host's content files, such as `appsettings.json` and `shells.json`,
  into the CLI's output folder.
- The host's root wiring (1236 R-2) registers services that features resolve when they activate, and CShells copies
  root registrations into every shell (`apps/Elsa.Foundation.Host/Program.cs`, the cluster membership block):
  `AddEfSchemaReadability`, `AddConfiguredClusterMembership`, `AddConfiguredDataProtection`,
  `AddShellStartupValidation`, authentication and authorization, the OpenIddict token store and the
  `ShellActivationTracker`. A shell composed without them can fail where the host's shell activates.
- `JsonWorkflowReconciliationFeature` refuses unless exactly one of `FilePath`, `Files` and `FolderPath` is set, and
  `JsonActivityReconciliationFeature` refuses unless exactly one of `FilePath` and `Files` is set. A `FilePath` names a
  file that must exist.
  The workflow feature also reads `AdditionalSources`, each another source with the same rule; the activity feature has
  no `AdditionalSources` and no `FolderPath`. `Files` is a collection, stored as `Files:0:*` keys, and `GetChildren`
  merges children across providers, so a higher configuration layer cannot remove a key a lower one sets.
- `Elsa3StartupPublishingAuthorization` decorates `IActivityPublishingAuthorizationContext`, which a shell feature
  registers (`WorkflowsPublishingApi`), so `Elsa3ImportRunRegistration.Add` must run inside the shell.
  `RunShellTasksInitializer` is internal and registered inside the shell by `TasksFeature`, and CShells has no hook for
  adding shell services from the root.
- `Host.CreateApplicationBuilder()` adds environment variables, the command line, the entry assembly's user secrets,
  `appsettings*.json` from the current directory and `DOTNET_ENVIRONMENT`. `AddConfiguredDataProtection` and
  `AddConfiguredClusterMembership` read configuration when they are called.

Facts that shape the run:

- EF migrations run through `EfModuleMigrator<TContext>`, which is both an `IHostedService` and an `IShellInitializer`
  (`essentials/src/Persistence/EntityFramework/EfModuleMigrator.cs`).
- The activity catalog is filled by startup tasks: `ActivityVersionReconcilerStartupTask` and
  `RegisterActivityTypesStartupTask`.
- The converter resolves Elsa 3 types against that catalog (`Elsa3ActivityResolver`).
- Startup tasks are started by a shell initializer, `RunShellTasksInitializer`
  (`essentials/src/Tasks/Elsa.Tasks/Services/RunShellTasksInitializer.cs`), and its one call,
  `ITaskManager.StartExecutingRegisteredTasks`, runs the startup tasks, then the background tasks, then the recurring
  tasks (`TaskManager.cs`).

**Decision**: the CLI composes through the host's own wiring. T004a extracts two public static methods from the
host's `CreateBuilder`, leaving the host's behaviour unchanged; the host's `Program.cs` and the CLI both call them
(spec *Dependencies and boundaries*):

- `MigrationHost.AddHostConfiguration`: the host's configuration layers in 1236 R-3's order, for a given host folder
  and environment, taking the host assembly for `AddUserSecrets` and the command-line arguments (the CLI passes none),
  with a switch that leaves out the local sources
  (`shells.Local.json`, user secrets, environment variables, command line);
- `MigrationHost.AddHostServices`: the host's root service registration, including CShells with
  `WithHostAssemblies()`, without the web pipeline.

The tooling builds an empty generic host (`Host.CreateEmptyApplicationBuilder`, so no default source leaks in), with
`ContentRootPath` set to the host folder and `EnvironmentName` set to Development. It is never started. In order:

1. `AddHostConfiguration` adds the host's layers into a separate configuration, which the run adds to the builder's
   configuration through a key filter. The filter drops `Files`, `FilePath` and `AdditionalSources` under the JSON
   workflow reconciliation feature and `Files` under the JSON activity reconciliation feature, at any depth.
2. The Elsa 3 addition list.
3. The run-override layer: every persistence connection string set to the run's database (R-4), the two
   reconciliation inputs (`FolderPath` and `FilePath`, below), the Tasks feature's startup-tasks-only setting, and,
   when the host composes `FileSystemDistributedLocking`, its `LocksFolderPath` in the run-scoped temporary folder:
   its default is `App_Data/locks` under the current directory, and the run takes locks in startup tasks, reusable
   publish and the import's draft commands. The key is set only when the feature is listed, because a key under
   `Features` enables a feature.
4. `AddHostServices`, which reads the configuration built so far.
5. Data Protection replaced at root with an ephemeral key ring.

The layers of step 1 are:

- `inventory`: local sources left out, so the configuration is the host's `appsettings*.json`, `shells.json` and
  `shells.Development.json`, the host's committed composition.
- `assess`: local sources included (`shells.Local.json`, the host's user secrets and environment variables), in the
  host's order. The CLI passes no command-line arguments, so its own command line is not a configuration source.

The addition list of step 2, `elsa3-migration.features.json`, only adds feature entries, so a local source cannot
remove an Elsa 3 addition.

Configuration is read from the host project folder, never from the CLI's output folder, and the CLI has no settings
file of its own. Then the tooling activates the shell itself: CShells activates lazily, and the host's activation
hosted service never runs because the CLI's host is never started, so `MigrationComposition` calls
`IShellRegistry.GetOrActivateAsync` with the host composition's shell name. An exception from that call is
`composition-failed`, and `--out` is created only after it returns. No web server starts.

The shell-level parts are a feature of the tooling's own, `Elsa3MigrationImportRun`, in the addition list. It
`DependsOn` `WorkflowsPublishingApi`, `WorkflowsPublishing`, `Elsa3ImportActivitiesPublishing` and `Tasks`, and its `ConfigureServices` calls
`Elsa3ImportRunRegistration.Add`, so the import run and its publishing authorization decoration are registered in the
shell, after the context they decorate. Startup tasks only is a supported Tasks setting, `StartupTasksOnly` on the
`Tasks` feature (default `false`): `RunShellTasksInitializer` reads it and, when it is set, runs only the startup
tasks, in dependency order, through the startup-only `ITaskManager` member, and starts no background or recurring task.
The run sets it in its override layer. Nothing removes or skips an internal type.

`AddHostServices` creates no folders; the host creates `data/` at startup, outside it. So the run creates nothing under
`apps/Elsa.Migration.Host`.

The tooling does not reference the host. The CLI does, and passes into the tooling the host assembly, the host project
folder and the CLI project folder; the two folders are written by the CLI's project file into
`AssemblyMetadataAttribute`s at build time. The addition list is read from the CLI project folder only, and is not
copied to the output. There is no option to point elsewhere. Tests pass a test-only host folder and addition list
through an internal seam of the tooling (`InternalsVisibleTo` for the migration test project).

**Rejected**: replicating the host's root wiring in the tooling. It drifts from the host as the host changes, and a
missing root service fails only when a feature that needs it activates.

A listed feature that the build does not provide (`FeatureNotFoundException`) refuses the run as `composition-failed`
and names the feature, as the host does at startup (1236 FR-005). A feature that throws while the shell is composed
refuses the same way and names the feature, or the setting key when the failure names one; the exception message is
not copied, because it may quote a value.

All shell initializers run. The startup-only `ITaskManager` member and the `StartupTasksOnly` setting are a small
essentials change. The tooling resolves services from the shell's provider. Background hosted services, background tasks and recurring tasks never start, so
there is no trigger runtime, scheduler or recurring work. The Elsa 3 folder import feature is not composed. The host's
JSON workflow and activity reconciliation features are composed, as the host composes them, but the run points them
at an empty `workflows/` folder (`FolderPath`) and an activities file holding `[]` (`FilePath`), in a run-scoped
temporary folder outside `--out` that it deletes at the end. The key filter of step 1 drops the other path keys and
`AdditionalSources`, because each feature refuses more than one path and an additional source would load content. Their relative defaults would
otherwise resolve against the engineer's current directory, where the host refuses a missing folder; an engineer who
pointed them at a previous `migrated/` folder would load it into the run next to the import; and a local source that
sets `Files` or `AdditionalSources` would make the run refuse or load more. `--out` and its staging folder are created only after the shell has been
composed, so a `composition-failed` refusal leaves `--out` untouched (FR-005).

The host's committed development keys apply, so the run needs no generated settings. **Rejected**: generating run-only
values for required secret settings. That answered the essentials engine image, which committed none; the migration
host commits development keys, so the problem does not exist.

**Rejected**: calling the host's `MigrationHost.CreateBuilder` and building a `WebApplication`. It brings in the web
pipeline and reads the process's command line, and the CLI must start no web server.

T015 checks parity: in a baseline empty application builder with the same settings and in the CLI's builder, it
snapshots the descriptors before and after the `AddHostServices` call and compares, as multisets, the descriptors
that call added, keyed by (ServiceType, ServiceKey, Lifetime, ImplementationType, or the marker `factory` or
`instance`). Excluded are the Data Protection descriptors the ephemeral key ring replaces (`IDataProtectionProvider`,
`IKeyManager`, `IConfigureOptions<KeyManagementOptions>`, and the `IHostedService` of `DataProtectionStartupCheck`).
The connection and the task mode get their own assertions: every EF feature resolves the run's connection string, and
the shell starts no background or recurring task.

## R-6 The engineer's own features

**Decision**: the engineer's Elsa 4 features reach the run as project references they add to the migration host in
their clone, the same edit that makes the host compose them (1236 FR-005). The CLI references the host, so the feature
is in the CLI's build. The engineer lists the feature, and its settings, in a local source of the host
(`shells.Local.json`, user secrets or environment variables), which `assess` reads (R-5). No package is loaded at run
time (spec Q9).

**Rejected**: reading a folder of `.nupkg` files as Nuplane directory feeds into a run-scoped install root. It needed
an ADR 0076 amendment, because the worker never reconciles on its own initiative (D1, D10), and the migration host
loads no packages, so the test migration would have run on another closure than the host.

## R-7 Architecture guards for a CLI that references a host

- `ArchitectureGuardTests.ExpectedProjectPath` derives an `Elsa3.*` project's path from its name under
  `extensions/Elsa3/` (the `ExtensionBuckets` row). `Elsa3.Migration.Cli` derives to
  `extensions/Elsa3/src/Migration/Cli/Elsa3.Migration.Cli.csproj`, so it needs no entry. The host's own entry is
  1236's (1236 T002).
- `ExtensionBoundaryTests` checks core-to-extension and extension-to-extension edges. An edge from an extension to an
  `apps/` host is neither, so it needs no entry. `Excluded_hosts_are_deployable_hosts_not_libraries` needs the host to
  be `IsPackable=false`, which 1236 already sets.
- `Elsa3MigrationBoundaryTests.CanonicalElsaProjects_DoNotReferenceElsa3AssembliesOrDtos` scans `essentials/src` only.
  The CLI lives under `extensions/Elsa3/` and the host under `apps/`, so it is unaffected. 1236's closure test (the
  host carries no Elsa 3 assembly, 1236 FR-003) is unaffected too, because the reference goes from the CLI to the host,
  never from the host to Elsa 3.
- `EfCoreDependencyGuardTests.Only_admitted_consumers_and_pilot_projects_resolve_ef_core_packages` fails for any
  project outside its admitted lists that resolves an EF Core package. The CLI resolves the host's EF closure, the
  tooling resolves EF through the import's EF ledger, and the migration test project, which references the CLI and
  the T003 fixture feature, resolves the host's closure plus SQLite. So the CLI is added to `AllowedEfConsumers` as a
  composition root, with a reviewed package array (the host's, from 1236 R-9) and an arm in `ReviewedEfPackages`; the
  tooling gets a reviewed closure next to `Adr0073Elsa3ImportEf`, and the test project one of its own.
- The Nuplane host guards (`NuplaneHostSettings.HostsThatShareAssemblies`) scan only `apps/` hosts that share
  assemblies, so a project under `extensions/` is out of their scope.
- `ArchitectureGuardTests.Every_production_project_is_built_before_the_architecture_tests` fails for a production
  project outside `Elsa.Architecture.Tests.csproj`'s reference graph. Nothing references the CLI, so it is added there
  as a build-only reference (`ReferenceOutputAssembly="false"`), as the `apps/` hosts and `Elsa.Cli` are.
- §E2.7 names import adapters and shared support as the `Elsa3` domain's projects. 1236 kept its host out of
  `extensions/Elsa3/` partly for that reason. The CLI and the tooling are migration tooling over the import adapter,
  as the tooling already was in the earlier design; if reviewers read §E2.7 as excluding them, the constitution
  section is updated in this unit.

**Decision**: the implementing PR adds the EF admissions and the build-only reference above, regenerates the solution
filters and `dependency-map.json`, and places the test project in `tools/ci/test-tiers.json`. The tooling library is `IsPackable=false` like the CLI: it
ships only with the CLI, so `FeatureCompositionTests`' packable-library rule does not ask the architecture tests to
load it, and its shell feature never composes next to `Elsa3JsonReconciliation` there. No other guard changes.

## R-8 Exporting migrated content

- JSON workflow reconciliation (`essentials/src/Workflows/Design/Reconciliation/Json/`) reads a top-level array of
  `WorkflowVersionReconciliationModel` through `IPayloadSerializer`. `FolderPath` reads top-level files in ordinal
  order. `activityVersionId` values of CLR catalog activities are deterministic catalog ids; imported reusables are stored
  under import ids (`ReusableActivityImportIdentity.ActivityVersion`) instead.
- JSON activity reconciliation (`essentials/src/Activities/Design/Reconciliation/Json/`) reads
  `ActivityVersionReconciliationModel` arrays, and `Files` are read in ascending `Order`. Specification 1239
  lets it publish source-owned composites with their dependencies, retrying refused versions in rounds, so order does
  not matter. The model has no version-id field: the engine assigns
  `ActivityCatalogStableIds.VersionId(ActivityTypeKey, Version)` (`CollectActivityVersions.cs`). A dependency must be
  named by that id (otherwise `activity.dependency.version-not-found`). Only a schema-1 graph needs exactly one
  `done` outcome (`activity.graph.outcome-done-required`); the import writes schema-2 graphs, which keep declared
  Elsa 3 outcomes.
- Only readers exist. No writer produces either format.

**Decision**: add a writer next to each reader, in the same project, so the format has one owner:
`JsonWorkflowCatalogWriter` and `JsonActivityCatalogWriter`. Each serializes models through `IPayloadSerializer`, and a
round-trip test checks read(write(x)) = x. The tooling reads the published versions back from the
design stores and writes them. Workflows go one file per definition, named by definition id. Activities go in one file,
in dependency order. The export rewrites each reusable version id, and every reference to one, to the engine's catalog id
(`ActivityCatalogStableIds.VersionId`), wherever an `activityVersionId` member appears, also inside a node's opaque
`Structure` payload. On load, the engine compiles a version's `DescriptorPayload` as its provider manifest
(`SourceOwnedActivityVersionPublisher`), so the export takes a reusable's descriptor from the publication's `Provider`
(the graph manifest), not from the catalog version, which holds the compiled runtime descriptor. Each field of `ActivityVersionReconciliationModel` has one source:

| Field | Source |
|---|---|
| `Id` | `null`, so the engine computes the definition id from the type key |
| `Version`, `ActivityTypeKey` | the publication |
| `DisplayName`, `Category`, `Description` | the activity definition |
| `ProviderKey`, `ProviderSchemaVersion`, `Descriptor` | the publication's `Provider` (the graph manifest, ids rewritten) |
| `ConsumerKey`, `ConsumerSchemaVersion`, `Inputs`, `Outputs`, `DesignFacets`, `ExecutionType` | the catalog version, which holds `ActivityCatalogProjection`'s projection of the publication's `Contract` |

Reloading is not perfectly lossless: the engine's source publisher sets the storage driver to `elsa.json` and the
default durability, and keeps only emitted outcomes. The import already uses those values, so nothing changes in
practice. Workflow envelopes carry `SourceCreatedAt` and `ContentHash` as null. T024 proves that the engine
loads the result. **Rejected**: changing the import's id formula or adding a version id to the JSON activity model;
both change other owners for a problem the exporter can solve alone.

## R-9 Report rendering

**Decision**: `report.html` is one static template, embedded in the tooling assembly, with inline CSS and script. The
report JSON is embedded as a `<script type="application/json">` block, and the script renders it. Script content is raw text. `report.json` is serialized from the JSON baseline
(ADR 0091, `JsonBaseline.Options`), whose relaxed encoder leaves `<`, `>` and `&` alone and which writes nulls; the report model's optional
members carry `[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]`, and the two flags `publishStoppedHere`
and `routine` are `bool?` set only when true, so an absent member stays absent, as
the schema requires. The baseline's encoder keeps those characters, so the HTML step itself
rewrites them as `\u003c`-style JSON escapes when it inlines the JSON, never as HTML entities. No
other data is written into the page (FR-021).

## R-10 Sanctioned patterns (framework §2.24.2)

There is no new seam. The CLI is a composition root, as a host is, and its test seam is internal. The shared
import run is a plain internal service. The writers are services with contracts beside the reader contracts. The
tooling composition registers them, and no feature behaviour changes. No new pattern is introduced, so the §2.24.3 gate
is not triggered.
