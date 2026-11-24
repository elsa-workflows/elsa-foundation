# Feature Specification: Elsa 3 workflows migration CLI

**Feature Branch**: `agent/claude/3052-spec-migration-cli`

**Status**: Draft — provisional on the ADR 0076 amendment named under *Dependencies and boundaries*

**Input**: A test migration an Elsa 3 user runs on their own machine, as often as they like. It converts their Elsa 3
workflow export, runs the real Elsa 4 import and publish against a disposable in-memory database, and writes a complete
report plus a folder of migrated Elsa 4 content. It never publishes anywhere, and nothing it builds outlives the run.

## Rule and boundary

The command group is `dotnet elsa elsa3-migration workflows`, with two commands, `inventory` and `assess`. Both run the
**real** conversion, import and `PublishWorkflow` pipeline. There is no dry-run seam, no mock store and no running API.
Everything runs against an in-memory SQLite database that exists only for the run. The only files the run leaves
behind are in the output folder: `report.json`, `report.html` and `migrated/`.

The report is the product. It is complete without an AI reading it. It is redacted, so it can be handed to an AI model
provider: codes, JSON paths, type names, function names and definition identities, never a literal value or a script
body from the user's definitions or settings.

**Example.** An export holds 40 Elsa 3 definitions. Three use `Acme.Activities.SendInvoice`, a custom activity, and
one calls `formatIban()`, a JavaScript global the user registered in Elsa 3. `inventory ./export` exits 1 and reports:
the custom-component inventory lists `Acme.Activities.SendInvoice` (activity, 3 definitions, 5 nodes) and
`formatIban` (JavaScript function, 1 definition). Those four definitions are **Must fix**, with the code, the JSON
path and the guidance per finding. 30 definitions **go smoothly**, 6 carry a **Reconsider** warning, and
`migrated/` holds the 36 that converted and published. After the user builds an Elsa 4 feature package for both
components, `assess ./export --packages ./nupkgs --settings ./appsettings.json` loads it and reruns. The four
definitions convert, and the report shows what still remains.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Inventory an export (Priority: P1)

As an Elsa 3 user, I point the tool at my workflow export and learn, without writing or installing anything Elsa 4,
which definitions convert, which custom components they use, and what I must fix, reconsider or take into account.

**Why this priority**: This is the first step of the migration journey. Everything else builds on its report.

**Independent Test**: Run `inventory` over a fixture export that has clean definitions, definitions using unmapped
custom activities, an undeclared JavaScript function, an unregistered variable type, an unmapped storage driver, a
reusable workflow, and a definition whose reusable dependency is refused. Check the exit code, the report sections,
every finding's code, path, category and remedy, and the contents of `migrated/`.

**Acceptance Scenarios**:

1. **Given** an export folder (searched recursively) or a zip, **When** `inventory <export>` runs, **Then** it
   converts, imports and publishes each selected definition in the reference closure. It writes `report.json`,
   `report.html` and `migrated/` to `--out` (default `./elsa3-migration`), prints that path, and exits 0 when no
   finding is Must fix or Reconsider and 1 otherwise.
2. **Given** definitions that use custom components, **When** the report is written, **Then** its custom-component
   inventory lists each component once, with its kind (activity, JavaScript function, variable type, storage driver),
   its Elsa 3 name, the definitions that use it and its usage count. A component no definition uses does not appear.
3. **Given** a definition refused only because a reusable definition it uses was refused, **When** the report is
   written, **Then** that definition is marked *blocked by dependency* and names the refused dependency, so the root
   cause is fixed first.
4. **Given** the same export and the same closure, **When** `inventory` runs twice, **Then** `report.json` and every
   file in `migrated/` are byte-identical between the runs.

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
   packages load through Nuplane into the reference closure and the settings merge as the essentials engine image
   merges them, and the run otherwise behaves as `inventory`.
2. **Given** `--host <build output>` instead of `--packages`, **When** `assess` runs, **Then** it runs inside that Elsa
   4 host's closure with the given settings. A host without the Elsa 3 migration entry point is refused before
   anything is read, and the refusal names the missing entry point and the minimum version that carries it.
3. **Given** a package that needs a dependency neither the folder nor the closure holds, **When** `assess` runs,
   **Then** it refuses to run (exit 2) and names the missing package and version range. It does not download it.

### User Story 3 - Migrated content I can load (Priority: P1)

