# Elsa 3 → Elsa 4 workflows migration: design

Status: agreed with Joey (coordinator, #3052) in a grilling session on 2026-11-24. This is the intake for three specs.
Nothing is built yet. Facts are from `origin/initiative/3052-elsa3-to-elsa4-conversion` at `2db2d8a899`.

## Problem

An Elsa 3 user has an export of their workflow definitions and an Elsa 3 codebase with custom activities, JavaScript
functions, variable types and storage drivers. They cannot see what moving to Elsa 4 takes. Today:

- The only ways to convert are the HTTP collection import, which needs a running API and converts only at *apply*,
  where one bad item refuses the whole apply, and startup reconciliation, which **publishes into the store** and
  reports skips only as log lines. In S-15's Workbench run, 10 definitions were published, 24 were skipped, and the
  reasons were only in the log.
- Custom components show up one refusal at a time: `ELS3-ACTIVITY-UNMAPPED-TYPE`, `VF-IMP-005`. Some don't show up
  at all: an unregistered variable type silently becomes `Any`.
- There is no guidance for porting Elsa 3 activity code to the Elsa 4 activity API.

## Goal

A test migration the user can run on their own machine, as often as they like. It produces a complete report of what
they must correct or patch, what to reconsider, what to take into account, and what goes smoothly. It also produces a
folder of converted Elsa 4 workflows. Only conversion happens, never publishing. Nothing persists.

## Decisions

### The journey (human-led, AI keeps it on track)

1. **Inventory.** Run the converter over the export. The custom components the definitions actually use come from
   the diagnostic codes: unmapped activity types, undeclared JS functions, unregistered variable types and unmapped
   storage drivers. A custom component that no workflow uses is not migrated. The skill then asks for the folder or
   repositories of the user's Elsa 3 source, and matches each item to its implementation.
2. **Prepare.** Lift, correct and shift the existing source and tests into Elsa 4 features. There is no spec per
   component. The skill fills in a **migration plan template** and follows a fixed **porting catalog** (below).
3. **Test migration.** Run conversion and the real publish pipeline with the user's Elsa 4 features loaded. Fix and
   rerun until every remaining item is clean or an accepted refusal.
4. **Handoff (outside the tool).** The user keeps their feature projects in any repository, publishes their NuGet
   packages through any pipeline, and stores the converted workflow JSON wherever they want. Going live means
   starting the essentials engine image with their `appsettings.json`, their packages and `migrated/` mounted.

### Custom components

- **Activities** keep their Elsa 3 type name. The resolver already looks the Elsa 3 type up directly as an Elsa 4
  catalog key, and a CLR activity's key is its `FullName`. `[ReplacesActivityType]` is only for when the names cannot
  match, for example when Elsa 3's `[Activity(namespace, type)]` changed the name.
- **JS functions:** the Elsa 4 namespace-prefix rule stays (ADR 0045 §5 D-5). The migrator picks one or more
  prefixes, and that choice is a recorded step in the plan. The converter maps Elsa 3 globals to prefixed
  Elsa 4 host functions: **#5664** (PR #5667). Since PR #5703 no declaration: a global matches the host function whose
  name after the prefix is the Elsa 3 name.
- **Variable types:** register the type in an Elsa 4 feature under a dotted alias and it converts typed. This is not
  related to proxies (#4103); a script calling .NET members on a value stays refused (`VF-IMP-012`). The converter
  gains the missing diagnostics: **#5666**.
- **Storage drivers** are not migrated: every member converts to `elsa.json` with an `ELS3-BEHAVIOUR-*` warning
  (Reconsider) that starts the conversation in [Elsa 3 storage drivers](../../migration/elsa3-storage-drivers.md).

### The CLI: `dotnet elsa elsa3-migration workflows inventory|assess`

- A command group of the `dotnet-elsa` CLI, built on its worker (ADR 0076).
  - `Elsa.Cli` reaches a second frozen entry point by type name. That entry point lives under `extensions/Elsa3/`,
    so the essentials take no reference on Elsa 3 and Elsa 3 stays behind the import boundary.
- **It runs inside the Elsa 3 migration image** (see *Essentials images*). The skill starts it with `docker run`,
  mounting the export, the user's packages folder, their `appsettings.json` and the output folder. The test
  migration therefore runs on exactly the composition the user later goes live on, minus the Elsa 3 additions.
  - The CLI ships no Elsa 4 closure of its own. Docker on the user's machine is a prerequisite; the skill checks it
    first.
  - There is no `--host` option: a user with their own Elsa 4 host brings their features as packages, like everyone
    else.
- Not a product anyone deploys. The skill drives it.
- **`inventory <export> [--out <dir>]`** runs on the image's composition with default settings.
- **`assess <export> --packages <folder of .nupkg> --settings <appsettings.json> [--out <dir>]`** loads the user's
  feature packages through Nuplane, exactly as the engine image does.
- Neither the skill nor the user configures the Elsa 3 converter and import features; the migration image bakes
  them in.
- **Composition is real:** features are composed the way a shell does it, with no web host, CShells runtime or HTTP.
  The run uses the same `appsettings.json` merge the image uses.
- **No dry-run seam:** the CLI runs the **real** import and `PublishWorkflow` pipeline on **in-memory SQLite** and
  disposes it.
  - One shared connection for the whole run, so every EF module sees the same database (S-15 hit
    `EfSharedTransactionTargetMismatch` with separate databases).
  - No hosted services or trigger runtime are started.
  - Publish stops at the first error per definition. The report says so.
- **Input:** a folder (searched recursively) or a zip. Each file holds one definition or an array. The latest
  published version of each definition, plus any version a reusable reference pins, is converted. Other versions
  are listed, not converted. `--all-versions` converts everything.
- **Output:** the folder `--out` names holds `report.json` and `report.html` side by side, overwritten on each run,
  plus `migrated/`.
  - `migrated/` is exported from the run's own Elsa 4 store after publish: workflows in the JSON workflow
    reconciliation format under `migrated/workflows/`, reusables in the JSON activity reconciliation format under
    `migrated/activities/activities.json`.
  - The `report.json` schema is `specs/1237-elsa3-migration-cli/contracts/report.schema.json` (schemaVersion 1;
    consumers ignore unknown fields and read category/remedy/componentKind, never hard-coded codes).
  - Refused definitions appear only in the report.
  - The CLI prints the output path.
- **Exit codes:** 0 clean, 1 findings, 2 refused to run (bad input, a user package or setting the composition
  cannot boot).

### The report

- **Per finding:**
  - Error → **Must fix**: patch the Elsa 3 definition or build the missing component.
  - `ELS3-BEHAVIOUR-*` warning → **Reconsider**: it converts, but runs differently.
  - Info → **Take into account**.
  - No findings → **Goes smoothly**.
- **Per definition:** one S-15 bucket: `convert-as-is`, `convert-with-adjustments` or `incompatible`.
  - A definition refused only because a dependency was refused is marked *blocked by dependency*, so the root cause
    is fixed first.
- **Sections:**
  1. Summary counts.
  2. Custom-component inventory, with usage counts.
  3. Definitions by bucket, with each finding's code, JSON path and guidance.
  4. The settings and closure the run used.
  5. Dispatched-workflow targets missing from the export, as Take into account.
- `report.html` renders the same JSON. No extra data.

### Essentials images: `elsa-…-essentials`

- Two images:
  - an **engine** with exactly the features the S-15 JTest acceptance set needs;
  - a **Studio** with only the workflow designer.

  No BPMN, Copilot or other extras. Users explore those later.
- The feature composition is baked in and immutable. Users never see `shells.json`.
- Users deliver one `appsettings.json`. It can override the settings of baked features and add features, such as
  their own custom features. Whether configuration can switch a baked feature off is deliberately left alone.
- **The essentials engine carries no Elsa 3 code.**
- **A third image, the Elsa 3 migration image,** is built `FROM` the essentials engine at the same version. It adds
  the Elsa 3 features and their dependencies: the converter (`Elsa3Mapping`), the collection import for reusables,
  its EF ledger and the publishing bridge.
  - Its Dockerfile is maintained under `extensions/Elsa3/`, so Elsa 3 stays behind the import boundary and can be
    retired with the extension.
  - Its composition is the essentials feature list plus an Elsa 3 addition list, and it contains the `dotnet-elsa`
    CLI. Its entry point is the `elsa3-migration` command group; it starts no web host.
- The essentials feature list is defined once and feeds the engine image, and through it the migration image.
- **Acceptance:** the S-15 JTest acceptance set passes against the essentials engine image. S-15 itself stays on the
  Workbench, unchanged.
- The engine image includes JSON workflow reconciliation and JSON activity reconciliation, so `migrated/` can be
  mounted and loaded at startup. Source-owned composites with activity dependencies are currently refused by the
  engine; fixed separately in PR #5685.

### The skill and its documentation

- **Provider-neutral.** The canonical skill is plain markdown plus the CLI, following the repository's existing
  canonical-skill strategy (`AGENTS.md`, `.agents/skills/`), with no wording addressed to one model.
  - Thin adapters only point to it: a Claude Code plugin through `marketplace.json`, and an `.agents/skills/` copy.
  - It is readable as ordinary documentation. The main README gets a short "Migrating from Elsa 3" section that says
    which file to read, so installing it as a skill is optional.
- Lives in this repository in one self-contained folder, `plugins/elsa3-migration/skills/elsa3-workflows-migration/`
  (`SKILL.md`, the plan template and the porting catalog), separate from the contributor skills in `.claude/skills/`.
  The repo-root `.claude-plugin/marketplace.json` and the plugin's `plugin.json` are metadata only; `.agents/skills/`
  users copy the folder. No sync script (decided 2026-11-24, spec 1238). It is
  released with the `dotnet-elsa` CLI and the essentials images.
- **Prerequisite check:** Docker is available and the migration image matching the CLI version can be pulled.
- **Privacy is stated up front.** Before reading anything, the skill tells the user what can reach the model
  provider.
  - `report.json` is complete without an AI and redacted by default: codes, JSON paths, type and function names, but
    no literal values or script bodies.
  - The skill asks before reading raw definitions or source.
- **State** lives in the filled-in migration plan (markdown, in the user's AI workspace). Each run reads the plan
  first and resumes where it says. The CLI stays stateless. Humans are responsible for keeping the files.
- **No diagnostic knowledge is hard-coded.** The skill reads code, message and guidance from `report.json`.
- The skill checks that every custom feature in the plan is added to the user's `appsettings.json`.
- **Migration plan template:** stage, prefix choices, the components grouped into Elsa 4 features and packages.
  For each component: its Elsa 3 source location, its Elsa 4 target name, the porting cases that apply, the tests to
  port, and its status. Plus the path of the last report.
- **Porting catalog:** each case has an Elsa 4 counterpart and a before/after example, so every AI handles it the
  same way (a focused search, then a patch that follows the example). Some cases may be refusals ("redesign"); the
  spec decides each one against the code.
  1. Base class
  2. Type name and the exact-name rule
  3. Inputs (defaults, UI hints, required)
  4. Outputs
  5. Completion
  6. Outcomes
  7. Scheduling child activities
  8. Signals and bookmarks
  9. Triggers
  10. Services from the execution context instead of constructor injection
  11. Variables and workflow state inside an activity
  12. Background and task activities (spec 1230)
  13. Cancellation and faults
  14. JS functions as `JavaScriptHostFunctionDeclaration` with a prefix
  15. Liquid filters and other expression additions
  16. Variable type registration
  17. Storage drivers
  18. Feature registration: an Elsa 4 `ShellFeature` and a Nuplane package
  19. Tests (xunit)

  Checked against the code in spec 1238: cases 7 (custom composites), 8 (signals; bookmarks do port), 15 (custom
  Liquid filters become JS host functions) and 17 (per-variable storage drivers) are **redesign** entries. Case 10
  inverts: Elsa 4 uses constructor injection instead of the service locator. Case 12 becomes background placement
  (`[BackgroundExecutionSafe]`).

### Staying correct

- The CLI's stability comes from the JTest corpus that proves the essentials images. Users never run JTest, and
  nothing runs in users' CI.
- Contributor documentation (the Elsa 3 Mapping README and an `AGENTS.md` pointer) says that a converter change also
  needs:
  - JTest suites for it;
  - an update to the migration skill (porting catalog, plan template).

  An AI in a contributor session raises this if the human forgets. No enforcement mechanism.

### Out of scope

- Live workflow instances
- Running Elsa 3 next to Elsa 4
- Persistence data migration
- Identity and auth migration
- Elsa 3 Studio extensions
- C# and Python expressions (refused as `VF-IMP-007` with guidance)
- Any publishing or deployment step
- Network calls other than NuGet restore and image pulls

## Spec breakdown and order

| # | Unit | Depends on | Status |
|---|------|------------|--------|
| 0 | #5664 converter maps Elsa 3 functions to prefixed host functions | — | PR #5667 |
| 0 | #5666 converter diagnoses unregistered variable types and unmapped storage drivers | — | PR #5668 |
| 1 | **Essentials images**: engine and designer-only Studio, one feature list, JTest acceptance, base-image contract | — | spec 1236, PR #5682 |
| 2 | **Workflows migration CLI**: `elsa3-migration workflows inventory\|assess`, running inside the Elsa 3 migration image (`extensions/Elsa3/`, `FROM` essentials), in-memory SQLite, report, `migrated/` | 1 (engine image), #5664, #5666 | spec 1237, PR #5678 |
| 3 | **Migration skill and docs**: plan template, porting catalog, provider-neutral skill, README section, contributor triggers | 2 (report schema) | spec 1238, PR #5679 |

All three specs can be defined in parallel now. Implementation follows the dependency order.

## Open questions

- Whether the two images are new images or Workbench profiles (spec 1).

