# Tasks: Elsa 3 workflows migration CLI

**Input**: [spec.md](spec.md), [plan.md](plan.md), [research.md](research.md), [data-model.md](data-model.md),
[contracts/](contracts/), [quickstart.md](quickstart.md).

**Prerequisites before implementation starts**:

- this specification is approved, and the ADR 0076 amendment is agreed (spec Q4);
- the essentials engine image and feature list of specification 1236 exist on the initiative branch;
- the converter diagnostic units named under *Dependencies and boundaries* are merged.

T032 also waits on the engine gap of spec Q2, for its composite case only.

**Tests**: Risk-based (framework §2.23). Each test is written with the code it covers:

- code-table completeness;
- the bucket rule;
- redaction;
- determinism;
- the single database;
- the output swap;
- the refusal paths;
- the shared-pipeline extraction;
- the image composition.

**Tooling root**: `extensions/Elsa3/src/Migration/Tooling/`, written `Tooling/` below.
**Tests root**: `extensions/Elsa3/tests/Migration/`, written `MTests/` below.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: US1 to US5 from spec.md

---

## Phase 1: Setup

- [ ] T001 Create `Tooling/Elsa3.Migration.Tooling.csproj`. It references the Elsa 3 models, mapping, import, import
  persistence, publishing and reconciliation projects, and the JSON workflow and activity reconciliation projects.
  Create `MTests/Tests/Elsa3.Migration.Tests.csproj` (xunit). Add both to the solution, regenerate the solution filters
  and check the CI tier placement (§E7).
- [ ] T002 [P] Build the fixture export in `MTests/Fixtures/export/`, with:
  - clean definitions;
  - an unmapped custom activity used by three definitions;
  - an undeclared JavaScript function;
  - an unregistered variable type and an unmapped storage driver;
  - a reusable definition with two users;
  - a definition whose reusable dependency is refused;
  - a C# expression;
  - a dispatch target absent from the export;
  - a definition with three versions: the latest published one, an older one pinned by a reusable reference, and an
    unpinned one;
  - duplicate input;
  - one non-JSON file;
  - a zip copy.

  Every literal, script body, variable default and setting value carries the sentinel `SENTINEL-1237`.
- [ ] T003 [P] Build the fixture feature package project in `MTests/Fixtures/AcmeFeature/`. It registers the custom
  activity under its Elsa 3 type name, an `acme.`-prefixed JavaScript host function and a variable type alias. Pack it
  to a `.nupkg` in the test setup, and add its `appsettings.json`.

## Phase 2: Foundational (blocks every story)

- [ ] T004 Add `Elsa3MigrationCodeTable` in `extensions/Elsa3/src/Models/`, mapping each code to a remedy and a
  component kind. Give it an entry for every code the converter, the import and reconciliation emit, including the
  codes of the converter diagnostic units. Add the completeness test in `extensions/Elsa3/tests/Mapping/Tests/`: it
  reflects over every code constant class, and fails on an Error code without a remedy or a component code without a
  kind (FR-016).
- [ ] T005 Extract `Elsa3ImportRun` from `Elsa3FolderImportTask` in
  `extensions/Elsa3/src/Activities/Design/Import/Reconciliation/Services/`. It returns the plan items, the skips with
  their cause and code, the publication outcomes and the publish diagnostics. Make the startup task call it and log as
  before, and add `InternalsVisibleTo("Elsa3.Migration.Tooling")`. The existing reconciliation integration tests must
  stay green unchanged (§2.21.1, FR-013).
- [ ] T006 [P] Add `IJsonWorkflowCatalogWriter` and `JsonWorkflowCatalogWriter` in
  `essentials/src/Workflows/Design/Reconciliation/Json/`, with a round-trip test against `JsonWorkflowCatalogReader`.
  Update the project's README and `EXTENSION_POINTS.md` (R-8).
- [ ] T007 [P] Add `IJsonActivityCatalogWriter` and `JsonActivityCatalogWriter` in
  `essentials/src/Activities/Design/Reconciliation/Json/`, with a round-trip test against `JsonActivityCatalogReader`.
  Update the project's README and `EXTENSION_POINTS.md` (R-8).
- [ ] T008 Add `Tooling/Elsa3MigrationToolingHost.cs`, with the frozen `RunAsync(Stream, Stream)` and the request and
  response records of [contracts/worker-entry-point.md](contracts/worker-entry-point.md), including the refusal of an
  unknown `contractVersion`. Test the contract parsing.
