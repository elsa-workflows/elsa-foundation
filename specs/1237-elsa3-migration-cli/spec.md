# Feature Specification: Elsa 3 workflows migration CLI

**Feature Branch**: `agent/claude/3052-spec-migration-cli`

**Status**: Draft — provisional on the ADR 0076 amendment named under *Dependencies and boundaries*

**Input**: A test migration an Elsa 3 user runs on their own machine, as often as they like. It converts their Elsa 3
workflow export, runs the real Elsa 4 import and publish against a disposable in-memory database, and writes a complete
report plus a folder of migrated Elsa 4 content. It runs inside the Elsa 3 migration image, never publishes anywhere,
and nothing it builds outlives the run.

## Rule and boundary

The command group is `dotnet elsa elsa3-migration workflows`, with two commands, `inventory` and `assess`. It runs
inside the **Elsa 3 migration image**, which this specification also defines. That image is built `FROM` the essentials
engine image at the same version and adds the Elsa 3 features. The test migration therefore runs on the composition the
user later goes live on, plus the Elsa 3 additions. The user's export, packages folder, `appsettings.json` and output
folder are mounted into the container. Docker is a prerequisite, and the CLI ships no Elsa 4 closure of its own.

Both commands run the **real** conversion, import and `PublishWorkflow` pipeline. There is no dry-run seam, no mock store
and no running API. Everything runs against an in-memory SQLite database that exists only for the run. The only files
the run leaves behind are in the output folder: `report.json`, `report.html` and `migrated/`.

The report is the product. It is complete without an AI reading it. It is redacted, so it can be handed to an AI model
provider: codes, JSON paths, type names, function names and definition identities, never a literal value or a script
body from the user's definitions or settings.

**Example.** An export holds 40 Elsa 3 definitions. Three use `Acme.Activities.SendInvoice`, a custom activity, and
one calls `formatIban()`, a JavaScript global the user registered in Elsa 3. The user runs `inventory` in the
migration image with the export mounted. It exits 1 and reports:

- The custom-component inventory lists `Acme.Activities.SendInvoice` (activity, 3 definitions, 5 nodes) and
  `formatIban` (JavaScript function, 1 definition).
- Those four definitions are **Must fix**, with the code, the JSON path and the guidance per finding.
- 30 definitions **go smoothly**, and 6 carry a **Reconsider** warning.
- `migrated/` holds the 36 that converted and published.

The user then builds an Elsa 4 feature package that provides both components and runs `assess` with the packages
folder and an `appsettings.json` that enables it. The four definitions convert, and the report shows what still
remains.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Inventory an export (Priority: P1)

As an Elsa 3 user, I run the migration image over my workflow export and learn, without building anything for Elsa 4,
which definitions convert, which custom components they use, and what I must fix, reconsider or take into account.

**Why this priority**: This is the first step of the migration journey. Everything else builds on its report.

**Independent Test**: Run `inventory` in the migration image over a fixture export that has:

- clean definitions;
- definitions using unmapped custom activities;
- an undeclared JavaScript function;
- an unregistered variable type and an unmapped storage driver;
- a reusable workflow;
- a definition whose reusable dependency is refused.

Check the exit code, the report sections, every finding's code, path, category and remedy, and the contents of
`migrated/`.

**Acceptance Scenarios**:

1. **Given** an export folder (searched recursively) or a zip, **When** `inventory <export>` runs, **Then** it converts,
   imports each selected definition and publishes the newest selected version of each, on the image's composition. It writes `report.json`, `report.html`
   and `migrated/` to `--out` (default `/app/out`, the documented output mount), prints that path, and exits 0 when no finding is Must fix
   or Reconsider and 1 otherwise.
2. **Given** definitions that use custom components, **When** the report is written, **Then** its custom-component
   inventory lists each component once, with its kind (activity, JavaScript function, variable type, storage driver),
   its Elsa 3 name, the definitions that use it and its usage count. A component no definition uses does not appear.
3. **Given** a definition refused only because a reusable definition it uses was refused, **When** the report is
   written, **Then** that definition is marked *blocked by dependency* and names the root refused definitions, so the
   root cause is fixed first.
4. **Given** the same export and the same image, **When** `inventory` runs twice, **Then** `report.json` and every file
   in `migrated/` are byte-identical between the runs.

