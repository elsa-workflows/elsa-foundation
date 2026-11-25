# Feature Specification: Elsa 3 workflows migration CLI

**Feature Branch**: `agent/claude/3052-spec-migration-cli`

**Status**: Draft — redesigned to run from source after specification 1236's rescope

**Input**: A test migration an engineer migrating from Elsa 3 runs on their own machine, as often as they like. It
converts their Elsa 3 workflow export, runs the real Elsa 4 import and publish against a disposable in-memory database,
and writes a complete report plus a folder of migrated Elsa 4 content. It runs from a clone of this repository, next
to the Elsa 3 migration host of specification 1236, never publishes anywhere, and nothing it builds outlives the run.

## Rule and boundary

The CLI is a console project, `Elsa3.Migration.Cli`, at `extensions/Elsa3/src/Migration/Cli/`, with two commands,
`workflows inventory` and `workflows assess`. The engineer runs it from their clone with
`dotnet run --project extensions/Elsa3/src/Migration/Cli -- workflows inventory <export>`, and can debug it from the
IDE like the migration host.

The audience is an engineer. They clone this repository, build from source, and want a host and project references
to debug, and their own activities to port and test (Joey, 2026-11-25). So the CLI takes its Elsa 4 closure from
source: it references the **Elsa 3 migration host** (`apps/Elsa.Migration.Host`, specification 1236) and the Elsa 3
projects it adds. The engineer adds a feature project of their own once, to the host, and both the host and the CLI
compose it. `inventory` runs on the host's committed composition plus the Elsa 3 additions. `assess` adds the
engineer's local configuration sources, so it runs exactly what the engineer's host runs, plus the Elsa 3 additions.
There is no container and no package loading.

Both commands run the **real** conversion, import and `PublishWorkflow` pipeline. There is no dry-run seam, no mock store
and no running API. Everything runs against an in-memory SQLite database that exists only for the run. The only files
the run leaves behind are in the output folder: `report.json`, `report.html` and `migrated/`.

The report is the product. It is complete without an AI reading it. It is redacted, so it can be handed to an AI model
provider: codes, JSON paths, type names, function names and definition identities, never a literal value or a script
body from the user's definitions or settings.

**Example.** An export holds 40 Elsa 3 definitions. Three use `Acme.Activities.SendInvoice`, a custom activity, and
one calls `formatIban()`, a JavaScript global the user registered in Elsa 3. The engineer runs `inventory` from their
clone over the export. It exits 1 and reports:

- The custom-component inventory lists `Acme.Activities.SendInvoice` (activity, 3 definitions, 5 nodes) and
  `formatIban` (JavaScript function, 1 definition).
- Those four definitions are **Must fix**, with the code, the JSON path and the guidance per finding.
- 30 definitions **go smoothly**, and 6 carry a **Reconsider** warning.
- `migrated/` holds the 36 that converted and published.

The engineer then writes an Elsa 4 feature project that provides both components, adds it as a project reference to
the migration host, lists the feature in the host's `shells.Local.json`, and runs `assess`. The four definitions
convert, and the report shows what still remains.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Inventory an export (Priority: P1)

As an engineer migrating from Elsa 3, I run the CLI from my clone over my workflow export and learn, without writing
anything for Elsa 4, which definitions convert, which custom components they use, and what I must fix, reconsider or
take into account.

**Why this priority**: This is the first step of the migration journey. Everything else builds on its report.

**Independent Test**: Run `inventory` over a fixture export that has:

- clean definitions;
- definitions using unmapped custom activities;
- an undeclared JavaScript function, called in one definition and only passed as a value in another;
- a matchable refused Elsa 3 built-in (`getConfig()`) and an unmatchable call;
- a definition with no published version;
- an unregistered variable type, a custom storage driver and the Elsa 3 memory driver;
- a reusable workflow;
- a definition whose reusable dependency is refused.

Check the exit code, the report sections, every finding's code, path, category and remedy, and the contents of
`migrated/`.

**Acceptance Scenarios**:

1. **Given** an export folder (searched recursively) or a zip, **When** `inventory <export>` runs, **Then** it converts,
   imports each selected definition and publishes the newest selected version of each, on the migration host's
   committed composition plus the Elsa 3 addition list. It writes `report.json`, `report.html` and `migrated/` to
   `--out` (default `elsa3-migration-out` in the current directory), prints that path, and exits 0 when no finding is
   Must fix or Reconsider and 1 otherwise.
