# Research: Elsa 3 workflows migration CLI

Facts are from the initiative branch at the commit this specification was cut from. Paths are repository-relative.

## R-1 The `dotnet-elsa` front end and worker

- The tool is `essentials/src/Cli/Elsa.Cli/` (`PackAsTool`, command `dotnet-elsa`). `ElsaCli.cs` builds a static
  `RootCommand` with two groups, `persistence` and `composition`. There is no plugin mechanism, so a third group is
  added there.
- The worker is `essentials/src/Cli/Worker/`. It references no product code: Nuplane comes in with
  `ExcludeAssets="runtime"`. The front end's `essentials/src/Cli/Elsa.Cli/WorkerProcess.cs` starts it with
  `dotnet exec --runtimeconfig <host>.runtimeconfig.json --depsfile <host>.deps.json Elsa.Cli.Worker.dll`. It takes one
  JSON request on stdin and writes one JSON response on stdout (`Worker/Program.cs`).
- The product entry point is reached by type name: `Worker/ToolingEntryPoint.cs` names
  `Elsa.Persistence.EntityFramework.Tooling.EfToolingHost` and resolves it with `GetType(name, throwOnError: false)`.
  A missing type is a refusal that names the minimum version. `WorkerRunner.ExecuteAsync` reads the deps file, loads
  the Nuplane package set, preloads the host closure, and then resolves the entry point.
- Exit codes live in `WorkerContract.cs` (`ToolExitCode`): 0, 1, 2 refusal, 3 resolution failure, 4 database failure.
  The new group maps resolution failures to 2, as the specification says (FR-004). This is done in the front end, per
  group, so the persistence group is unchanged.

**Decision**: add an `elsa3-migration` group to `ElsaCli.cs`. Run it on the same worker with a second command family,
which resolves a second frozen entry point, `Elsa3.Migration.Tooling.Elsa3MigrationToolingHost`, in the assembly
`Elsa3.Migration.Tooling`. Reuse `HostClosure` and `WorkerProcess` unchanged. `NuplanePackageSet` gains the run-scoped
install root of R-6. `ToolingEntryPoint` resolves the persistence type with `Assembly.GetType` on the persistence
assembly from the host closure. The migration assembly is not in the host closure, because it arrives as a Nuplane
package, so the worker finds it among the loaded package set's assemblies first and then calls `GetType`. A missing
assembly or type refuses with `entry-point-missing` (exit 2).

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
result. The CLI entry point calls the same service (FR-013). The reconciliation project grants the tooling assembly
`InternalsVisibleTo`, so no public surface is added (ADR 0080).

The run's collaborators (`Elsa3ImportSelection`, `ImportedWorkflowPublisher`, `Elsa3StartupPublishingGrant` and the
`Elsa3StartupPublishingAuthorization` decoration) are registered only by `Elsa3JsonReconciliationFeature`
(`Reconciliation/Elsa3JsonReconciliationFeature.cs`). That feature throws without `SourceId` and a path, and it
registers `Elsa3FolderImportTask`. The extraction therefore also moves their registration into one internal helper,
`Elsa3ImportRunRegistration.Add(IServiceCollection)`. The feature calls it, and the tooling composition calls it
without enabling the feature.

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
- Several messages, and some guidance, interpolate input text: the strict-mode and safe-lowering parse messages pass
  the parser's description through (`Elsa3ExpressionRewriter.cs`), the Switch mode and case-condition messages quote
  literals (`Elsa3SwitchValueSynthesisRewrite.cs`), the Fork join-mode message can quote a whole typed value
  (`Elsa3ParallelBranchNamesRewrite.cs`), the HTTP status and `canStartWorkflow` messages quote literals
  (`Elsa3ActivityNormalizer.cs`), the variable starting-value messages echo the value and one guidance builds a
  suggestion from its items (`Elsa3VariableMapper.cs`), and several sites pass `exception.Message` through
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