### User Story 2 - Assess with my own Elsa 4 features (Priority: P1)

As an Elsa 3 user who has ported custom components into Elsa 4 feature packages, I rerun the test migration with my
packages and settings loaded exactly as the essentials engine image loads them, and see the findings those components
resolve.

**Why this priority**: The fix-and-rerun loop is how a migration converges. Without it, the inventory is a list with
no way to prove the fixes.

**Independent Test**: Build a fixture feature package that registers a custom activity under its Elsa 3 type name, a
prefixed JavaScript host function and a variable type alias. Run `assess` with that package folder and an
`appsettings.json` that enables the feature. Check that the findings it resolves are gone and that the
settings-and-closure section names the package and the setting keys.

**Acceptance Scenarios**:

1. **Given** `--packages <folder of .nupkg>` and `--settings <appsettings.json>`, **When** `assess` runs, **Then** the
   packages load through Nuplane on top of the image's composition, the settings merge as the essentials engine image
   merges them, and the run otherwise behaves as `inventory`.
2. **Given** a package that needs a dependency that neither the folder nor the image holds, or a package or setting that
   the composition cannot boot, **When** `assess` runs, **Then** it refuses to run (exit 2). It names the package and
   version range, or the feature or setting that failed. It downloads nothing and never falls back to a partial run.

### User Story 3 - The Elsa 3 migration image (Priority: P1)

As the maintainer of the migration journey, I publish one migration image per release, built from the essentials
engine image of the same version, so the test migration and going live use the same engine.

**Why this priority**: The CLI has no closure without it.

**Independent Test**: Build the image from the essentials engine image of the same version. Inspect its composition
and entry point. Run `inventory` in it on the fixture export with networking disabled.

**Acceptance Scenarios**:

1. **Given** the essentials engine image at version V, **When** the migration image is built, **Then** it is
   `FROM` that image at V. Its composition is the essentials feature list plus the Elsa 3 addition list: the converter
   (`Elsa3Mapping`), the collection import for reusables, the import's EF ledger and the publishing bridge, with
   their dependencies.
2. **Given** the migration image, **When** it starts, **Then** its entry point is the `dotnet-elsa` CLI's
   `elsa3-migration` command group. It starts no web host, and a run with networking disabled behaves the same as one
   with networking enabled.
3. **Given** the essentials engine image, **When** it is inspected, **Then** it carries no Elsa 3 assembly.

### User Story 4 - Migrated content I can load (Priority: P1)

As an Elsa 3 user, I take the `migrated/` folder of a clean run and mount it into the essentials engine image, where
JSON reconciliation loads it.

**Why this priority**: Going live means starting the engine with this folder. The folder must be what Elsa 4 itself
holds after the import, not a second rendering of the conversion.

**Independent Test**: Run `inventory` over a fixture export. Start the essentials engine image with JSON workflow and
JSON activity reconciliation pointed at `migrated/`, and compare the loaded definitions and versions with what the run
published.

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
- A script refers to a declared JavaScript global without calling it (assigns it, passes it as a callback). The
  converter rewrites only direct calls, so it reports such a reference with a diagnostic of its own (see
  *Dependencies and boundaries*). The CLI scans no scripts itself.
- A converted reusable activity always depends on other activity versions, because its body is made of catalog
  activities. While the engine's source reconciliation refuses such content, the report marks every reusable, every
  reusable's wrapper workflow and every workflow that uses one with `needsCompositeSourceSupport` (see *Dependencies and
  boundaries*).

## Requirements *(mandatory)*

### Functional Requirements

**Commands and process**

- **FR-001**: `dotnet-elsa` MUST offer the `elsa3-migration workflows` group, with
  `inventory <export> [--out <dir>] [--all-versions]` and
  `assess <export> --packages <dir> --settings <file> [--out <dir>] [--all-versions]`.
- **FR-002**: The group MUST run on the `dotnet-elsa` worker as ADR 0076 describes. The front end carries no Elsa 3
  and no Elsa 4 product reference. The worker runs inside the migration image's host closure and reaches a frozen entry
  point under `extensions/Elsa3/` by type name. It takes versioned JSON on stdin and answers on stdout.