2. **Given** definitions that use custom components, **When** the report is written, **Then** its custom-component
   inventory lists each component once, with its kind (activity, JavaScript function, variable type, storage driver),
   its Elsa 3 name, the definitions that use it and its usage count. A component no definition uses does not appear.
3. **Given** a definition refused only because a reusable definition it uses was refused, **When** the report is
   written, **Then** that definition is marked *blocked by dependency* and names the root refused definitions, so the
   root cause is fixed first.
4. **Given** the same export, the same commit and the same composition, **When** `inventory` runs twice, **Then**
   `report.json` and every file in `migrated/` are byte-identical between the runs.

### User Story 2 - Assess with my own Elsa 4 features (Priority: P1)

As an engineer who has ported custom components into an Elsa 4 feature project, I add that project to the migration
host, list its feature in the host's `shells.Local.json` (or user secrets), and rerun the test migration. `assess` picks
the feature up and I see the findings it resolves.

**Why this priority**: The fix-and-rerun loop is how a migration converges. Without it, the inventory is a list with
no way to prove the fixes.

**Independent Test**: Build a fixture feature project that registers a custom activity under its Elsa 3 type name, a
prefixed JavaScript host function and a variable type alias. Run `assess` over a test-only host composition, set
through the tooling's internal test seam, that references the fixture project and lists its feature. Check that the
findings it resolves are gone and that the settings-and-composition section names the feature and the setting keys.

**Acceptance Scenarios**:

1. **Given** a feature project referenced by the migration host and its feature listed in `shells.Local.json`, user
   secrets or an environment variable, **When** `assess` runs, **Then** the feature is composed on top of the host's
   composition, in the host's configuration order, and the run otherwise behaves as `inventory`.
2. **Given** a listed feature that no project in the build provides, or a feature or setting that throws while the
   shell is composed, **When** `assess` runs, **Then** it refuses to run (exit 2). It names the feature or the setting
   key, never a value, and never falls back to a partial run.

### User Story 3 - Same composition as the migration host (Priority: P1)

As the engineer, I want the test migration to run on the composition my migration host runs, plus the Elsa 3
additions, so a definition that converts in the CLI loads in the host, and the host still carries no Elsa 3 code.

**Why this priority**: A test migration on another composition proves nothing about the host the engineer runs.

**Independent Test**: Compose the CLI's shell for `inventory` and for `assess`, and compare the enabled features with
the host's composition. Inspect the host's dependency closure.

**Acceptance Scenarios**:

1. **Given** the migration host's committed `shells.json`, **When** `inventory` composes, **Then** its features are the
   host's committed composition plus the Elsa 3 addition list: the converter (`Elsa3Mapping`), the collection import
   for reusables, the import's EF ledger, the publishing bridge and the tooling's own run feature, with their
   dependencies. Local sources
   (`shells.Local.json`, user secrets, environment variables) are not read.
2. **Given** a local source that lists an extra feature, **When** `assess` composes, **Then** the extra feature is
   composed too, and the configuration is layered in the host's order.
3. **Given** a feature that is listed but not found in the CLI's build, **When** either command composes, **Then** it
   refuses with exit 2 and names the feature.
4. **Given** the migration host, **When** its closure is inspected, **Then** it carries no Elsa 3 assembly: the CLI
   references the host, never the other way round.

### User Story 4 - Migrated content I can load (Priority: P1)

As an engineer migrating from Elsa 3, I point the migration host of specification 1236 at the `migrated/` folder of a
clean run, where JSON reconciliation loads it.

**Why this priority**: Going live means starting the engine with this folder. The folder must be what Elsa 4 itself
holds after the import, not a second rendering of the conversion.

**Independent Test**: Run `inventory` over a fixture export. Compose the migration host with its
`JsonWorkflowReconciliation` and `JsonActivityReconciliation` settings pointed at `migrated/`, and compare the loaded
definitions and versions with what the run published.

**Acceptance Scenarios**:

