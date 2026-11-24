# Tasks: Elsa 3 workflows migration CLI

**Input**: [spec.md](spec.md), [plan.md](plan.md), [research.md](research.md), [data-model.md](data-model.md),
[contracts/](contracts/), [quickstart.md](quickstart.md).

**Prerequisites before implementation starts**: this specification is approved, the ADR 0076 amendment is agreed
(spec Q4), the essentials engine feature list from specification 1236 exists on the initiative branch, and the two
converter diagnostic units named under *Dependencies and boundaries* are merged. T031 also waits on the engine gap of
spec Q2, for its composite case only.

**Tests**: Risk-based (framework §2.23). Tests are written with the code they cover: code-table completeness, bucket
rule, redaction, determinism, the single database, the output swap, refusal paths and the shared-pipeline extraction.

**Tooling root**: `extensions/Elsa3/src/Migration/Tooling/` (written `Tooling/` below). **Tests root**:
`extensions/Elsa3/tests/Migration/` (written `MTests/`).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: US1 to US4 from spec.md

---

## Phase 1: Setup

- [ ] T001 Create `Tooling/Elsa3.Migration.Tooling.csproj` (references the Elsa 3 models, mapping, import, import
  persistence, publishing and reconciliation projects, and the JSON workflow and activity reconciliation projects) and
  `MTests/Tests/Elsa3.Migration.Tests.csproj` (xunit). Add both to the solution, regenerate the solution filters and
  check the CI tier placement (§E7).
- [ ] T002 [P] Build the fixture export in `MTests/Fixtures/export/`. It needs clean definitions; an unmapped custom
  activity used by three definitions; an undeclared JavaScript function; an unregistered variable type; an unmapped
  storage driver; a reusable definition and two users of it; a definition whose reusable dependency is refused; a C#
  expression; a dispatch target absent from the export; a definition with three versions (latest published, older
  pinned by a reusable reference, unpinned); duplicate input; one non-JSON file; a zip copy. Every literal, script
  body, variable default and setting value carries the sentinel `SENTINEL-1237`.
- [ ] T003 [P] Build the fixture feature package project in `MTests/Fixtures/AcmeFeature/`. It registers the custom
  activity under its Elsa 3 type name, a prefixed JavaScript host function, and a variable type alias, and is packed
  to a `.nupkg` by the test setup. Add its `appsettings.json`.

## Phase 2: Foundational (blocks every story)

- [ ] T004 Add `Elsa3MigrationCodeTable` in `extensions/Elsa3/src/Models/` (code → remedy, component kind), with an
  entry for every code the converter, import and reconciliation emit, including the codes of the two converter
  diagnostic units. Add the completeness test in `extensions/Elsa3/tests/Mapping/Tests/`: it reflects over every code
  constant class and fails on an Error code without a remedy or a component code without a kind (FR-014).
- [ ] T005 Extract `Elsa3ImportRun` from `Elsa3FolderImportTask` in
  `extensions/Elsa3/src/Activities/Design/Import/Reconciliation/Services/`. It returns plan items, skips with cause and
  code, publication outcomes and publish diagnostics. Make the startup task call it and log as before. Add
  `InternalsVisibleTo("Elsa3.Migration.Tooling")`. The existing reconciliation integration tests must stay green
  unchanged (§2.21.1, FR-011).
- [ ] T006 [P] Add `IJsonWorkflowCatalogWriter` and `JsonWorkflowCatalogWriter` in
  `essentials/src/Workflows/Design/Reconciliation/Json/`, with a round-trip test against `JsonWorkflowCatalogReader`.
  Update its README and `EXTENSION_POINTS.md` (R-8).
- [ ] T007 [P] Add `IJsonActivityCatalogWriter` and `JsonActivityCatalogWriter` in
  `essentials/src/Activities/Design/Reconciliation/Json/`, with a round-trip test against `JsonActivityCatalogReader`.
  Update its README and `EXTENSION_POINTS.md` (R-8).
- [ ] T008 Add `Tooling/Elsa3MigrationToolingHost.cs` with the frozen `RunAsync(Stream, Stream)` and the request and
  response records of [contracts/worker-entry-point.md](contracts/worker-entry-point.md), including the unknown
  `contractVersion` refusal. Test the contract parsing.
