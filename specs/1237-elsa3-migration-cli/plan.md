# Implementation Plan: Elsa 3 workflows migration CLI

**Branch**: `agent/claude/3052-spec-migration-cli` | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/1237-elsa3-migration-cli/spec.md`

## Summary

Add an `elsa3-migration workflows` command group to `dotnet-elsa`, and an Elsa 3 migration image that runs it. The
image is built `FROM` the essentials engine image at the same version. It adds the Elsa 3 addition list and the tool,
and its entry point is the command group.

The group runs on the existing ADR 0076 worker inside the image's closure. It reaches a second frozen entry point,
`Elsa3MigrationToolingHost`, in a new `extensions/Elsa3/src/Migration/Tooling/` project. That entry point:

1. composes the shell without a web host, on one in-memory SQLite database;
2. runs the Elsa 3 startup reconciliation sequence, extracted into a shared service;
3. exports the published content from the store into `migrated/`, through new JSON reconciliation writers;
4. writes a redacted, versioned `report.json` and a self-contained `report.html`.

## Technical Context

**Language/Version**: C# / .NET 10; Dockerfile.

**Primary Dependencies**: System.CommandLine (existing front end); Nuplane (existing worker); the Elsa 3 converter,
import and reconciliation projects; JSON workflow and activity reconciliation (essentials); EF Core SQLite; the
essentials engine image (specification 1236).

**Storage**: One in-memory shared-cache SQLite database per run (R-4). There is no other storage.

**Testing**: xunit.

- Unit tests: the report builder, the code table and the bucket rule.
- Integration tests: run the entry point in-process over fixture exports, on a composition built from the essentials
  feature list plus the addition list.
- Front-end tests: the existing CLI style.
- Image smoke test: build the image and run it with networking disabled.

**Target Platform**: The migration image (Linux container), wherever Docker runs.

**Project Type**: CLI command group, a tooling library and a container image.

**Performance Goals**: The 34-definition S-15 acceptance set completes in under two minutes on a developer machine.

**Constraints**:

- No network access, and nothing persists outside `--out`.
- The output is deterministic, and the report is redacted.
- The front end must carry no reference whose name contains `Elsa3`
  (`Elsa3MigrationBoundaryTests.CanonicalElsaProjects_DoNotReferenceElsa3AssembliesOrDtos`), so it reaches the entry
  point only by name.
- The essentials engine image carries no Elsa 3 code.

**Scale/Scope**: Exports of hundreds of definitions. The fixture export has about 15 definitions, and the S-15 corpus 34.

## Constitution Check

*Gate before research, re-checked after design: no change.*

| Gate | Status | Notes |
|---|---|---|
| §E2.7 Elsa 3 stays behind the import boundary | PASS | All Elsa 3 code, the addition list and the Dockerfile are under `extensions/Elsa3/`. `essentials/src` gains no reference containing `Elsa3`, and the front end uses a type-name string only. The essentials engine image stays free of Elsa 3. The boundary tests stay green. |
| §E2.2 Runtime takes no Design reference | PASS | No runtime project changes. The tooling project is a composition root, as a host is. |
| ADR 0076 worker and frozen entry points | CONDITIONAL | Applies D1 to a second entry point. Depends on the ADR 0076 amendment (spec Q4). The group downloads nothing, so D10 is untouched. |
| ADR 0080 internal by default | PASS | The shared import run stays internal (`InternalsVisibleTo` for the tooling assembly). The only public additions are the entry point type and the two writer contracts beside their reader contracts. |
| §2.24.2 sanctioned patterns | PASS | R-10. There is no new seam and no dry-run abstraction: the real pipeline runs on a disposable store. |
| §2.21.1 refactoring keeps behaviour | PASS | Extracting `Elsa3ImportRun` keeps the startup task's log lines and skip behaviour, and the existing reconciliation integration tests cover it. |
| §2.23 risk-based tests | PASS | Risk sits in redaction, determinism, the bucket rule, code-table completeness, the single-database rule and the image composition. Each has a test (tasks). |
| §2.22 feature documentation | PASS | The reconciliation READMEs document the new writers, and a tooling README documents the entry point and the image. |
| §E7 CI test tiers | PASS | New test projects follow the existing Elsa 3 test placement, and the solution filters are regenerated. |
| xunit-only test rule | PASS | xunit only. |
| Studio | PASS | No Studio surface. |
| Refuse over workaround | PASS | FR-026. Converter message changes only remove input text, and the CLI scans no scripts. |

## Decisions

| # | Decision | Detail |
|---|---|---|
| P-1 | Command group | R-1: added to `ElsaCli.cs`. The front end maps exit codes per group (FR-004). |
| P-2 | Entry point | R-1 and [contracts/worker-entry-point.md](contracts/worker-entry-point.md). |
| P-3 | Shared pipeline | R-2: `Elsa3ImportRun`, extracted from `Elsa3FolderImportTask`. |
| P-4 | Code table | R-3: `Elsa3MigrationCodeTable` beside the code constants, with a completeness test. |
| P-5 | Database | R-4: one named in-memory shared-cache database, held open for the run. |
| P-6 | Composition | R-5: a generic host that is never started, with one shell, the merged settings and no hosted services. |
| P-7 | Packages | R-6: the image's addition folder and the user's folder are read as directory feeds into a run-scoped install root. A missing dependency refuses the run. |
| P-8 | Image | R-7: `extensions/Elsa3/docker/Dockerfile`, `FROM` the essentials engine image at the same version, plus the addition list, the tool and the entry point. |
| P-9 | Migrated export | R-8: writers next to the readers, reading back from the design stores. |
| P-10 | HTML | R-9: an embedded template with the JSON inline. |
| P-11 | Output swap | data-model: a staging folder, swapped in last. |

## Project Structure

### Documentation (this feature)

```text
specs/1237-elsa3-migration-cli/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── cli.md
│   ├── report.schema.json
│   └── worker-entry-point.md
├── checklists/requirements.md
└── tasks.md
```

### Source Code (repository root)

```text
essentials/src/Cli/Elsa.Cli/
├── ElsaCli.cs                                  # + elsa3-migration workflows group
└── Elsa3MigrationCommands.cs                   # inventory, assess; argument rules; exit mapping