1. **Given** a completed run, **When** `migrated/` is written, **Then** it holds what the run's in-memory Elsa 4 store
   holds after publish, exported from that store. Workflows are in the JSON workflow reconciliation format under
   `migrated/workflows/`. Reusable activities (Elsa 3 definitions used as activities) are in the JSON activity
   reconciliation format under `migrated/activities/`, dependencies before dependents.
2. **Given** a definition that was refused, **When** `migrated/` is written, **Then** neither it nor any definition
   blocked by it appears there. They appear only in the report.
3. **Given** `migrated/` from a previous run, **When** the tool runs again, **Then** `migrated/`, `report.json` and
   `report.html` are replaced as a whole, and nothing else in `--out` is touched.

### User Story 5 - An AI-readable, redacted report (Priority: P2)

As the migration skill (and as the user reading it), I read one versioned `report.json` that carries every code,
category, remedy, JSON path and guidance. It needs no hard-coded diagnostic knowledge, and it contains nothing I would
not send to a model provider.

**Why this priority**: The skill consumes this contract. Its redaction is the privacy promise the skill makes before
reading anything.

**Independent Test**: Run over a fixture export in which every literal value, script body, variable default and
setting value carries a sentinel string. Search `report.json` and `report.html` for the sentinel. Validate
`report.json` against the published schema.

**Acceptance Scenarios**:

1. **Given** any run, **When** `report.json` is written, **Then** it validates against the report schema of its
   `schemaVersion`. No sentinel string from the definitions or the settings appears in it or in `report.html`.
2. **Given** `report.json`, **When** `report.html` is rendered, **Then** it shows the same data and nothing more, and
   opens offline.

### Edge Cases

- The export path does not exist, the zip cannot be opened, or no file holds an Elsa 3 definition: refused to run,
  exit 2.
- One file among many is not valid JSON, or not an Elsa 3 definition: a Must fix finding on that file, and the run
  continues.
- Two inputs carry the same version id, or the same definition id and version: both are refused with the existing
  duplicate-input code, as startup reconciliation refuses them.
- A definition has several versions: the latest published version converts, plus any version that a reusable
  reference pins. Other versions are listed as *not converted* unless `--all-versions` is given. A definition with
  no published version converts its latest version, and an Info finding says so.
- Publish fails on a definition: publish stops at its first error, and the finding says that later publication errors
  may be hidden until it is fixed.
- A dispatched-workflow or execute-workflow target that is not in the export is listed as Take into account. It is not
  a refusal.
- C# and Python expressions are refused with the existing value-flow code and its guidance.
- A script refers to an Elsa 3 global that a registered prefixed host function carries as its member name without calling it (assigns it, passes it as a callback). The
  converter rewrites only direct calls, so it reports such a reference with a diagnostic of its own (see
  *Dependencies and boundaries*). A matchable undeclared global that is only used as a value, with no host function
  registered yet, is reported with the same `javascript-function` component code as a matchable call, so the
  inventory lists it. The CLI scans no scripts itself.
- A converted reusable activity always depends on other activity versions, because its body is made of catalog
  activities. The engine's JSON activity reconciliation publishes such source-owned composites with their dependencies
  (specification 1239), so `migrated/activities/activities.json` loads in any order. The engine gives each loaded
  activity version the catalog id of its type key and version, not the id the import stored, so the export rewrites
  every reusable version id, and every reference to one in a reusable or a workflow, to that catalog id. Each exported
  reusable keeps its contract and outcomes, including any outcome it declared in Elsa 3.

## Requirements *(mandatory)*

### Functional Requirements

**Commands and process**

- **FR-001**: The console project `Elsa3.Migration.Cli` at `extensions/Elsa3/src/Migration/Cli/` MUST offer
  `workflows inventory <export> [--out <dir>] [--all-versions]` and
  `workflows assess <export> [--out <dir>] [--all-versions]`, run from the clone with
  `dotnet run --project extensions/Elsa3/src/Migration/Cli -- <command>`.
- **FR-002**: The CLI MUST be a thin console entry over the tooling library `Elsa3.Migration.Tooling`
  (`extensions/Elsa3/src/Migration/Tooling/`), which holds the pipeline. It runs the pipeline in its own process and
  is debuggable from the IDE. It needs no separate process and no other command-line tool.
