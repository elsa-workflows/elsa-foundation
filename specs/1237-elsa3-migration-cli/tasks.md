# Tasks: Elsa 3 workflows migration CLI

**Input**: [spec.md](spec.md), [plan.md](plan.md), [research.md](research.md), [data-model.md](data-model.md),
[contracts/](contracts/), [quickstart.md](quickstart.md).

**Prerequisites before implementation starts**:

- this specification is approved;
- the migration host of specification 1236 exists on the initiative branch, implemented;
- the converter's member-name matching of Elsa 3 globals to prefixed host functions, and its storage-driver change
  (the two `ELS3-BEHAVIOUR-*-STORAGE-*` codes with `StorageDriverType` and `ComponentName`), are on the initiative
  branch (done: PRs #5703 and #5709);


**Tests**: Risk-based (framework §2.23). Each test is written with the code it covers:

- code-table completeness;
- the bucket rule;
- redaction;
- determinism;
- the single database;
- the output swap;
- the refusal paths;
- the shared-pipeline extraction;
- the composition parity with the migration host.

**Tooling root**: `extensions/Elsa3/src/Migration/Tooling/`, written `Tooling/` below.
**CLI root**: `extensions/Elsa3/src/Migration/Cli/`, written `Cli/` below.
**Tests root**: `extensions/Elsa3/tests/Migration/`, written `MTests/` below.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: US1 to US5 from spec.md

---

## Phase 1: Setup

- [ ] T001 Create `Tooling/Elsa3.Migration.Tooling.csproj`. It references the Elsa 3 models, mapping, import, import
  persistence, publishing and reconciliation projects, and the JSON workflow and activity reconciliation projects.
  Create `Cli/Elsa3.Migration.Cli.csproj`, a console project (`IsPackable` `false`, no `RunWorkingDirectory`) that
  references the tooling and `apps/Elsa.Migration.Host/Elsa.Migration.Host.csproj`, and writes the host's project folder
  and its own project folder into `AssemblyMetadataAttribute`s at build time (R-5). The tooling does not reference the
  host. Create `MTests/Tests/Elsa3.Migration.Tests.csproj` (xunit); it references the CLI project, so the host's
  features come into its `deps.json` for `WithHostAssemblies()`, and the T003 fixture feature project.
  Add all three to the solution, regenerate the solution filters and `dependency-map.json`, and check the CI tier
  placement (§E7). Add the guard entries of R-7: the CLI in `EfCoreDependencyGuardTests.AllowedEfConsumers` with a
  reviewed package array and an arm in `ReviewedEfPackages`, a reviewed closure for the tooling next to
  `Adr0073Elsa3ImportEf`, and one for the test project (the host's closure plus SQLite); and the CLI as a build-only
  reference in `Elsa.Architecture.Tests.csproj`. Confirm that `ArchitectureGuardTests`, `ExtensionBoundaryTests`,
  `Elsa3MigrationBoundaryTests` and 1236's host closure test stay green without other entries.
- [ ] T002 [P] Build the fixture export in `MTests/Fixtures/export/`, with:
  - clean definitions;
  - an unmapped custom activity used by three definitions;
  - an undeclared JavaScript function, called in one definition and only passed as a value in another
    (`items.map(lookupCustomer)`), a matchable refused Elsa 3 built-in (`getConfig()`), and an unmatchable call;
  - a definition with no published version;
  - an unregistered variable type, a variable on a custom storage driver, and one on the Elsa 3 memory driver;
  - a reusable definition with two users;
  - a definition whose reusable dependency is refused;
  - a C# expression;
  - a JavaScript call of a .NET-style member on a sentinel literal, which the converter refuses as `VF-IMP-012` with
    the analyzer's message, which quotes the call;
  - a dispatch target absent from the export;
  - a Switch whose case conditions are a non-boolean literal and an unparsable JavaScript expression, and a Switch
    with an unsupported mode literal;
  - a Fork whose join mode is an expression;
  - an unparsable Liquid template;
  - variables of known types (an integer, a collection, a dictionary) whose starting values cannot be converted;
  - a script that parses only in sloppy mode;
  - a definition with three versions: the latest published one, an older one pinned by a reusable reference, and an
    unpinned one;
  - duplicate input;
  - one non-JSON file;
  - a zip copy.

  Every literal, script body, variable default and setting value carries the sentinel `SENTINEL-1237`, except
  identities that the report may name (FR-019): definition ids and names, including the dispatch target's id, type
  names, function names and member names (input, output, variable and property names, and literal keys that name a
  target member or input).
- [ ] T003 [P] Build the fixture feature project in `MTests/Fixtures/AcmeFeature/`. It registers the custom
  activity under its Elsa 3 type name, `acme.`-prefixed JavaScript host functions for the undeclared function and for `getConfig`, and a variable type
  alias. The test project references it, and a test-only host folder (the host's committed files plus a
  `shells.Local.json` that lists the feature and its settings) is passed through the tooling's internal seam (R-5).

## Phase 2: Foundational (blocks every story)

- [ ] T004 Add `Tooling/Elsa3MigrationCodeTable.cs`. It maps each code to a remedy, a component kind, a routine mark
  and, for codes the converter does not own, a fixed text, with `patch-definition` and a default text for unlisted
  Elsa 4 codes. Give it an entry for every code the converter, the import and reconciliation emit, including the codes
  of the converter diagnostic units (among them `ELS3-REPLACED-FUNCTION-REFERENCE`, remedy `patch-definition`, no component kind: it is
  emitted only once a host function provides the name, so nothing is left to build) and the tooling's own `ELS3-MIGRATION-*` codes. Every Elsa 3 entry has a remedy. Mark `ELS3-NULL-POLICIES` routine. Promote the codes that are emitted as inline
  literals (`ELS3-INPUT-DEFAULT-REQUIRED`, `ELS3-NULL-POLICIES` and any others in `Elsa3ActivityNormalizer` and its
  peers) to constants. Add the completeness test in `MTests/Tests/`. It reflects over every code constant class in
  Models, Mapping, Import (`ReusableActivityImportDiagnosticCodes`) and Reconciliation, and fails on any code without
  an entry or without a remedy, whatever its severity. It reflects over the tooling's own code class too. It also greps the Elsa 3 sources for `"ELS3-`, `"VF-IMP-` and `"ELS3-REUSABLE-`
  string literals outside the constant classes, and fails on any (FR-016, FR-019).
- [ ] T004a Extract `MigrationHost.AddHostConfiguration` and `MigrationHost.AddHostServices` from the migration host's
  `CreateBuilder` (`apps/Elsa.Migration.Host/`), as spec *Dependencies and boundaries* describes. The host keeps
  calling them, and 1236's host tests stay green unchanged. The shapes are
  `AddHostConfiguration(IConfigurationBuilder, string hostFolder, string environment, Assembly hostAssembly,
  string[] args, bool includeLocalSources)`, so the CLI can build into its own builder under the key filter, and
  `AddHostServices(IHostApplicationBuilder)`, so both the host's `WebApplicationBuilder` and the CLI's
  `HostApplicationBuilder` can call it. The host does not clear `WebApplication.CreateBuilder`'s default configuration
  sources: it calls `AddHostConfiguration` on top of them, so the re-added layers override the defaults and the
  effective order is still 1236 R-3's, while the `ASPNETCORE_`/`DOTNET_` host keys survive (for example
  `ASPNETCORE_URLS`, which reaches Kestrel only as `urls` through the prefixed source and carries 1236's fixed URL).
  Only the CLI starts from an empty builder, where `AddHostConfiguration` alone produces the order
  (`appsettings*.json` included). Add a host test that `ASPNETCORE_URLS` still reaches `IConfiguration` as `urls`;
  1236's other host tests stay green unchanged.
  `AddHostServices` registers the root services, CShells with `WithHostAssemblies()` included, without the web
  pipeline, and creates no folders; the host creates `data/` at startup, outside it.
- [ ] T004b Make the converter carry component names as data (FR-016a). Add a `ComponentName` metadata key to every
  component diagnostic: unmapped activity types, undeclared-function references, unregistered variable
  types and custom storage drivers. That includes giving `Elsa3ExpressionRewriteIssue` metadata. Where
  `ELS3-BEHAVIOUR-CUSTOM-STORAGE-DRIVER-REPLACED` already carries `ComponentName`, leave it; test that the inventory
  lists both the custom driver and the memory driver as storage-driver components. Give every call, and every read of an undeclared global used as a value, that the
  converter reports as matchable (`CanBeMatched`: an undeclared custom global or a refused built-in a host function may
  take over) a `javascript-function` component code of its own, separate from the forbidden-capability code, which
  unmatchable calls and forbidden capabilities keep. Cover every emission site, not only the `CanBeMatched` one in
  `Elsa3ExpressionRewriter`: the `get{Name}()` and `set{Name}()` calls with no matching variable or input in
  `Elsa3ValueFlowLowerer` (the `UndeclaredFunctionGuidance` sites) get the component code too, and so does the
  read-only workflow-input branch of `set{Name}()` in `Elsa3ExpressionRewriter` (Elsa 3 generated no setter for
  inputs, so a call that ran there was a custom global); its guidance keeps both routes. Add a `get`-prefixed custom
  global (`getCustomerData()`) to the T002 fixture. Update the mapping README and the affected converter tests, and
  test the fixture calls and the value-only reference. The report builder reads `componentName` only from that metadata.
- [ ] T005 Extract `Elsa3ImportRun` from `Elsa3FolderImportTask` in
  `extensions/Elsa3/src/Activities/Design/Import/Reconciliation/Services/`. It returns the plan items, the skips with
  their cause and code, the publication outcomes and the publish diagnostics. Move the registration of its collaborators
  (`Elsa3ImportSelection`, `ImportedWorkflowPublisher`, `Elsa3StartupPublishingGrant`, the publishing authorization
  decoration, and the `TimeProvider.System` registration `ImportedWorkflowPublisher` needs) out of `Elsa3JsonReconciliationFeature` into an internal `Elsa3ImportRunRegistration.Add`, which the
  feature calls. Make the startup task call the run and log as before, and add
  `InternalsVisibleTo("Elsa3.Migration.Tooling")`. The existing reconciliation integration tests must
  stay green unchanged (§2.21.1, FR-013).
- [ ] T006 [P] Add `IJsonWorkflowCatalogWriter` and `JsonWorkflowCatalogWriter` in
  `essentials/src/Workflows/Design/Reconciliation/Json/`, with a round-trip test against `JsonWorkflowCatalogReader`.
  Update the project's README, and create its `EXTENSION_POINTS.md`, which it does not have yet (R-8).
- [ ] T007 [P] Add `IJsonActivityCatalogWriter` and `JsonActivityCatalogWriter` in
  `essentials/src/Activities/Design/Reconciliation/Json/`, with a round-trip test against `JsonActivityCatalogReader`.
  Update the project's README and `EXTENSION_POINTS.md` (R-8).
- [ ] T008 Add `Tooling/Elsa3MigrationRun.cs`, the pipeline the CLI calls in-process. It takes the command, the export
  path, the output path, `--all-versions`, the tool version, and from the CLI the host assembly, the host project folder
  and the CLI project folder (the internal test seam overrides the last three), and returns the exit code, the output path and, on
  exit 2, the refusal reason and sentence of [contracts/cli.md](contracts/cli.md). Test the request validation.
- [ ] T009a Add a startup-only member to `ITaskManager` (`essentials/src/Tasks/Core/ITaskManager.cs`), implemented in
  `essentials/src/Tasks/Elsa.Tasks/Services/TaskManager.cs`. It runs the registered
  startup tasks in their dependency order (`TopologicalTaskSorter`) and starts no background or recurring task. Add a
  supported `StartupTasksOnly` setting to the `Tasks` feature (default `false`), which `RunShellTasksInitializer`
  reads: when it is set, the initializer calls the startup-only member instead of `StartExecutingRegisteredTasks`.
  Document the setting in the Tasks README. Test the order, the default, and that with the setting no
  `IBackgroundTask` or recurring task starts (R-5).
- [ ] T009 Add `Tooling/MigrationComposition.cs` (R-4, R-5). It depends on T013 (the addition list and the
  `Elsa3MigrationImportRun` feature), so do T013 first even though it is listed under US3. It takes the host assembly, the host project folder and
  the CLI project folder from the CLI, builds an empty generic host (`Host.CreateEmptyApplicationBuilder`) that is
  never started, with `ContentRootPath` set to the host folder and `EnvironmentName` set to Development, and uses the
  T004a methods; it copies none of the host's wiring. After building, it activates the shell itself with
  `IShellRegistry.GetOrActivateAsync` and the host composition's shell name (the host's activation hosted service never
  runs, because this host is never started); an exception from that call maps to `composition-failed` (T026), and
  `--out` is created only after it returns. Configuration comes from the host project folder, never from the
  CLI's output folder, in this order:
  1. `MigrationHost.AddHostConfiguration`, read through a key filter that drops `Files`, `FilePath` and
     `AdditionalSources` under JSON workflow reconciliation and `Files` under JSON activity reconciliation.
     `inventory` leaves out the local sources (the host's `appsettings*.json`, `shells.json` and
     `shells.Development.json` remain); `assess` includes them (`shells.Local.json`, the host's user secrets and
     environment variables), with no command-line arguments;
  2. `elsa3-migration.features.json` from the CLI project folder;
  3. the run-override layer: every persistence connection string set to SQLite on
     `file:elsa3-migration-<guid>;Mode=Memory;Cache=Shared`, the migrate policy set to apply, JSON workflow
     reconciliation's `FolderPath` at an empty `workflows/` folder and JSON activity reconciliation's `FilePath` at an
     activities file holding `[]`, both in a run-scoped temporary folder outside `--out` that it deletes on every
     path, the `Tasks` feature's `StartupTasksOnly` (T009a), and, when the composed configuration lists
     `FileSystemDistributedLocking`, its `LocksFolderPath` at a `locks/` folder in the same run-scoped temporary folder
     (the feature's default is `App_Data/locks` under the current directory; the key is set only when the feature is
     already listed, because writing a key under `Features` would enable it). Test that no `App_Data` folder appears in
     the current directory.

  Then it calls `MigrationHost.AddHostServices`, and replaces Data Protection at root with an ephemeral key ring. It
  creates `--out` and its staging folder only after the shell has been composed. All shell initializers run; the
  T013 feature registers the import run inside the shell. No background hosted service starts. It keeps one
  connection open, and disposes everything on every path. Tests:
  - two EF modules commit in one shared transaction without `EfSharedTransactionTargetMismatchException`;
  - after composition, the schema exists and the activity catalog holds the host's activities;
  - no background hosted service, `IBackgroundTask` or recurring task starts;
  - `inventory` ignores a `shells.Local.json`, a user secret and an environment variable that `assess` applies;
  - a local source that sets `Files`, or `AdditionalSources` on the workflow feature, neither loads anything into the
    run nor makes it refuse;
  - no environment variable, current-directory `appsettings.json` or `DOTNET_ENVIRONMENT` reaches `inventory`;
  - a `composition-failed` refusal leaves a missing `--out` missing and an existing one untouched;
  - an addition-list feature and a T003 feature both resolve;
  - the host's sample workflow does not load into the run;
  - the tests use a temporary host folder through the internal seam;
  - no file appears outside a temporary `--out`, and no `data/` folder appears under the host project folder.

  (FR-008 to FR-010, SC-003.)
- [ ] T011 Add `Cli/Program.cs`, with:
  - the `workflows inventory` and `workflows assess` commands (System.CommandLine);
  - the argument rules and refusal reasons of [contracts/cli.md](contracts/cli.md);
  - the exit codes (FR-004);
  - the printed output path, or the refusal line on standard error (FR-005).

  It passes the CLI's informational version as the tool version. Test the argument combinations, the refusal lines
  and the exit codes, and confirm that `Elsa3MigrationBoundaryTests` stays green.

## Phase 3: User Story 3 - Same composition as the migration host (P1)

- [ ] T013 [US3] Add `Tooling/Elsa3MigrationImportRunFeature.cs`, the shell feature `Elsa3MigrationImportRun`. It
  `DependsOn` `WorkflowsPublishingApi`, `WorkflowsPublishing`, `Elsa3ImportActivitiesPublishing` and `Tasks`, as
  `Elsa3JsonReconciliationFeature` does for the same publisher, and its `ConfigureServices`
  calls `Elsa3ImportRunRegistration.Add` (R-5). Add `Cli/elsa3-migration.features.json`, the Elsa 3 addition list in
  the CShells feature shape of the host's `shells.json`: `Elsa3Mapping`, the collection import for reusables, the
  import's EF ledger, the publishing bridge and `Elsa3MigrationImportRun`. It is read from the CLI project folder only,
  never copied to the output. It only adds feature entries. Test that the feature registers the import run and the
  publishing authorization decoration in the shell.
- [ ] T015 [US3] Add the composition parity test (R-5): in both a baseline empty application builder with the same
  settings and the CLI's builder, snapshot the descriptors before and after the `MigrationHost.AddHostServices` call,
  and compare the descriptors that call added as multisets (the CLI's added set taken after its Data Protection
  replacement), keyed by (ServiceType,
  ServiceKey, Lifetime, ImplementationType, or the marker `factory` or `instance`), excluding the Data Protection
  descriptors the ephemeral key ring replaces (`IDataProtectionProvider`, `IKeyManager`,
  `IConfigureOptions<KeyManagementOptions>`, and the `IHostedService` of `DataProtectionStartupCheck`); every EF
  feature resolves the run's connection string; the shell starts no background or recurring task; `inventory`'s
  enabled features equal the host's committed
  composition plus the addition list and their dependencies; `assess` adds a feature listed in a local source; a
  listed feature that no project provides refuses with `composition-failed` and names it; and the migration host's
  closure contains no assembly whose name contains `Elsa3`.

## Phase 4: User Story 1 - Inventory an export (P1)

- [ ] T016 [US1] Add `Tooling/ExportInputReader.cs` (FR-011, edge cases). It reads a folder (recursive `*.json`, in
  ordinal order) or a zip, with one definition or an array per file. A file that is not valid JSON becomes an
  `ELS3-MIGRATION-INPUT-UNREADABLE` finding, and valid JSON with no Elsa 3 definition an
  `ELS3-MIGRATION-UNSUPPORTED-INPUT` finding.
  It refuses with `input-missing`, `input-empty` and `input-unreadable`. Test it with the fixture.
- [ ] T017 [US1] Add `Tooling/VersionSelection.cs` (FR-012). It selects the latest published version plus the versions
  that reusable references pin, lists the versions not converted, supports `--all-versions`, and adds the Info finding
  (`ELS3-MIGRATION-NO-PUBLISHED-VERSION`) for a definition with no published version, which the tests and the T021
  golden file assert. It keeps every input that shares
  a selected version id, or a selected definition id and version, so that duplicates still reach the duplicate-input
  refusal. Test each fixture case, the duplicate case included.
- [ ] T018 [US1] Wire `Elsa3MigrationRun`: read and validate the export first, before composition (`ExportInputReader`
  needs no shell, so `input-missing`, `input-unreadable` and `input-empty` refuse before `--out` exists; test that an
  `input-empty` refusal leaves a missing `--out` missing), then compose (T009), select, run `Elsa3ImportRun`, and collect every diagnostic with its stage.
  Publish stops at the first error and marks `publishStoppedHere` (FR-013, FR-014). `Elsa3ImportRun` returns a refusal
  it cannot pin on one definition instead of throwing (the startup task still throws on it), and the report lists it
  in `runFindings`. Mark older selected workflow versions and the wrappers of older reusable versions
  `ELS3-MIGRATION-SUPERSEDED-VERSION`; older reusable versions' activities stay published. On a run-level refusal, give
  every affected definition `ELS3-MIGRATION-RUN-REFUSED`. Test a plan error that names no
  item, a credential-literal refusal at apply (exit 1, a report, no message text, no definition bucketed
  `convert-as-is`), and `--all-versions` with three
  versions (only the newest published and exported).
- [ ] T019 [US1] Add `Tooling/ReportBuilder.cs`. It provides:
  - categories (FR-015);
  - remedies and component kinds from the code table (FR-016);
  - buckets and blocked-by (FR-017);
  - all the sections of FR-018: summary, components with usage counts, definitions, versions not converted, missing
    targets, input findings and run;
  - the ordering rules of data-model.md.

  Every skip is an `error` finding. A definition goes smoothly when its only findings are routine. Unit-test the
  bucket rule, the category mapping, the skip severity and the goes-smoothly rule table-driven, and test the component
  inventory: no unused component, correct counts (SC-001).
- [ ] T020 [US1] Detect dispatched and executed workflow targets missing from the export, from the converted
  definitions' dispatch and execute-workflow activities. List them as Take into account (FR-018 item 5), and test with
  the fixture.
- [ ] T021 [US1] Add a golden-file integration test in `MTests/Tests/`. The fixture export runs through `inventory`
  in-process: `report.json` must equal the committed golden file after `tool.version` is normalized, it must validate
  against `contracts/report.schema.json`, and the exit code must be 1. A second run must be byte-identical,
  `migrated/` included (FR-022, SC-006).

## Phase 5: User Story 4 - Migrated content (P1)

- [ ] T022 [US4] Add `Tooling/MigratedExporter.cs` (FR-023, FR-024). It reads the published workflow versions and
  reusable activity versions back from the design stores and writes them through the T006 and T007 writers: one file
  per workflow definition, the wrapper workflow of each reusable among them, and one activities file in dependency
  order. A reusable's `exportedTo` is the activities file, and its `wrapperExportedTo` is the wrapper's file. Every
  published reusable version goes into `activities.json`, older pinned ones included. Add a fixture case of a workflow
  that pins an older reusable version, and assert that the older version is in `activities.json`. The engine gives each loaded activity version
  `ActivityCatalogStableIds.VersionId(typeKey, version)`, not the stored import id, so the exporter rewrites every
  reusable version id, and every reference to one, to that id: every `activityVersionId` member anywhere in a workflow
  or a reusable's graph manifest, including those inside a node's opaque `Structure` payload (children of a Flowchart
  or Sequence). A reusable's exported descriptor is its graph manifest, taken from the publication's `Provider`
  (key, schema version, manifest), not the catalog version's compiled runtime descriptor, which the engine would
  refuse as a manifest. Its consumer key, inputs, outputs, design facets (including `elsa.outcomes`) and
  execution type come from the catalog version, the engine's projection of the publication's `Contract`. Add fixture reusables with a custom Elsa 3 outcome next to `done`, and with a reusable nested in a container,
  used from both a workflow and another reusable. Map every field as research R-8's table says. Write
  `SourceCreatedAt` and `ContentHash` of each workflow envelope as null, so the engine computes the hash and no store
  timestamp reaches `migrated/`. Order envelopes in a workflow file by SemVer sort key, and `activities.json`
  topologically with an ordinal tie-break on (`ActivityTypeKey`, SemVer sort key). It sets
  `exportedTo`. It leaves out refused and blocked
  definitions. It always creates `migrated/workflows/` and always writes `migrated/activities/activities.json`, as
  `[]` when no reusable is exported. Test the layout, the absence, and a run that exports no workflow and no reusable.
- [ ] T023 [US4] Add the output swap: write to a hidden `.staging-<guid>` folder inside `--out`, rename `report.json`,
  `report.html` and `migrated/` into place last, and touch no other file. A failed run removes the staging folder and
  leaves the previous output intact. Test both paths, and test it again in the T034 smoke run.
- [ ] T024 [US4] Add the loadability test (SC-004). Compose the migration host's committed composition in-process, with
  its JSON workflow and activity reconciliation settings pointed at the fixture run's `migrated/`, and assert that every exported reusable and workflow loads,
  including the workflow that pins an older reusable version (T022), and that a run that exported nothing loads too
  (both entries empty). Each reusable goes through `SourceOwnedActivityVersionPublisher`, not only a parse; no version
  may be refused with `activity.dependency.version-not-found` or `activity.graph.outcome-done-required`, including the
  nested-reusable fixture (T022).

## Phase 6: User Story 2 - Assess with my own features (P1)

- [ ] T025 [US2] Layer the host's local sources for `assess` as R-5 says. Record setting keys only, those under
  `CShells:Shells:default` and `ConnectionStrings`, never machine environment variables outside them, and set
  `persistenceOverridden` when a persistence connection setting was replaced (FR-009, FR-018 item 4). Test that no
  setting value reaches the report.
- [ ] T026 [US2] Map composition failures to the `composition-failed` refusal, with no partial run: a listed feature
  that the build does not provide (`FeatureNotFoundException`), and a feature that throws while the shell is
  composed. Name the feature or setting key, never a value, and do not copy the exception message. Test with a listed
  feature that no project provides, and with a fixture feature whose configuration throws.
- [ ] T027 [US2] Add the assess integration test (SC-007). Run the fixture export with the T003 project listed in the
  test-only host folder's `shells.Local.json`. Every finding that names the custom activity, the function or the variable type disappears, except that the
  value-only read `items.map(lookupCustomer)` becomes `ELS3-REPLACED-FUNCTION-REFERENCE` (`patch-definition`), and the function is no longer
  listed under `components`. No other finding is added. The other definitions it unblocks publish clean.

## Phase 7: User Story 5 - Redacted, versioned report (P2)

- [ ] T028 [US5] Make every message and guidance from the converter, the import and reconciliation free of input text
  (FR-019). Start with the strict-mode parse message and the `VF-IMP-012` message (the .NET-usage analyzer's message,
  passed through, which quotes the call), and the Switch case-condition messages of
  `Elsa3SwitchValueSynthesisRewrite` (the literal reason, which quotes the literal, and the parse reason, which passes
  the parser's message through). Audit every interpolated message and guidance string in `extensions/Elsa3/src`
  (search for `$"` in diagnostic and issue constructors), and fix at least these known sites: the Switch mode message
  (`Elsa3SwitchValueSynthesisRewrite`), the Fork join-mode message, which can quote a typed value's raw text
  (`Elsa3ParallelBranchNamesRewrite`), the HTTP status literal message of the `EnumNameToInt` value contract (no built-in
  entry uses it, so its unit test builds that rule directly) and the `exception.Message` pass-throughs (`Elsa3ActivityNormalizer`, `Elsa3DefinitionConverter`), the JavaScript and Liquid safe-lowering parse
  messages (`Elsa3ExpressionRewriter`), the variable starting-value messages and the guidance that builds a
  suggestion from the value's items (`Elsa3VariableMapper`), and the `LiteralDateInterpreted` note, which quotes the
  literal, its interpreted value and the source time-zone setting (`Elsa3LiteralConversion`), the script text quoted through
  `Quote(...)` in `Elsa3ExpressionRewriter` (the `toJson` arity refusal and rewrite warning, the zone-dependent date
  messages and their `{ timeZone: ... }` guidance, and the locale refusal), every other note that names the source
  time-zone setting (the date-interpretation notes in `Elsa3ValueFlowLowerer` and the starting-values date note in
  `Elsa3ActivityNormalizer`), and the raw literal text in the Fork branches message (`Elsa3FlowchartGraphBuilder`) and
  the unreadable outcomes message (`Elsa3TerminalPlacementRewrite`). Give each changed site a unit test whose input carries
  `SENTINEL-1237` and assert the sentinel is absent from message and guidance. Where the input must be valid to reach
  the site (a date literal must parse, a time zone must be a known IANA id), use a unique valid value instead, such as
  `2031-07-13T04:05:06` in `Pacific/Chatham`, and assert that the literal, the interpreted value and the zone id are
  all absent; the fixture sentinel test (T029) only
  covers the paths the fixture triggers. For codes the
  converter does not own, the report builder uses the code table's fixed text (T004), never the Elsa 4 message. Unit-test
  that path directly: a publish-stage diagnostic with an Elsa 4 code and a sentinel in its message reaches the report
  with the fixed text only. Change each
  message so that it names the cause without quoting input, and add no workaround (FR-026).
- [ ] T029 [US5] Add the sentinel test: the fixture run's `report.json` and `report.html` contain no `SENTINEL-1237`,
  including from the conversion-stage `VF-IMP-012` refusal of the .NET-member call (SC-005).
- [ ] T030 [US5] Add the schema test: every report the tests produce validates against
  [contracts/report.schema.json](contracts/report.schema.json). Copy the schema into `Tooling/` as an embedded resource,
  and add a test that keeps it identical to the contract file (FR-020).
- [ ] T031 [US5] Add `Tooling/ReportHtml.cs` and the embedded `report.html` template. The page inlines `report.json`, serialized from the JSON baseline (ADR 0091), and `ReportHtml` itself rewrites
  `<`, `>` and `&` in it as JSON `\u` escapes, never as HTML entities, independent of the serializer's encoder,
  renders every section grouped by bucket and category, and works offline (FR-021). Test that the page's parsed JSON equals
  `report.json`, that the page contains no external URL, and that a `</script>` inside a name cannot break it.

## Phase 8: Polish and acceptance

- [ ] T032 Start the actual migration host (`dotnet run --project apps/Elsa.Migration.Host`) with its two folder
  settings pointing at the fixture run's `migrated/`, and record that every exported version loads (SC-004).
- [ ] T033 Run `inventory --all-versions` over the specification 1235 acceptance set (34 definitions) from the clone
  with `dotnet run`. Check that every definition has one bucket, and that the published versions equal what startup
  reconciliation publishes from the same input on the same composition (SC-002). Record the result in the PR evidence, and check the two-minute goal.
- [ ] T034 Add the smoke test (SC-003). From a clean build, run
  `dotnet run --project extensions/Elsa3/src/Migration/Cli -- workflows inventory` with the fixture export and a
  relative `--out`. Expect exit 1, the printed path and the same `report.json` as the in-process run, after
  `tool.version` is normalized. Nothing may remain outside the output folder, and no `data/` folder appears under
  `apps/Elsa.Migration.Host`.
- [ ] T035 [P] Write `Tooling/README.md`, covering the commands, how to add a feature project to the migration host for
  `assess`, the database rule and redaction. Link it from `extensions/Elsa3/src/Mapping/README.md` and from the
  migration host's README.
- [ ] T036 Set this specification's status to Implemented in the finishing PR.