- **FR-003**: The group MUST make no network call. Everything it runs on is in the migration image or in a mounted
  folder.
- **FR-004**: Exit codes MUST be:
  - 0: no Must fix and no Reconsider finding;
  - 1: findings;
  - 2: refused to run, because of bad arguments, unusable input, or a user package or setting that the composition
    cannot boot.

  The persistence group's codes are unchanged.
- **FR-005**: On exit 0 or 1 the tool MUST print the output path as it was given (the default if none was given). On
  exit 2 it prints the refusal reason, writes no report, and leaves the output folder untouched.

**The migration image**

- **FR-006**: The Elsa 3 migration image MUST be built `FROM` the essentials engine image at the same version, with no
  host rebuild. Following the engine image's base contract, it bakes its addition list as
  `/app/composition/elsa3.features.json`, and composes through the engine's composition library, which reads the
  essentials list first and the addition list after it. The packages of the added features are this image's own: they
  are baked into `/app/elsa3/packages/`, and the CLI's worker loads them itself, because the engine host reads every list in
  that folder but loads no package a derived image adds, and the migration image never starts the engine host. The Dockerfile and the addition list are maintained under `extensions/Elsa3/`. The added
  features are the converter (`Elsa3Mapping`), the collection import for reusables, the import's EF ledger and the
  publishing bridge, with their dependencies. Neither the user nor the skill configures them.
- **FR-007**: The image MUST contain the `dotnet-elsa` CLI, and its entry point MUST be the `elsa3-migration` command
  group. It starts no web host. It is published as `elsaworkflows/elsa-elsa3-migration`, next to the essentials images, with the
  same tags as the essentials engine image, and each tag is built `FROM` the engine image with that tag. The
  `dotnet-elsa` inside is the one built from the same commit. A consumer pulls the migration image by the essentials
  tag it goes live on. The report records both the image tag and the tool's own version.

**Composition and persistence**

- **FR-008**: Features MUST be composed as a shell composes them, from the image's composition plus `--settings`,
  merged as the essentials engine image merges `appsettings.json`, through the engine's composition library. The
  shell's initializers and its startup tasks run, so the EF schema and the activity catalog exist. No web host, HTTP
  endpoint, background hosted service, background task or recurring task starts, so there is no trigger runtime,
  scheduler or workflow execution. The Elsa 3 folder import is not
  composed.
- **FR-008a**: When the engine requires a setting before it starts (such as a signing key or the seeded admin
  password) and `--settings` does not set it, the run MUST generate a value for that run only. Generated values never
  reach the report or the output.
- **FR-009**: Every EF module MUST use the same in-memory SQLite database, named uniquely per run. The run holds that
  database open for its whole duration and disposes it at the end, so every module sees the same data and the
  shared-transaction single-target rule holds. A persistence connection string in `--settings` MUST be replaced for
  the run, and the report says that it was.
- **FR-010**: Nothing the run writes may outlive it, except the output folder. Package extraction goes to a run-scoped
  temporary folder that is deleted at the end, including after a failure.

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

  It collects every diagnostic and skip into the report instead of the log. As in startup reconciliation, only the
  newest selected version of each definition is published and exported. An older selected version is converted and
  imported for its findings only, and is marked with the Info code `ELS3-MIGRATION-SUPERSEDED-VERSION`. A refusal that
  the shared sequence cannot pin on one definition (a plan error that names no item, a refusal that covers the whole
  selection at apply) is not thrown: it becomes a run-level Must fix finding, with its code and the code table's text,
  and the run exits 1 with a report.
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
  message. The converter attaches a `ComponentName` metadata entry (the Elsa 3 activity type, function name, variable
  type name or storage driver type) to every component diagnostic. A call of an undeclared JavaScript function gets
  its own code, separate from the forbidden-capability code it otherwise shares, so that refused capabilities (forbidden globals) do not count as
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
  4. The settings and composition the run used: the tool and image versions, the loaded package ids and versions, the
     enabled features, the setting keys (never their values) and the persistence override.
  5. Dispatched-workflow targets missing from the export, as Take into account.

  It also lists the versions not converted, and the `migrated/` items that need the engine's source reconciliation of
  composites with dependencies: every reusable, and every workflow that uses one.
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
- **FR-022**: For identical input, image, packages and settings, `report.json` and `migrated/` MUST be byte-identical.
  They carry no timestamps, absolute paths or run identifiers.