- [ ] T009 Add `Tooling/MigrationComposition.cs` (R-4, R-5). It builds a generic host that is never started, with one
  shell from the composition and the merged settings. It sets every enabled EF feature to SQLite on
  `file:elsa3-migration-<guid>;Mode=Memory;Cache=Shared`, sets the migrate policy to apply, keeps one connection open,
  and disposes everything on every path. Tests:
  - two EF modules commit in one shared transaction without `EfSharedTransactionTargetMismatchException`;
  - no `IHostedService` or startup task runs;
  - no file appears outside a temporary `--out`.

  (FR-008 to FR-010, SC-003.)
- [ ] T010 Add the `elsa3-migration` command family to `essentials/src/Cli/Worker/WorkerContract.cs`, and resolution of
  the second entry point by name to `ToolingEntryPoint.cs`. A missing type is a resolution failure that names the type
  and the minimum version. Test it with a fake host layout that lacks the assembly.
- [ ] T011 Add `essentials/src/Cli/Elsa.Cli/Elsa3MigrationCommands.cs`, with:
  - the two commands;
  - the argument rules and refusal reasons of [contracts/cli.md](contracts/cli.md);
  - the per-group exit mapping (FR-004);
  - the printed output path (FR-005).

  Register the group in `ElsaCli.cs`. Test the argument combinations and the exit mapping in the existing CLI test
  style, and confirm that `Elsa3MigrationBoundaryTests` stays green.
- [ ] T012 Extend `essentials/src/Cli/Worker/NuplanePackageSet.cs`, or add a sibling, with the run-scoped install root
  (R-6). The image's Elsa 3 addition folder and, for `assess`, the `--packages` folder are reconciled as directory
  feeds into one temporary root. Resolution is restricted to those feeds and the host's closure, and the root is
  deleted on every path. An unresolvable dependency is the `package-unresolved` refusal. Test it with the T003 package
  and with a package whose dependency is missing.

## Phase 3: User Story 3 - The Elsa 3 migration image (P1)

- [ ] T013 [US3] Add `extensions/Elsa3/docker/elsa3-additions.json`, the Elsa 3 addition list: `Elsa3Mapping`, the
  collection import for reusables, the import's EF ledger, the publishing bridge and their dependencies, plus the
  `Elsa3.Migration.Tooling` assembly package. Check that the pack workflow packs each of them as a Nuplane package.
- [ ] T014 [US3] Add `extensions/Elsa3/docker/Dockerfile`. It is `FROM` the essentials engine image at the same
  version, bakes the addition packages into an image-owned folder, installs `dotnet-elsa` at the same version, and sets
  the entry point to `dotnet-elsa elsa3-migration` with the worker pointed at the engine host's build output (R-7). It
  starts no web host. Add the image to the workflow that publishes the essentials images, at their version.
- [ ] T015 [US3] Add the image test: the image builds; its composition is the essentials feature list plus the addition
  list; `--help` lists the group; and the essentials engine image contains no assembly whose name contains `Elsa3`.

## Phase 4: User Story 1 - Inventory an export (P1)

- [ ] T016 [US1] Add `Tooling/ExportInputReader.cs` (FR-011, edge cases). It reads a folder (recursive `*.json`, in
  ordinal order) or a zip, with one definition or an array per file. A file it cannot read becomes an input finding.
  It refuses with `input-missing`, `input-empty` and `input-unreadable`. Test it with the fixture.
- [ ] T017 [US1] Add `Tooling/VersionSelection.cs` (FR-012). It selects the latest published version plus the versions
  that reusable references pin, lists the versions not converted, supports `--all-versions`, and adds the Info finding
  for a definition with no published version. Test each fixture case.
- [ ] T018 [US1] Wire the entry point: read, select, run `Elsa3ImportRun`, and collect every diagnostic with its stage.
  Publish stops at the first error and marks `publishStoppedHere` (FR-013, FR-014).
- [ ] T019 [US1] Add `Tooling/ReportBuilder.cs`. It provides:
  - categories (FR-015);
  - remedies and component kinds from the code table (FR-016);
  - buckets and blocked-by (FR-017);
  - all the sections of FR-018: summary, components with usage counts, definitions, versions not converted, missing
    targets, input findings and run;
  - the ordering rules of data-model.md.

  Unit-test the bucket rule and the category mapping table-driven, and test the component inventory: no unused
  component, correct counts (SC-001).