As an Elsa 3 user, I take the `migrated/` folder of a clean run and mount it into the essentials engine image, where
JSON reconciliation loads it.

**Why this priority**: Going live means starting the engine with this folder. The folder must be what Elsa 4 itself
holds after the import, not a second rendering of the conversion.

**Independent Test**: Run `inventory` over a fixture export. Start a fresh reference composition with JSON workflow and
JSON activity reconciliation pointed at `migrated/`. Compare the loaded definitions and versions with what the run
published.

**Acceptance Scenarios**:

1. **Given** a completed run, **When** `migrated/` is written, **Then** it holds what the run's in-memory Elsa 4 store
   holds after publish, exported from that store: workflows in the JSON workflow reconciliation format under
   `migrated/workflows/`, and reusable activities (Elsa 3 definitions used as activities) in the JSON activity
   reconciliation format under `migrated/activities/`, dependencies before dependents.
2. **Given** a definition that was refused, **When** `migrated/` is written, **Then** neither it nor any definition
   blocked by it appears there. They appear only in the report.
3. **Given** `migrated/` from a previous run, **When** the tool runs again, **Then** `migrated/`, `report.json` and
   `report.html` are replaced as a whole, and nothing else in `--out` is touched.

### User Story 4 - An AI-readable, redacted report (Priority: P2)

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
- Publish fails on a definition: publish stops at its first error, and the finding says that later publication
  errors may be hidden until it is fixed.
- A dispatched-workflow or execute-workflow target that is not in the export is listed as Take into account. It is
  not a refusal.
- C# and Python expressions are refused with the existing value-flow code and its guidance.
- A converted reusable activity depends on other activity versions: the engine's source reconciliation cannot yet
  publish such content. The report flags the affected `migrated/` items as Take into account until the engine
  supports it (see *Dependencies and boundaries*).
- The reference host package is not in the local NuGet cache and the machine is offline: refused to run, exit 2,
  naming the package and the version needed.

## Requirements *(mandatory)*

### Functional Requirements

**Commands and process**

- **FR-001**: `dotnet-elsa` MUST offer the `elsa3-migration workflows` group with `inventory <export> [--out <dir>]
  [--all-versions]` and `assess <export> (--packages <dir> | --host <dir>) [--settings <file>] [--out <dir>]
  [--all-versions]`. `--settings` is required with `--packages`.
- **FR-002**: The group MUST run on the `dotnet-elsa` worker as ADR 0076 describes. The front end carries no Elsa 3
  and no Elsa 4 product reference. The worker runs inside the selected host closure and reaches a frozen entry point
  under `extensions/Elsa3/` by type name, taking versioned JSON on stdin and answering on stdout.
- **FR-003**: `inventory` and `assess --packages` MUST run in the **reference closure**: a host build output whose
  composition is the essentials engine feature list plus the Elsa 3 converter and import features. It is published as
  a NuGet package version-pinned to the tool and restored into the NuGet cache on the first run that needs it. This
  restore is the group's only network access.
- **FR-004**: Exit codes MUST be 0 (no Must fix and no Reconsider finding), 1 (findings), and 2 (refused to run: bad
  arguments, unusable input, an unbootable closure, a missing package or host entry point). The persistence group's
  codes are unchanged.
- **FR-005**: On exit 0 or 1 the tool MUST print the absolute output path. On exit 2 it prints the refusal reason,
  writes no report, and leaves the output folder untouched.

**Composition and persistence**

- **FR-006**: Features MUST be composed as a shell composes them, from the reference closure's feature list (or the
  `--host` composition) plus `--settings`, merged as the essentials engine image merges `appsettings.json`. No web
  host, CShells runtime, HTTP endpoint, hosted service, startup task, trigger runtime or scheduler is started.
- **FR-007**: Every EF module MUST use the same in-memory SQLite database, named uniquely per run. The run holds that
  database open for its whole duration and disposes it at the end, so every module sees the same data and the
  shared-transaction single-target rule holds. A persistence connection string in `--settings` MUST be replaced for
  the run, and the report says that it was.
- **FR-008**: Nothing the run writes may outlive it, except the output folder. Package extraction goes to a run-scoped
  temporary folder that is deleted at the end, including after a failure.

**Selection and pipeline**

- **FR-009**: The input MUST be a folder (searched recursively for `*.json`) or a zip. Each file holds one Elsa 3
  definition or an array of them.