- **FR-003**: The CLI MUST make no network call. Everything it runs on is in its build or in a folder it is given.
- **FR-004**: Exit codes MUST be:
  - 0: no Must fix and no Reconsider finding;
  - 1: findings;
  - 2: refused to run, because of bad arguments, unusable input, or a composition that cannot be built (a listed
    feature that is not found, or a feature that throws while the shell is composed).
- **FR-005**: On exit 0 or 1 the tool MUST print the output path as it was given (the default if none was given). On
  exit 2 it prints the refusal reason, writes no report, and leaves the output folder untouched.

**Code closure**

- **FR-006**: The CLI project MUST reference the migration host project, `apps/Elsa.Migration.Host` (specification
  1236), so every feature the host composes, and every feature project the engineer has added to the host, is in the
  CLI's build. It MUST also reference the Elsa 3 projects of the addition list: the converter (`Elsa3Mapping`), the
  collection import for reusables, the import's EF ledger, the publishing bridge and the tooling, whose own shell
  feature, `Elsa3MigrationImportRun`, registers the shared import run inside the shell. The engineer adds a feature
  project once, to the host. The reference goes from the CLI to the host only, so the host still carries no Elsa 3
  assembly (specification 1236 FR-003). The CLI therefore carries the ASP.NET Core shared framework, because the
  host's features need it, but it starts no web host.
- **FR-007**: The Elsa 3 addition list MUST be a data file maintained under `extensions/Elsa3/`,
  `elsa3-migration.features.json` next to the CLI project, read after the host composition. Neither the user nor the
  skill configures it. The report records the tool's own version.

**Composition and persistence**

- **FR-008**: The CLI MUST compose through the migration host's own wiring, not a copy of it. It calls the host's two
  public methods (see *Dependencies and boundaries*): one adds the host's configuration layers, the other the host's
  root services, including CShells with `WithHostAssemblies()`, so features are discovered over the CLI's own build as
  the host discovers them. It calls both on an empty generic host (no default configuration sources), with the host
  folder as content root and the Development environment, that starts no web server, and composes one shell:
  - `inventory` composes the host's committed composition only: `appsettings*.json`, `shells.json` and
    `shells.Development.json` from the host project folder, plus the Elsa 3 addition list. It ignores the engineer's
    local sources;
  - `assess` composes with the host's full configuration stack, in the host's order (lowest first):
    `appsettings*.json`, `shells.json`, `shells.Development.json`, `shells.Local.json`, the host's user secrets (from
    its `UserSecretsId`), environment variables, plus the Elsa 3 addition list. The CLI passes the host no
    command-line arguments. It therefore runs exactly what the
    engineer's host runs, plus the Elsa 3 additions.

  The CLI runs in the Development environment, the only one specification 1236 supports, so the host's committed
  development keys apply. The host project folder and the addition list's folder (the CLI project folder) are fixed
  when the CLI is built; there is no option to point elsewhere, and tests use an internal seam. The order is: the
  host's configuration layers, then the addition list, then the CLI's own run-override layer (the in-memory database
  of FR-009, the reconciliation inputs below, and the Tasks setting that runs startup tasks only), and only then the
  host's root services, which read configuration when they are registered. After them, the CLI replaces only Data
  Protection, with an ephemeral key ring. The shell's initializers run; with the startup-tasks-only setting, the
  Tasks feature's initializer runs the startup tasks alone, so the EF schema and the activity catalog exist. No web
  host, HTTP endpoint, background hosted service, background task or recurring task starts, so there is no trigger
  runtime, scheduler or workflow execution. The Elsa 3 folder import is not composed.
  The host's JSON workflow and activity reconciliation stay composed, but the run points them at an empty workflows
  folder and an activities file holding `[]`, in a run-scoped temporary folder outside `--out`. Configuration layers
  cannot remove keys, so the run reads the host's configuration through a filter that drops their other path keys
  (`Files` and `FilePath` for workflows, `Files` for activities) and the workflow feature's `AdditionalSources`. So
  neither the host's sample nor a `migrated/` folder the engineer configured loads into the run, and no local source
  can make the run refuse through them. `--out` and its staging folder are created only after the shell has been
  composed. The run creates nothing under the host project folder.