- [ ] T020 [US1] Detect dispatched and executed workflow targets missing from the export, from the converted
  definitions' dispatch and execute-workflow activities. List them as Take into account (FR-018 item 5), and test with
  the fixture.
- [ ] T021 [US1] Add a golden-file integration test in `MTests/Tests/`. The fixture export runs through `inventory`
  in-process: `report.json` must equal the committed golden file, and the exit code must be 1. A second run must be
  byte-identical, `migrated/` included (FR-022, SC-006).

## Phase 5: User Story 4 - Migrated content (P1)

- [ ] T022 [US4] Add `Tooling/MigratedExporter.cs` (FR-023, FR-024). It reads the published workflow versions and
  reusable activity versions back from the design stores and writes them through the T006 and T007 writers: one file
  per workflow definition, and one activities file in dependency order. It sets `exportedTo` and
  `needsCompositeSourceSupport`, and leaves out refused and blocked definitions. Test the layout and the absence.
- [ ] T023 [US4] Add the output swap: write to a staging folder beside `--out`, replace `report.json`, `report.html`
  and `migrated/` last, and touch no other file. A failed run leaves the previous output intact. Test both paths.
- [ ] T024 [US4] Add the loadability test (SC-004). Compose the essentials feature list with JSON workflow and activity
  reconciliation over the fixture run's `migrated/`, and assert that every exported workflow loads. Assert the
  composite-with-dependencies case as skipped with `activity.source.dependencies-unsupported` until the spec Q2 gap is
  fixed; T032 then flips it.

## Phase 6: User Story 2 - Assess with my own features (P1)

- [ ] T025 [US2] Merge `--settings` as specification 1236 defines. Record setting keys only, and set
  `persistenceOverridden` when a persistence connection setting was replaced (FR-009, FR-018 item 4). Test that no
  setting value reaches the report.
- [ ] T026 [US2] Map composition failures caused by a user package or setting to the `composition-failed` refusal, with
  no partial run. Name the feature, package or setting key, never a value. Test with a fixture package whose feature
  throws during configuration.
- [ ] T027 [US2] Add the assess integration test (SC-007). Run the fixture export with the T003 package and its
  settings. Exactly the findings for the custom activity, the function and the variable type disappear, and every other
  finding is unchanged.

## Phase 7: User Story 5 - Redacted, versioned report (P2)

- [ ] T028 [US5] Make every message and guidance from the converter, the import and reconciliation free of input text
  (FR-019). Start with the strict-mode parse message, and find the others with the sentinel test (T029). Change each
  message so that it names the cause without quoting input, and add no workaround (FR-026).
- [ ] T029 [US5] Add the sentinel test: the fixture run's `report.json` and `report.html` contain no `SENTINEL-1237`
  (SC-005).
- [ ] T030 [US5] Add the schema test: every report the tests produce validates against
  [contracts/report.schema.json](contracts/report.schema.json). Copy the schema into `Tooling/` as an embedded resource,
  and add a test that keeps it identical to the contract file (FR-020).
- [ ] T031 [US5] Add `Tooling/ReportHtml.cs` and the embedded `report.html` template. The page inlines the escaped JSON,
  renders every section grouped by bucket and category, and works offline (FR-021). Test that the page holds the same
  JSON, contains no external URL, and escapes script-breaking strings.

## Phase 8: Polish and acceptance

- [ ] T032 Once the spec Q2 engine gap is fixed on the initiative branch, flip T024's composite case to assert that it
  loads (SC-004).
- [ ] T033 Run `inventory` over the S-15 acceptance set (34 definitions) in the migration image. Check that every
  definition has one bucket, and that the published set equals what startup reconciliation publishes on the same
  composition (SC-002). Record the result in the PR evidence, and check the two-minute goal.
- [ ] T034 Add the image smoke test (SC-003, SC-008). Run `docker run --rm --network none` with the fixture export,
  then with the T003 packages. Expect exit 1, the printed path and the same `report.json` as the in-process run.
  Nothing may remain outside the mounted output folder.
- [ ] T035 [P] Write `Tooling/README.md`, covering the commands, the image, the entry point contract, the database rule
  and redaction. Link it from `extensions/Elsa3/src/Mapping/README.md` and `essentials/src/Cli/Elsa.Cli/README.md`.
- [ ] T036 Set this specification's status to Implemented in the finishing PR.