**Migrated content**

- **FR-023**: `migrated/` MUST be exported from the run's Elsa 4 store after publish:
  - workflows in the JSON workflow reconciliation format, one file per definition under `migrated/workflows/`;
  - reusable activities in the JSON activity reconciliation format, in `migrated/activities/activities.json`,
    dependencies first;
  - the wrapper workflow that the import publishes for each reusable, under the reusable's definition id, as a workflow
    file like any other. The reusable's result names both files.

  Both always exist: an empty `workflows/` folder, and an `activities.json` holding `[]`, when there is nothing to
  export, because the engine mounts the folder at `/app/migrated` and its readers need both.

  It carries no publication state. Whether the loading engine publishes is up to that engine's configuration.
- **FR-024**: Refused and blocked definitions MUST NOT appear in `migrated/`.

**Boundaries**

- **FR-025**: The group MUST NOT publish to, call, or write into any Elsa server, database or store outside the run.
- **FR-026**: Unconvertible Elsa 3 behaviour MUST be refused with a diagnostic and guidance. The CLI adds no converter
  workaround and scans no scripts. A gap it finds becomes a converter change with its own diagnostic, or an Elsa 4
  specification.

### Key Entities

- **Run**: the image version, the packages, the setting keys, the input and the selected versions. Its results are the
  report and `migrated/`.
- **Finding**: code, severity, category, remedy, component kind (for component codes), JSON path, definition identity,
  message, guidance.
- **Definition result**: definition id, name, version, reusable flag, bucket, blocked-by, findings, published flag,
  exported path.
- **Component**: kind, Elsa 3 name, using definitions, usage count.
- **Code table**: code → remedy and component kind.
- **Elsa 3 addition list**: the features the migration image adds to the essentials feature list.

## Spec decisions

| ID | Question | Decision |
|---|---|---|
| Q1 | Where does the migrated content come from? | It is exported from the run's own Elsa 4 store after publish, not written from the converter's output. The folder is `migrated/`. Workflows use the workflow format, and reusables use the activity format. |
| Q2 | Converted reusables depend on other activity versions, and the engine's source reconciliation refuses such content. | The engine gap is fixed by specification 1239, outside this one. The CLI exports reusables anyway, and the report flags every reusable and every workflow that uses one. Only that part of SC-004 waits on the fix. |
| Q3 | Where does the CLI's Elsa 4 closure come from? | The Elsa 3 migration image, built `FROM` the essentials engine at the same version, with the Elsa 3 additions baked in. The CLI ships no closure, and Docker is a prerequisite. |
| Q4 | Does using the ADR 0076 worker for this group need an ADR change? | Yes. A small ADR 0076 amendment does two things. It lets the worker reach further named frozen entry points, one per command group (D1). It also lets such a group, by default, reconcile local directory feeds into a private, run-scoped install root that it deletes at the end, never a host's shared store and never the network (D1, D10). This specification is provisional on it. |
| Q5 | How is the run kept to one database when the shared transaction refuses a supplied connection? | Every module gets the same connection string, for a uniquely named in-memory shared-cache database that is kept alive for the run. |
| Q6 | How far is the report versioned? | One integer `schemaVersion`. The skill and the image ship separately, so the skill must be able to refuse a version it does not know. Nothing more. |
| Q7 | Is there an unredacted mode? | No. The user already holds the raw definitions, and the skill asks before reading them. |
| Q8 | Does a Must fix finding mean patching the definition or building a component? | Each code's remedy comes from one code table, kept with the diagnostic codes and enforced by a completeness test. |
| Q9 | How do the user's packages load without the network? | From the mounted `--packages` folder, read as a directory feed into a run-scoped install root. A missing dependency refuses the run. |
| Q10 | What about a user with their own Elsa 4 host? | No `--host` option. They bring their features as packages, as everyone else does. |
| Q11 | Who finds a declared JavaScript global that is referenced but not called? | The converter, with a diagnostic of its own that names the global at the reference's JSON path. The CLI only reports codes, so the finding reaches the report as a `javascript-function` component finding. |
| Q13 | What do the CLI's own findings (unreadable input file, no published version) use as codes? | Codes of the tooling's own, `ELS3-MIGRATION-*`, in the same code table. An existing code such as `ELS3-MIGRATION-UNSUPPORTED-INPUT` is reused where it fits. |
| Q12 | What does a run need that the engine refuses to start without? | Generated, run-only values for the engine's required secret settings (FR-008a). Where the user's settings set them, the user's values win. |

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001 - Complete inventory**: On the fixture export, every custom component a definition uses appears in the
  inventory with the right kind and count. No unused component appears. Every finding has a category, and every Must
  fix finding has a remedy.