- **FR-009**: Every EF module MUST use the same in-memory SQLite database, named uniquely per run. The run holds that
  database open for its whole duration and disposes it at the end, so every module sees the same data and the
  shared-transaction single-target rule holds. The host's persistence connection setting MUST be replaced for the run,
  and the report says that it was.
- **FR-010**: Nothing the run writes may outlive it, except the output folder. There is no install root and no package
  extraction: the run writes only the in-memory database, which it disposes, the temporary folder that holds the empty
  reconciliation inputs, and the output folder's staging folder, both of which it removes, including after a failure.

**Selection and pipeline**

- **FR-011**: The input MUST be a folder (searched recursively for `*.json`) or a zip. Each file holds one Elsa 3
  definition or an array of them.
- **FR-012**: For each definition, the latest published version MUST convert, plus every version that a reusable
  reference pins. All other versions are listed as not converted. `--all-versions` converts every version.
- **FR-013**: The run MUST perform the same sequence as Elsa 3 startup reconciliation, sharing that implementation
  rather than duplicating it:
  1. input selection;
  2. collection analysis;
  3. apply, excluding refused items and their dependents;
  4. `PublishWorkflow` per imported workflow.

  It collects every diagnostic and skip into the report instead of the log. As in startup reconciliation, the import
  publishes the activity version of every selected reusable version, older pinned ones included, and all of them are
  exported to `activities.json`. For workflows, wrappers included, only the newest selected version of each
  definition is published and exported. An older selected workflow version, or the wrapper of an older reusable
  version, is imported for its findings only and is marked with the Info code `ELS3-MIGRATION-SUPERSEDED-VERSION`.

  A refusal that the shared sequence cannot pin on one definition (a plan error that names no item, a refusal that
  covers the whole selection at apply) is not thrown. It becomes a run-level Must fix finding with its code; the text
  is the code table's fixed text for that code, or for a converter-owned code its redacted message. Every selected
  definition that the refusal kept from being applied gets an Error finding `ELS3-MIGRATION-RUN-REFUSED` (remedy
  `patch-definition`), so none of them buckets as `convert-as-is` or goes smoothly. The run exits 1 with a report.
- **FR-014**: Publish MUST stop at the first error per definition. The finding states that the publish stage was
  reached and that later errors may be hidden.

**Report**

- **FR-015**: The report MUST map each finding to one category:
  - Error → **Must fix**;
  - `ELS3-BEHAVIOUR-*` warning → **Reconsider**;
  - any other warning, or Info → **Take into account**.

  A definition **goes smoothly** when it has no finding other than routine ones. A code is routine when the code table
  marks it so: an Info the converter adds to every definition, such as the null-policy record.
- **FR-016a**: A component finding MUST name its component from structured diagnostic data, never by parsing a
  message. Every component diagnostic carries a `ComponentName` metadata entry (the Elsa 3 activity type, function name,
  variable type name or the trimmed Elsa 3 storage driver type). The converter attaches it to its component
  diagnostics. This specification relies on the converter's storage-driver change (see *Dependencies and boundaries*),
  after which the converter does not refuse Elsa 3 storage drivers: it converts the value to the default `elsa.json`
  storage and warns. A custom driver is reported as
  `ELS3-BEHAVIOUR-CUSTOM-STORAGE-DRIVER-REPLACED`, a `storage-driver` component whose `ComponentName` is the trimmed Elsa
  3 driver type. It is a Reconsider finding, not a blocker. The Elsa 3 memory driver is reported as
  `ELS3-BEHAVIOUR-MEMORY-STORAGE-PERSISTED`, also Reconsider and also a `storage-driver` component, whose
  `ComponentName` is the trimmed Elsa 3 memory driver type: the user chose it in Elsa 3, and the migration has to
  decide what replaces it. An Elsa 4 publication error that carries
  no structured metadata is never used to name a component. A JavaScript call, or a read of an undeclared global used as a
  value, that the converter reports as matchable (a registered prefixed host function may take the name over: an undeclared custom global, or a refused
  Elsa 3 built-in such as `getConfig`) gets a `javascript-function` component code of its own, with `ComponentName`.
  That includes a `get{Name}()` or `set{Name}()` call that matches no variable or input, which can be an Elsa 3 custom
  global such as `getCustomerData()`. A `set{Name}()` call on a workflow input is one too: Elsa 3 generated no setter
  for inputs, so a call that ran in Elsa 3 was a custom global.
  Calls the converter reports as unmatchable, and forbidden capabilities, keep the forbidden-capability code and are not
  components.
