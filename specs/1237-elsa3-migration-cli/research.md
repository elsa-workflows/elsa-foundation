# Research: Elsa 3 workflows migration CLI

Facts are from the initiative branch at the commit this specification was cut from. Paths are repository-relative.

## R-1 The `dotnet-elsa` front end and worker

- The tool is `essentials/src/Cli/Elsa.Cli/` (`PackAsTool`, command `dotnet-elsa`). `ElsaCli.cs` builds a static
  `RootCommand` with two groups, `persistence` and `composition`. There is no plugin mechanism, so a third group is
  added there.
- The worker is `essentials/src/Cli/Worker/`. It references no product code: Nuplane comes in with
  `ExcludeAssets="runtime"`. `WorkerProcess.cs` starts it with
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
`Elsa3.Migration.Tooling`. Reuse `NuplanePackageSet`, `HostClosure` and `WorkerProcess` unchanged.

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

**Rejected**: running the startup task itself in the CLI. It reads only `FilePath`/`FolderPath` options, logs instead of
returning, and would have to run as a startup task, which FR-008 rules out.

## R-3 Diagnostics and the code table

- `Elsa3MigrationDiagnostic` (`extensions/Elsa3/src/Models/Elsa3MigrationBoundary.cs`) carries severity, code,
  message, JSON path, guidance and metadata (`DefinitionId`, `DefinitionVersion`, `ActivityNodeId`, `Elsa3Type`,
  `Property`).
- Code constants: `Elsa3ActivityDiagnosticCodes` (`ELS3-ACTIVITY-*`, `ELS3-BEHAVIOUR-*`), `Elsa3ImportDiagnosticCodes`
  (`VF-IMP-001` to `012`, in `Mapping/Services/Elsa3ExpressionRewriter.cs`), `Elsa3MigrationDiagnosticCodes`,
  `Elsa3ReconciliationDiagnosticCodes`, plus `ELS3-UNTYPED-RESULT` and `ELS3-SCRIPT-OUTCOME-*`.
- Guidance strings are constant literals today, while some messages interpolate. One example is the strict-mode parse
  message, which embeds the parser's error description, and that description can quote script text
  (`Elsa3ExpressionRewriter.cs`, the sloppy-mode message).
- Publish refusals come from Elsa 4 (`ExpressionPublicationValidationException`, `WorkflowExecutableCompilationException`,
  `PublicationActivationException`). Their diagnostics use Elsa 4 codes.

**Decision**: add one code table, `Elsa3MigrationCodeTable`, in `extensions/Elsa3/src/Models/`, next to the
constants. It maps every Elsa 3 code to its remedy (`patch-definition`, `build-component`, `none`), and component codes
to a component kind. A reflection test enumerates every code constant and fails when one has no entry (FR-016). Elsa 4
publish diagnostics get remedy `patch-definition` by default, because a published Elsa 3 definition that Elsa 4 refuses
must be changed or reported as a converter gap. Messages that embed input text are changed at the converter so that
they name the cause without the text (FR-019). The sentinel test finds every such message.

## R-4 In-memory SQLite across modules

- `EfSharedTransaction.EnsureSingleTarget` (`essentials/src/Persistence/EntityFramework/EfSharedTransaction.cs`) throws
  `EfSharedTransactionTargetMismatchException` when providers or connection strings differ. Serializable mode refuses a
  supplied connection. A plain `:memory:` database belongs to one connection.
- Tests use `Data Source=file:<name>;Mode=Memory;Cache=Shared`, for example in
  `essentials/tests/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/BackgroundActivityWorkTests.cs`.

**Decision**: one connection string, `Data Source=file:elsa3-migration-<guid>;Mode=Memory;Cache=Shared`, is set on every
enabled EF feature. The host-wide migrate policy is set to apply migrations, and one connection stays open from
composition until disposal (FR-009). The guid never reaches the report (FR-022).

## R-5 Composition without a web host

- Non-web compositions exist: `essentials/tests/Modularity/Tests/NuplaneHostTestComposition.cs`
  (`Host.CreateApplicationBuilder`, `AddNuplaneHostIntegration`, `AddCShells().WithNuplaneFeatureDiscovery()`) and
  `essentials/src/Foundation/Hosting/FoundationShellHostingExtensions.cs`.
- `EfToolingConfigurationContext` already layers `appsettings.json`, `shells.json` and their environment variants for
  the persistence tooling.