- [ ] T009 Add `Tooling/MigrationComposition.cs` (R-4, R-5). It builds an unstarted generic host with one shell from
  the closure's feature list and the merged settings, sets every enabled EF feature to SQLite on
  `file:elsa3-migration-<guid>;Mode=Memory;Cache=Shared`, sets the migrate policy to apply, keeps one connection open,
  and disposes everything on every path. Tests: two EF modules commit in one shared transaction without
  `EfSharedTransactionTargetMismatchException`; no `IHostedService` or startup task runs; no file appears outside a
  temporary `--out` (FR-006 to FR-008, SC-003).
- [ ] T010 Add the `elsa3-migration` command family to `essentials/src/Cli/Worker/WorkerContract.cs`, and the
  second entry point resolution by name to `ToolingEntryPoint.cs`, with the `host-entry-point-missing` refusal naming the
  minimum version. Test it with a fake host layout that lacks the assembly.
- [ ] T011 Add `essentials/src/Cli/Elsa.Cli/Elsa3MigrationCommands.cs`: the two commands, the argument rules and
  refusal reasons of [contracts/cli.md](contracts/cli.md), the per-group exit mapping (FR-004), and the printed output
  path (FR-005). Register the group in `ElsaCli.cs`. Test the argument combinations and exit mapping with the existing
  CLI test style. Confirm `Elsa3MigrationBoundaryTests` stays green.
- [ ] T012 Add `extensions/Elsa3/src/Migration/ReferenceHost/`. Its composition is the specification 1236 feature list
  plus the Elsa 3 converter, import, reconciliation and tooling features. Pack its publish output, with the
  runtimeconfig and deps files, as `Elsa.Elsa3Migration.ReferenceHost` at the tool's version, and check that
  `.github/workflows/packages.yml` packs it.
- [ ] T013 Add `essentials/src/Cli/Elsa.Cli/ReferenceHostRestore.cs`. It restores the pinned package id and version
  into the NuGet global packages folder through a generated temporary project, reuses a cached copy without network
  access, and refuses with `reference-host-unavailable` otherwise. Test the cached, offline-missing and version-pinned
  paths (FR-003).

## Phase 3: User Story 1 - Inventory an export (P1)

- [ ] T014 [US1] Add `Tooling/ExportInputReader.cs`: a folder (recursive `*.json`, ordinal order) or zip; one
  definition or an array per file; per-file unreadable input as an input finding; the `input-missing`, `input-empty`
  and `input-unreadable` refusals (FR-009, edge cases). Test it with the fixture.
- [ ] T015 [US1] Add `Tooling/VersionSelection.cs`: the latest published version plus pinned reusable versions, the
  not-converted list, `--all-versions`, and the Info finding for a definition with no published version (FR-010). Test
  each fixture case.
- [ ] T016 [US1] Wire the entry point: read, select, run `Elsa3ImportRun`, and collect every diagnostic with its stage.
  Publish stops at the first error and marks `publishStoppedHere` (FR-011, FR-012).
- [ ] T017 [US1] Add `Tooling/ReportBuilder.cs`: categories (FR-013), remedies and component kinds from the code table
  (FR-014), buckets and blocked-by (FR-015), all sections of FR-016 (summary, components with usage counts,
  definitions, not converted, missing targets, input findings, run), and the ordering rules of data-model.md.
  Unit-test the bucket rule and the category mapping table-driven, plus the component inventory (no unused component,
  correct counts; SC-001).
- [ ] T018 [US1] Detect dispatched and executed workflow targets missing from the export, from the converted
  definitions' dispatch and execute-workflow activities, as Take into account (FR-016 item 5). Test it with the
  fixture.
- [ ] T019 [US1] Add a golden-file integration test in `MTests/Tests/`: the fixture export → `inventory` in-process →
  `report.json` equals the committed golden file, and the exit code is 1. A second run is byte-identical, including
  `migrated/` (FR-020, SC-006).

## Phase 4: User Story 3 - Migrated content (P1)