- **FR-016**: Every Must fix finding MUST carry one of three remedies:
  - `patch-definition`: change the Elsa 3 definition;
  - `build-component`: supply the missing Elsa 4 component;
  - `none`: there is no Elsa 4 equivalent.

  The remedy, the component kind for component codes and the routine mark come from one code table maintained with
  the converter's diagnostic codes. A test fails when an Elsa 3 code the converter, the import or reconciliation can
  emit has no entry. A code from Elsa 4 that the table does not list gets `patch-definition`. A skip (duplicate input,
  blocked by dependency, a failed publish, an import refusal) is reported at Error severity.
- **FR-017**: Every definition MUST carry exactly one bucket. The bucket names come from the specification 1235
  acceptance, but the rule here is this specification's own: it is automatic, and no coordinator approval is involved.
  - `incompatible`: any Must fix finding has remedy `none`;
  - `convert-as-is`: no Must fix and no Reconsider finding;
  - `convert-with-adjustments`: everything else.

  A definition refused only because a dependency was refused is also marked *blocked by dependency*, which names the
  root refused definitions: those refused for a finding of their own, through any chain of dependencies.
- **FR-018**: The report MUST have these sections:
  1. Summary counts per category and bucket.
  2. The custom-component inventory, with usage counts.
  3. Definitions by bucket, each with its findings' code, severity, category, remedy, JSON path, message and guidance.
  4. The settings and composition the run used: the tool version, the enabled features, the setting keys under the
     host's shell section (`CShells:Shells:default`) and `ConnectionStrings` (never their values, and never machine
     environment variables outside those sections) and the persistence override. Whether local sources were applied
     follows from `command`.
  5. Dispatched-workflow targets missing from the export, as Take into account.

  It also lists the versions not converted.
- **FR-019**: The report MUST NOT contain a literal value, an expression or script body, a variable default or a
  setting value from the input. Diagnostic messages and guidance the converter emits for the report MUST meet this
  rule. A converter message that embeds such text is changed at the converter. For a code the converter does not own
  (an Elsa 4 publication or compilation code), the report carries the code, its path and a fixed text from the code
  table, never the Elsa 4 message, which may quote script source. Identities are allowed: definition ids and names,
  including the id of a referenced definition such as a dispatch target, type names, function names and setting keys.
  Redaction is the only mode.
- **FR-020**: `report.json` MUST carry an integer `schemaVersion`, starting at 1, and conform to the schema this
  specification publishes. Removing a field, or changing its meaning, increments the version. Adding a field does not.
- **FR-021**: `report.html` MUST be a single self-contained file that renders exactly the data in `report.json`, with
  no network access and no data of its own.
- **FR-022**: For identical input, commit and configuration, `report.json` and `migrated/` MUST be byte-identical.
  They carry no timestamps, absolute paths or run identifiers.

**Migrated content**

- **FR-023**: `migrated/` MUST be exported from the run's Elsa 4 store after publish:
  - workflows in the JSON workflow reconciliation format, one file per definition under `migrated/workflows/`;
  - reusable activities in the JSON activity reconciliation format, in `migrated/activities/activities.json`,
    dependencies first;
  - the wrapper workflow that the import publishes for each reusable, under the reusable's definition id, as a workflow
    file like any other. The reusable's result names both files.

  Both always exist: an empty `workflows/` folder, and an `activities.json` holding `[]`, when there is nothing to
  export, because the migration host's readers need both.

  It carries no publication state. Whether the loading engine publishes is up to that engine's configuration.
- **FR-024**: Refused and blocked definitions MUST NOT appear in `migrated/`.

**Boundaries**

- **FR-025**: The group MUST NOT publish to, call, or write into any Elsa server, database or store outside the run.
- **FR-026**: Unconvertible Elsa 3 behaviour MUST be refused with a diagnostic and guidance. The CLI adds no converter
  workaround and scans no scripts. A gap it finds becomes a converter change with its own diagnostic, or an Elsa 4
  specification.