- **SC-002 - Corpus run**: Over the 34 definitions in the specification 1235 acceptance set, `inventory --all-versions`
  in the migration image completes. Every definition gets exactly one bucket. Every definition version that startup
  reconciliation publishes from the same input, on the same composition, is also published by the run and present in
  `migrated/`.
- **SC-003 - Nothing persists**: After a run, including a run that fails part-way, nothing remains outside the output
  folder: the container is removed, and no database or install root survives.
- **SC-004 - Loadable output**: The essentials engine image, with JSON workflow and activity reconciliation over a run's
  `migrated/`, loads every workflow in it that uses no reusable, including when the run exported no reusable or no workflow. Reusables, and the workflows that use them, load once
  the engine gap of Q2 is fixed.
- **SC-005 - Redaction**: The sentinel test finds no sentinel in `report.json` or `report.html`.
- **SC-006 - Determinism**: Two runs over the same input produce byte-identical `report.json` and `migrated/`.
- **SC-007 - Assess resolves**: With the fixture feature package, `assess` clears every finding that names one of the
  package's components, and adds no finding unrelated to them. The fixture is built so that the definitions it unblocks
  publish clean.
- **SC-008 - Offline**: A run with container networking disabled produces the same output as one with networking
  enabled.

## Dependencies and boundaries

- **Essentials engine image and feature list** (specification 1236). The migration image is built `FROM` the engine
  image, and its composition starts from the feature list. The `appsettings.json` merge comes from that specification
  too. The engine must also include JSON activity reconciliation, so that `migrated/activities/` loads, and it must
  carry `Nuplane.Sources.Directory`, so that directory feeds load.
- **Converter diagnostics this specification relies on.** The converter maps declared Elsa 3 JavaScript globals to
  prefixed host functions (direct calls only), and diagnoses unregistered variable types and unmapped storage drivers. These supply
  the codes from which the custom-component inventory is built, and this specification adds their entries to the code
  table.
- **Converter diagnostic for non-call references.** A declared global that a script references without calling it is
  not rewritten, and it fails at run time. The converter refuses it as `ELS3-REPLACED-FUNCTION-REFERENCE` (Q11), and
  this specification adds its entry to the code table.
- **Component metadata in the converter** (FR-016a): `ComponentName` metadata on component diagnostics, and a code of
  its own for a call of an undeclared JavaScript function. This specification makes that converter change (T004b).
- **ADR 0076 amendment** (Q4): the worker may reach one further named frozen entry point per command group, each a
  compatibility surface. Such a group may, by default, reconcile local directory feeds into a private, run-scoped
  install root. Until the amendment is agreed, this specification stays provisional.
- **Engine gap**: the engine's JSON activity reconciliation refuses source-owned activity content that depends on other
  activity versions. Specification 1239 fixes that gap (Q2).
- **Migration skill** (specification 1238): it starts the image with `docker run` and consumes `report.json` through
  the schema this specification publishes.
- [Elsa constitution](../../.specify/memory/constitution.md) E2.7 keeps Elsa 3 behind the import boundary. The Elsa 3
  entry point, the converter, the addition list and the migration image's Dockerfile live under `extensions/Elsa3/`,
  and the essentials and the engine image carry no Elsa 3 code.
- Out of scope: live instances; running Elsa 3 next to Elsa 4; persistence data; identity and auth; Elsa 3 Studio
  extensions; any publishing or deployment step.

## Assumptions

- Elsa 3 exports mark which version is published and which is latest, as Studio exports do.
- The essentials engine image merges `appsettings.json` in a way that a non-web composition can reproduce.
  Specification 1236 defines that merge.
- The user's feature packages are Nuplane packages, built the way the engine image loads them.