- [ ] T020 [US3] Add `Tooling/MigratedExporter.cs`. It reads the published workflow versions and reusable activity
  versions back from the design stores and writes them through the T006 and T007 writers: one file per workflow
  definition, and one activities file in dependency order. It sets `exportedTo` and `needsCompositeSourceSupport`
  (FR-021, FR-022). Refused and blocked definitions are absent. Test the layout and the absence.
- [ ] T021 [US3] Add the output swap: write to a staging folder beside `--out`, replace `report.json`, `report.html` and
  `migrated/` last, and leave other files untouched. A failed run leaves the previous output intact. Test both paths.
- [ ] T022 [US3] Add the loadability test: compose a fresh reference shell with JSON workflow and activity
  reconciliation over the fixture run's `migrated/`, and assert that every exported workflow loads (SC-004). The
  composite-with-dependencies case is asserted as skipped with `activity.source.dependencies-unsupported` until the
  spec Q2 gap is fixed, then flipped by T031.

## Phase 5: User Story 2 - Assess with my own features (P1)

- [ ] T023 [US2] Extend `essentials/src/Cli/Worker/NuplanePackageSet.cs` (or a sibling) with the run-scoped
  directory-feed install root for `--packages`. Resolution is restricted to the folder and the closure, the root is
  deleted on every path, and an unresolvable dependency is the `package-unresolved` refusal (R-6). Test with the T003
  package and with a package whose dependency is missing.
- [ ] T024 [US2] Support `--host`: check the host layout (`host-layout`), resolve the entry point (T010), and record
  `hostAssemblyVersion` in the report. Test with a fake host layout.
- [ ] T025 [US2] Merge `--settings` as specification 1236 defines. Record setting keys only, and set
  `persistenceOverridden` when a persistence connection setting was replaced (FR-007, FR-016 item 4). Test that no
  setting value reaches the report.
- [ ] T026 [US2] Add the assess integration test: the fixture export + the T003 package + its settings → exactly the
  findings for the custom activity, the function and the variable type are gone. Every other finding is unchanged
  (SC-007).

## Phase 6: User Story 4 - Redacted, versioned report (P2)

- [ ] T027 [US4] Make every converter, import and reconciliation message and guidance free of input text (FR-017).
  Start with the strict-mode parse message, and find the others with the sentinel test (T028). Change the message so
  that it names the cause without quoting input, and add no workaround (FR-024).
- [ ] T028 [US4] Add the sentinel test: the fixture run's `report.json` and `report.html` contain no
  `SENTINEL-1237` (SC-005).
- [ ] T029 [US4] Add the schema test: every report the tests produce validates against
  [contracts/report.schema.json](contracts/report.schema.json). Copy the schema into `Tooling/` as an embedded
  resource, and keep it identical to the contract file (FR-018).
- [ ] T030 [US4] Add `Tooling/ReportHtml.cs` and the embedded `report.html` template. It inlines the escaped JSON,
  renders all sections grouped by bucket and category, and works offline (FR-019). Test that the page holds the same
  JSON, that no external URL appears, and that script-breaking strings are escaped.

## Phase 7: Polish and acceptance

- [ ] T031 Once the spec Q2 engine gap is fixed on the initiative branch, flip T022's composite case to assert that
  it loads (SC-004).
- [ ] T032 Run `inventory` over the S-15 acceptance set (34 definitions) in the reference closure. Check that every
  definition has one bucket and that the published set equals what startup reconciliation publishes in the same
  closure (SC-002). Record the result in the PR evidence, and check the two-minute goal.
- [ ] T033 [P] Write `Tooling/README.md`: the commands, the entry point contract, the reference host, the database
  rule and redaction. Link it from `extensions/Elsa3/src/Mapping/README.md` and `essentials/src/Cli/Elsa.Cli/README.md`.
- [ ] T034 [P] Add the packed-tool smoke test: install the packed `dotnet-elsa` and the reference host package from a
  local feed, run `inventory` on the fixture, and expect exit 1 and the printed path. Then rerun with every NuGet
  source removed: it still succeeds from the cache, so nothing beyond the first-use restore needs the network (FR-003,
  FR-023).
- [ ] T035 Set this specification's status to Implemented in the finishing PR.