**Decision**: the entry point builds a generic host without starting it. It composes one shell from the image's
composition and the merged settings, as specification 1236 defines the merge, and resolves services from that shell's
provider. No `IHostedService` or startup task runs. Feature `ConfigureServices` and the EF migrations run, because
publish needs the schema.

## R-6 User packages

- `Worker/NuplanePackageSet.cs` loads a state file, or a probed install root with `.nuplane-ready` markers.
  `Worker/NuplaneDirectoryFeeds.cs` and `NuplaneRestoreRunner.cs` already reconcile from directory feeds for
  `--restore`.

**Decision**: on every run, the worker reconciles the image's Elsa 3 addition folder (R-7), plus the `--packages`
folder for `assess`, as directory feeds into one run-scoped temporary install root. Package resolution is restricted to that feed and the image's closure. It then loads that root through
`NuplanePackageSet`, and deletes the root at the end. A dependency that cannot be resolved is a refusal (FR-004,
spec Q9).

## R-7 The Elsa 3 migration image

The CLI runs inside an image built `FROM` the essentials engine image at the same version (spec Q3). The engine image
is specification 1236's. Two of its properties matter here: its host build output, with the `.runtimeconfig.json` and
`.deps.json` that the worker needs (D1), and the way it adds feature packages on top of the baked composition.

**Decision**: the migration image adds three things:

- **The Elsa 3 addition list**, `extensions/Elsa3/docker/elsa3-additions.json`: the converter (`Elsa3Mapping`), the
  collection import for reusables, the import's EF ledger, the publishing bridge, and the tooling assembly. The
  projects are packed as Nuplane packages, as the existing pack workflow already packs `extensions/` projects. They are
  baked into an image-owned package folder that the image loads exactly as it loads a user's custom feature packages.
  The worker therefore resolves the frozen entry point in the image's own closure.
- **The `dotnet-elsa` tool**, installed at the image's version.
- **The entry point** `dotnet-elsa elsa3-migration`, with the worker pointed at the engine host's build output in the
  image. No web host starts.

The Dockerfile lives at `extensions/Elsa3/docker/Dockerfile`, and the image is published beside the essentials images,
at their version. If specification 1236 bakes features into the host build output instead of loading them as packages,
the addition list follows whatever mechanism that specification provides for adding features to the engine image.
The list stays the same.

**Rejected**: a NuGet reference host restored on first use, and a host bundled in the tool. Both would ship a second
Elsa 4 closure that can drift from the engine the user goes live on.

## R-8 Exporting migrated content

- JSON workflow reconciliation (`essentials/src/Workflows/Design/Reconciliation/Json/`) reads a top-level array of
  `WorkflowVersionReconciliationModel` through `IPayloadSerializer`. `FolderPath` reads top-level files in ordinal
  order. `activityVersionId` values are deterministic catalog ids.
- JSON activity reconciliation (`essentials/src/Activities/Design/Reconciliation/Json/`) reads
  `ActivityVersionReconciliationModel` arrays, and `Files` are read in ascending `Order`. Its README documents that
  source-owned composites with dependencies are refused (`activity.source.dependencies-unsupported`,
  `essentials/src/Workflows/Publishing/Api/Services/SourceOwnedActivityVersionPublisher.cs`).
- Only readers exist. No writer produces either format.

**Decision**: add a writer next to each reader, in the same project, so the format has one owner:
`JsonWorkflowCatalogWriter` and `JsonActivityCatalogWriter`. Each serializes models through `IPayloadSerializer`, and a
round-trip test checks read(write(x)) = x. The tooling entry point reads the published versions back from the
design stores and writes them. Workflows go one file per definition, named by definition id. Activities go in one file,
in dependency order. Spec Q2 covers the engine refusal.

## R-9 Report rendering

**Decision**: `report.html` is one static template, embedded in the tooling assembly, with inline CSS and script. The
report JSON is embedded as a `<script type="application/json">` block, HTML-escaped, and the script renders it. No
other data is written into the page (FR-021).

## R-10 Sanctioned patterns (framework §2.24.2)

There is no new seam. The entry point is the ADR 0076 frozen entry point pattern under its amendment. The shared
import run is a plain internal service. The writers are services with contracts beside the reader contracts. The tooling
composition registers them, and no feature behaviour changes. No new pattern is introduced,
so the §2.24.3 gate is not triggered.