The baked settings also keep the OpenIddict store and the Data Protection key ring under `/app/data` (specification
1236). The run points the OpenIddict store at the same in-memory database, and gives Data Protection an ephemeral key
ring, so nothing reaches a disk (FR-010).

## R-5 Composition without a web host

- Non-web compositions exist: `essentials/tests/Modularity/Tests/NuplaneHostTestComposition.cs`
  (`Host.CreateApplicationBuilder`, `AddNuplaneHostIntegration`, `AddCShells().WithNuplaneFeatureDiscovery()`) and
  `essentials/src/Foundation/Hosting/FoundationShellHostingExtensions.cs`.
- `EfToolingConfigurationContext` already layers `appsettings.json`, `shells.json` and their environment variants for
  the persistence tooling.

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
- Specification 1236 provides the composition library `Elsa.Essentials.Composition`: `EssentialsFeatureList.Read`,
  `EssentialsFeatureAssemblies.All` and `AddEssentialsConfiguration(compositionFolder, bakedSettingsPath,
  userSettingsPath)`. It ships only in `/app` of the engine image and is not packed.

**Decision**: the entry point builds a generic host that is never started. It composes one shell from the image's
composition and the merged settings, as specification 1236 defines the merge. It reads the lists with
`EssentialsFeatureList.Read(<root>/composition)` and merges settings with `AddEssentialsConfiguration` (the
image's baked `appsettings.json`, then the `--settings` file). It runs every shell initializer except
`RunShellTasksInitializer`, and runs the startup tasks alone, in their dependency order, through a new startup-only
member on `ITaskManager`. That is a small essentials change. It makes the features of the run-scoped package set discoverable, because `EssentialsFeatureAssemblies.All` lists
only the essentials features' assemblies, while the Elsa 3 additions and a user's packages arrive as Nuplane packages.
CShells' own Nuplane discovery reads a DI catalog that a Nuplane-integrated host fills, and the worker loads packages
through static `NuplaneHostIntegratedLoader` calls instead. So the tooling registers a feature-assembly provider
(`RegisterFeatureAssemblyProvider`) over the assemblies of the worker-loaded package load contexts, found through
`AssemblyLoadContext.All`, as `EfToolingHost` finds its assemblies. It resolves services from the shell's provider.

`<root>` is a request field, `compositionRoot`, which defaults to `/app`. Tests lay out a composition folder, baked
settings and an empty `migrated/` skeleton under a temporary root. The essentials list bakes JSON workflow and activity
reconciliation over the absolute paths `/app/migrated/workflows` and `/app/migrated/activities/activities.json`, so the
test's baked settings override both paths to `<root>/migrated/...`. Background
hosted services, background tasks and recurring tasks never start, so there is no trigger runtime, scheduler or
recurring work. The Elsa 3 folder import feature is not
composed. The engine image refuses to start without some secret settings (specification 1236, *Required settings*).
Specification 1236's feature-list contract asks the CLI to supply throwaway values for every one of them. For each one
that `--settings` leaves empty, the run generates a value that lives only in memory (FR-008a).

## R-6 User packages

- `Worker/NuplanePackageSet.cs` loads a state file, or a probed install root with `.nuplane-ready` markers.
  `Worker/NuplaneDirectoryFeeds.cs` and `NuplaneRestoreRunner.cs` already reconcile from directory feeds for
  `--restore`.

**Decision**: on every run, the worker reconciles `/app/elsa3/packages/` (R-7), plus the `--packages` folder for
`assess`, as directory feeds into one run-scoped temporary install root. The persistence group's `--packages` is a
different thing, an install root of extracted packages. The two groups never share the option. Package resolution is restricted to that feed and the image's closure. It then loads that root through
`NuplanePackageSet`, and deletes the root at the end. A dependency that cannot be resolved is a refusal (FR-004,
spec Q9).

ADR 0076 D1 says the worker never reconciles on its own initiative, and D10 says no command reconciles by default,
because a reconcile pass writes a host's shared state file and install root. This reconcile touches neither: it
reads local folders, writes only a private root it deletes, and uses no network. The ADR 0076 amendment (spec Q4)
states that exception.

## R-7 The Elsa 3 migration image

The CLI runs inside an image built `FROM` the essentials engine image at the same version (spec Q3). Specification
1236 defines the engine image's base contract for a derived image:

- `/app/composition/essentials.features.json` holds the essentials list. A derived image adds its own
  `<name>.features.json` there, and the composition library reads it after the essentials list, in ordinal file-name
  order.
- `Elsa.Essentials.Composition.dll` and every listed essentials feature's assemblies are in `/app`.
- The engine host reads every list in `/app/composition/`, but loads no package a derived image adds. A derived tool
  composes for itself, and the migration image never starts the engine host.
- A derived image uses its base's version tag, and replaces the entry point.

**Decision**: the migration image uses that contract and rebuilds nothing.

- `extensions/Elsa3/docker/elsa3.features.json`, the Elsa 3 addition list, is copied to
  `/app/composition/elsa3.features.json`. It holds the converter (`Elsa3Mapping`), the collection import for
  reusables, the import's EF ledger and the publishing bridge.
- The packages of those features, and the `Elsa3.Migration.Tooling` package, go into `/app/elsa3/packages/`. That folder
  belongs to this specification, not to the engine contract, and the worker reconciles it (R-6). The existing pack
  workflow already packs the `extensions/` projects.
- The worker loads packages with `HostIntegratedLoadOptions` built from the engine image's baked
  `Nuplane:Loading:SharedAssemblies` and `Nuplane:HostProvidedPackages`, plus `Elsa.Essentials.Composition`. Today
  `NuplaneLoader` passes no options (`essentials/src/Cli/Worker/NuplaneLoader.cs`), and then a feed-loaded EF module
  such as the import's EF ledger would bind its own copy of `Elsa.Persistence.EntityFramework`, where ADR 0076 requires
  the host's. Specification 1236 leaves the shared-assembly declaration for its composition library to this
  specification.
- The tooling project references `Elsa.Essentials.Composition` with `Private=false` and `PrivateAssets="all"`, so the
  packed tooling package neither carries the library nor depends on it. The library is not packed.
- The Dockerfile sets `ELSA3_MIGRATION_HOST=/app`. The front end resolves it with `HostLayout.Resolve`, exactly as the
  persistence group resolves `--host` (a directory holding one `*.runtimeconfig.json` and `*.deps.json` pair), and maps
  that resolver's refusals to `entry-point-missing`.
- Image tags are build numbers, not package versions (`.github/workflows/docker.yml`), and every packable project has
  its own computed version (`PackageVersioning.props`). So the Dockerfile takes the essentials image tag as a build
  argument and bakes it as `ELSA3_MIGRATION_IMAGE_VERSION`. The front end passes it, and its own informational
  version, to the entry point as `imageVersion` and `toolVersion`. They become `run.imageVersion` and `tool.version`.
- `dotnet-elsa` is installed in the version computed for the same commit. The entry point is `dotnet-elsa elsa3-migration`, with the worker
  pointed at the engine host's build output in `/app`. No web host starts.
- The image is published as `elsaworkflows/elsa-elsa3-migration`, with the essentials engine's tags; each tag is built `FROM` the engine
  image with that tag.
- Mount points follow the engine's: `/app/config/appsettings.json` and `/app/packages` hold the user's settings and
  packages, as on the engine. `/app/export` (read-only) holds the export, and `/app/out` the output. The engine later
  mounts the output's `migrated/` at `/app/migrated`.

**Rejected**: a NuGet reference host restored on first use, and a host bundled in the tool. Both would ship a second
Elsa 4 closure that can drift from the engine the user goes live on.

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
round-trip test checks read(write(x)) = x. The tooling entry point reads the published versions back from the
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

There is no new seam. The entry point is the ADR 0076 frozen entry point pattern under its amendment. The shared
import run is a plain internal service. The writers are services with contracts beside the reader contracts. The tooling
composition registers them, and no feature behaviour changes. No new pattern is introduced,
so the §2.24.3 gate is not triggered.