### Key Entities

- **Run**: the tool version, the composed features, the setting keys, the input and the selected versions. Its results
  are the report and `migrated/`.
- **Finding**: code, severity, category, remedy, component kind (for component codes), JSON path, definition identity,
  message, guidance.
- **Definition result**: definition id, name, version, reusable flag, bucket, blocked-by, findings, published flag,
  exported path.
- **Component**: kind, Elsa 3 name, using definitions, usage count.
- **Code table**: code → remedy and component kind.
- **Elsa 3 addition list**: the features the CLI adds to the migration host's composition.

## Spec decisions

| ID | Question | Decision |
|---|---|---|
| Q1 | Where does the migrated content come from? | It is exported from the run's own Elsa 4 store after publish, not written from the converter's output. The folder is `migrated/`. Workflows use the workflow format, and reusables use the activity format. |
| Q2 | Converted reusables depend on other activity versions. Can the engine load them from `migrated/`? | Yes: specification 1239 lets the engine's JSON activity reconciliation publish source-owned composites with their dependencies. The engine assigns each loaded version its catalog id (type key and version), so the export rewrites reusable version ids and every reference to them to that id, and keeps each reusable's contract and outcomes. Nothing in the report needs to flag reusables. |
| Q3 | Where does the CLI's Elsa 4 closure come from? | From source: the CLI project references the migration host project and the Elsa 3 projects of the addition list (Joey, 2026-11-25, after specification 1236's rescope). The audience is an engineer who wants a host and references to debug, and their own activities to port and test. Rejected: an image built `FROM` an essentials engine image, which 1236 no longer plans; and the engineer adding their feature references to both the host and the CLI, two places to keep in sync. |
| Q4 | Does the CLI need an ADR 0076 change? | No. It runs no `dotnet-elsa` worker and reaches no frozen entry point, so ADR 0076 does not apply. The amendment PR #5680 is withdrawn. |
| Q5 | How is the run kept to one database when the shared transaction refuses a supplied connection? | Every module gets the same connection string, for a uniquely named in-memory shared-cache database that is kept alive for the run. |
| Q6 | How far is the report versioned? | One integer `schemaVersion`. The skill and the CLI can come from different commits, so the skill must be able to refuse a version it does not know. Nothing more. |
| Q7 | Is there an unredacted mode? | No. The user already holds the raw definitions, and the skill asks before reading them. |
| Q8 | Does a Must fix finding mean patching the definition or building a component? | Each code's remedy comes from one code table, kept with the diagnostic codes and enforced by a completeness test. |
| Q9 | How do the engineer's own features reach the run? | As project references the engineer adds to the migration host, which the CLI references. No packages are loaded. |
| Q10 | What about an engineer with their own Elsa 4 host? | No `--host` option: the migration host is the host. The engineer adds their features to it. |
| Q11 | Who finds an Elsa 3 global that a registered prefixed host function carries as its member name that is referenced but not called? | The converter, with a diagnostic of its own that names the global at the reference's JSON path. The CLI only reports codes, so the finding reaches the report as a Must fix `patch-definition` finding with no component kind: a host function already provides the name, so nothing is left to build. |
| Q13 | What do the CLI's own findings (unreadable input file, no published version) use as codes? | Codes of the tooling's own, `ELS3-MIGRATION-*`, in the same code table. An existing code such as `ELS3-MIGRATION-UNSUPPORTED-INPUT` is reused where it fits. |

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001 - Complete inventory**: On the fixture export, every custom component a definition uses appears in the
  inventory with the right kind and count. No unused component appears. Every finding has a category, and every Must
  fix finding has a remedy.
- **SC-002 - Corpus run**: Over the 34 definitions in the specification 1235 acceptance set, `inventory --all-versions`
  from the clone completes. Every definition gets exactly one bucket. Every definition version that startup
  reconciliation publishes from the same input, on the same composition, is also published by the run and present in
  `migrated/`.
- **SC-003 - Nothing persists**: After a run, including a run that fails part-way, nothing remains outside the output
  folder: no database file and no temporary folder survives.
- **SC-004 - Loadable output**: The migration host, with JSON workflow and activity reconciliation over a run's
  `migrated/`, loads every exported reusable and workflow, including a workflow that pins an older reusable version,
  and also when the run exported no reusable or no workflow.