- **FR-010**: Per definition, the latest published version MUST convert, plus every version that a reusable reference
  pins. All other versions are listed as not converted. `--all-versions` converts every version.
- **FR-011**: The run MUST perform the same sequence as Elsa 3 startup reconciliation: input selection, collection
  analysis, apply (excluding refused items and their dependents), then `PublishWorkflow` per imported workflow. It
  shares that implementation instead of duplicating it. It collects every diagnostic and skip into the report instead
  of the log.
- **FR-012**: Publish MUST stop at the first error per definition. The finding states that the publish stage was
  reached and that later errors may be hidden.

**Report**

- **FR-013**: The report MUST map each finding to one category: Error → **Must fix**; `ELS3-BEHAVIOUR-*` warning →
  **Reconsider**; any other warning or Info → **Take into account**. A definition with no finding **goes smoothly**.
- **FR-014**: Every Must fix finding MUST carry a remedy: `patch-definition` (change the Elsa 3 definition),
  `build-component` (supply the missing Elsa 4 component), or `none` (no Elsa 4 equivalent). The remedy, and the
  component kind for component codes, comes from one code table maintained with the converter's diagnostic codes. A
  test fails when a code the converter can emit has no entry.
- **FR-015**: Every definition MUST carry exactly one bucket: `convert-as-is` (no Must fix, no Reconsider),
  `incompatible` (any Must fix with remedy `none`), or `convert-with-adjustments` (everything else). A definition
  refused only through a refused dependency is also marked *blocked by dependency*, naming it.
- **FR-016**: The report MUST have these sections: (1) summary counts per category and bucket; (2) the
  custom-component inventory, with usage counts; (3) definitions by bucket, each with its findings' code, severity,
  category, remedy, JSON path, message and guidance; (4) the settings and closure the run used: tool and reference
  closure versions, loaded package ids and versions, enabled features, setting keys (never values), and the
  persistence override; (5) dispatched-workflow targets missing from the export, as Take into account. It also lists
  the versions not converted, and the `migrated/` items that need the engine's source reconciliation of composites
  with dependencies.
- **FR-017**: The report MUST NOT contain a literal value, an expression or script body, a variable default or a
  setting value from the input. Diagnostic messages and guidance the converter emits for the report MUST meet this
  rule. A converter message that embeds such text is changed at the converter. Redaction is the only mode.
- **FR-018**: `report.json` MUST carry an integer `schemaVersion`, starting at 1, and conform to the schema this
  specification publishes. A change that removes or changes the meaning of a field increments it. Adding a field does
  not.
- **FR-019**: `report.html` MUST be a single self-contained file that renders exactly the data in `report.json`, with
  no network access and no data of its own.
- **FR-020**: For identical input, closure, packages and settings, `report.json` and `migrated/` MUST be
  byte-identical. They carry no timestamps, machine paths or run identifiers. The printed output path is not part of
  them.

**Migrated content**

- **FR-021**: `migrated/` MUST be exported from the run's Elsa 4 store after publish: workflows in the JSON workflow
  reconciliation format, one file per definition under `migrated/workflows/`; reusable activities in the JSON activity
  reconciliation format, in one file under `migrated/activities/`, dependencies first. It carries no publication
  state. Whether a loading engine publishes is that engine's configuration.
- **FR-022**: Refused and blocked definitions MUST NOT appear in `migrated/`.

**Boundaries**

- **FR-023**: The group MUST NOT publish to, call, or write into any Elsa server, database or store outside the run. It
  makes no network call other than the restore in FR-003.
- **FR-024**: Unconvertible Elsa 3 behaviour MUST be refused with a diagnostic and guidance. The CLI adds no converter
  workaround. A gap it finds becomes a converter change with its own diagnostic, or an Elsa 4 specification.

### Key Entities

- **Run**: the closure, the packages, the settings keys, the input and the selected versions. Its results are the
  report and `migrated/`.
- **Finding**: code, severity, category, remedy, component kind (for component codes), JSON path, definition identity,
  message, guidance.
- **Definition result**: definition id, name, version, reusable flag, bucket, blocked-by, findings, published flag,
  exported path.
- **Component**: kind, Elsa 3 name, using definitions, usage count.
- **Code table**: code → remedy and component kind.

## Spec decisions