essentials/src/Cli/Worker/
├── WorkerContract.cs                           # + elsa3-migration command family
├── ToolingEntryPoint.cs                        # + second entry point resolution by name
└── NuplanePackageSet.cs                        # + run-scoped directory-feed install root (R-6)

essentials/src/Workflows/Design/Reconciliation/Json/
├── Contracts/IJsonWorkflowCatalogWriter.cs     # + writer contract
└── Services/JsonWorkflowCatalogWriter.cs       # + writer

essentials/src/Activities/Design/Reconciliation/Json/
├── Contracts/IJsonActivityCatalogWriter.cs     # + writer contract
└── Services/JsonActivityCatalogWriter.cs       # + writer

extensions/Elsa3/src/Models/
└── Elsa3MigrationCodeTable.cs                  # + code → remedy, component kind

extensions/Elsa3/src/Activities/Design/Import/Reconciliation/Services/
├── Elsa3ImportRun.cs                           # + shared sequence (extracted)
└── Elsa3FolderImportTask.cs                    # now calls Elsa3ImportRun

extensions/Elsa3/src/Migration/Tooling/         # Elsa3.Migration.Tooling
├── Elsa3MigrationToolingHost.cs                # frozen entry point
├── MigrationComposition.cs
├── ExportInputReader.cs
├── VersionSelection.cs
├── ReportBuilder.cs
├── MigratedExporter.cs
├── ReportHtml.cs + report.html (embedded)
└── README.md

extensions/Elsa3/docker/
├── Dockerfile                                  # FROM essentials engine image; + additions, tool, entry point
└── elsa3-additions.json                        # the Elsa 3 addition list

extensions/Elsa3/tests/Migration/
├── Tests/                                      # unit + in-process integration
└── Fixtures/export/                            # fixture export (sentinel-marked)
```

**Structure Decision**: Elsa 3 code and the migration image stay under `extensions/Elsa3/` (§E2.7). The front end and
the worker change only through additions by name. The format writers live with their readers, so each format has one
owner.

## Complexity Tracking

| Item | Why | Simpler alternative rejected because |
|---|---|---|
| A second frozen entry point | Keeps Elsa 3 out of the persistence assembly and out of the front end | Routing through `EfToolingHost` would put Elsa 3 knowledge into essentials |
| A third image | The test migration must run on the engine the user goes live on, plus Elsa 3 | A separate closure (NuGet or bundled) could drift from the engine image |