- **SC-005 - Redaction**: The sentinel test finds no sentinel in `report.json` or `report.html`.
- **SC-006 - Determinism**: Two runs over the same input produce byte-identical `report.json` and `migrated/`.
- **SC-007 - Assess resolves**: With the fixture feature project, `assess` clears every finding that names one of the
  project's components, except that a value-only read of a function the project now provides becomes
  `ELS3-REPLACED-FUNCTION-REFERENCE` (Must fix, `patch-definition`), because only direct calls are rewritten. It adds no
  other finding. The fixture is built so that the other definitions it unblocks publish clean.

## Dependencies and boundaries

- **Elsa 3 migration host** (specification 1236): its project, `shells.json` and configuration order; the CLI
  references it. The host composes JSON workflow and JSON activity reconciliation, so `migrated/` loads there.
- **Two public wiring methods on the migration host.** 1237 adds them to the host (task T004a), as an extraction from
  `CreateBuilder` that leaves the host's behaviour unchanged. The host's `Program.cs` and the CLI both call them:
  - `MigrationHost.AddHostConfiguration`: adds the host's configuration layers in 1236 R-3's order for a given host
    folder and environment, taking the host assembly for `AddUserSecrets`, with a switch that leaves out the engineer's
    local sources (`shells.Local.json`, user secrets, environment variables, command line);
  - `MigrationHost.AddHostServices`: the host's root service registration (1236 R-2), including the CShells
    `WithHostAssemblies()` setup, without the web pipeline. It creates no folders: the host creates `data/` at
    startup, outside this method.

  It needs the migration host of specification 1236 implemented on the initiative branch.
- **The migration host is temporary** (1236 Q12). #5800's app profiles replace it, and its cleanup removes
  `apps/Elsa.Migration.Host`. The CLI's closure and configuration follow the host, so that cleanup must move the CLI's
  reference and the two wiring calls to whatever replaces the host. The CLI is temporary in the same way.
- **Converter diagnostics this specification relies on.** The converter maps a direct call of an Elsa 3 global to the
  registered prefixed host function that carries the same member name (direct calls only), and diagnoses unregistered
  variable types. Its storage-driver change converts Elsa 3 storage drivers to the default storage with a Reconsider
  warning: `ELS3-BEHAVIOUR-CUSTOM-STORAGE-DRIVER-REPLACED` and `ELS3-BEHAVIOUR-MEMORY-STORAGE-PERSISTED`, each carrying
  the driver type as `StorageDriverType` and `ComponentName`. The member-name matching and the storage-driver change
  are converter changes outside this specification, delivered by PRs #5703 and #5709.
  These supply
  the codes from which the custom-component inventory is built, and this specification adds their entries to the code
  table.
- **Converter diagnostic for non-call references.** A global that a host function carries, referenced without a call, is
  not rewritten, and it fails at run time. The converter refuses it as `ELS3-REPLACED-FUNCTION-REFERENCE` (Q11), and
  this specification adds its entry to the code table.
- **Component metadata in the converter** (FR-016a): `ComponentName` metadata on component diagnostics, and a
  component code of its own for every call or value reference the converter reports as matchable (an undeclared custom
  global or a refused built-in a host function may take over). This specification makes that converter change (T004b).
- **Source-owned composites** (specification 1239): the engine's JSON activity reconciliation publishes composites with
  their dependencies, which `migrated/activities/` relies on (Q2).
- **Migration skill** (specification 1238): it starts the CLI with `dotnet run` from the clone and consumes
  `report.json` through the schema this specification publishes.
- [Elsa constitution](../../.specify/memory/constitution.md) E2.7 keeps Elsa 3 behind the import boundary. The CLI
  project, the tooling, the converter and the addition list live under `extensions/Elsa3/`, and the migration host
  carries no Elsa 3 code.
- Out of scope: live instances; running Elsa 3 next to Elsa 4; persistence data; identity and auth; Elsa 3 Studio
  extensions; any publishing or deployment step.

## Assumptions

- Elsa 3 exports mark which version is published and which is latest, as Studio exports do.
- Engineers build this repository from source, as specification 1236's audience does. They have the .NET SDK the
  repository pins.