| ID | Question | Decision |
|---|---|---|
| Q1 | Where does the migrated content come from? | It is exported from the run's own Elsa 4 store after publish, not written from the converter's output, and the folder is named `migrated/`. Workflows use the workflow format; reusables use the activity format. |
| Q2 | Converted reusables depend on other activity versions, and the engine's source reconciliation refuses such content today. | The engine gap is fixed in its own issue, outside this specification. Here, the CLI exports reusables anyway and the report flags them. Only SC-004's composite case waits on that fix. |
| Q3 | Where does the reference closure come from? | A NuGet package, version-pinned to the tool, restored on first use. Persistence users never carry it. |
| Q4 | Does using the ADR 0076 worker for this group need an ADR change? | Yes. A small ADR 0076 amendment allows further named frozen entry points and this group's first-use restore. This specification is provisional on it. |
| Q5 | How is the run kept to one database when the shared transaction refuses a supplied connection? | All modules get the same connection string for a uniquely named in-memory shared-cache database, kept alive for the run. |
| Q6 | How far is the report versioned? | One integer `schemaVersion`. The skill and the tool ship separately, so the skill must be able to refuse a version it does not know. Nothing more. |
| Q7 | Is there an unredacted mode? | No. The user already holds the raw definitions, and the skill asks before reading them. |
| Q8 | Must fix: patch the definition or build a component? | Each code's remedy comes from one code table kept with the diagnostic codes and enforced by a completeness test. |
| Q9 | How do the user's packages load without the network? | From the `--packages` folder as a directory feed into a run-scoped install root. A missing dependency refuses the run. |

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001 - Complete inventory**: On the fixture export, every custom component that a definition uses appears in the
  inventory with the right kind and count. No unused component appears, and every finding has a category and, for
  Must fix, a remedy.
- **SC-002 - Corpus run**: Over the 34 definitions in the S-15 JTest acceptance set, `inventory` completes. Every
  definition gets exactly one bucket, and every definition that startup reconciliation publishes in the same closure
  is also published by the run and present in `migrated/`.
- **SC-003 - Nothing persists**: After a run, including a run that fails mid-way, no file or database exists outside
  the output folder and the NuGet cache that was not there before.
- **SC-004 - Loadable output**: A fresh reference composition with JSON workflow and activity reconciliation over a
  run's `migrated/` loads every workflow in it. A reusable with activity dependencies loads once the engine gap
  of Q2 is fixed.
- **SC-005 - Redaction**: The sentinel test finds no sentinel in `report.json` or `report.html`.
- **SC-006 - Determinism**: Two runs over the same input produce byte-identical `report.json` and `migrated/`.
- **SC-007 - Assess resolves**: With the fixture feature package, `assess` clears exactly the findings that package
  addresses and nothing else changes.

## Dependencies and boundaries

- **Essentials engine feature list** (specification 1236): the reference closure's composition and the
  `appsettings.json` merge come from it. This specification does not define the list.
- **Converter diagnostics in progress**: the mapping of declared Elsa 3 JavaScript globals to prefixed host functions,
  and the diagnostics for unregistered variable types and unmapped storage drivers. They supply the codes from which
  the custom-component inventory is built. This specification adds their entries to the code table.
- **ADR 0076 amendment**: the worker may reach further named frozen entry points, each a compatibility surface, and
  this group may restore its pinned reference host package on first use. Until it is agreed, this specification stays
  provisional.
- **Engine gap**: source-owned activity content that depends on other activity versions is refused by the engine's
  JSON activity reconciliation. It is fixed outside this specification (Q2).
- **Migration skill** (specification 1238): it consumes `report.json` through the schema this specification publishes.
- [Elsa constitution](../../.specify/memory/constitution.md) E2.7 keeps Elsa 3 behind the import boundary: the Elsa 3
  entry point and converter live under `extensions/Elsa3/`, and the essentials and the engine image carry no Elsa 3
  code.
- Out of scope: live instances, running Elsa 3 next to Elsa 4, persistence data, identity and auth, Elsa 3 Studio
  extensions, and any publishing or deployment step.

## Assumptions

- Elsa 3 exports mark which version is published and which is latest, as Studio exports do.
- The essentials engine image merges `appsettings.json` in a way a non-web composition can reproduce. Specification
  1236 defines that merge.
- The user's feature packages are Nuplane packages, built the way the image loads them.
