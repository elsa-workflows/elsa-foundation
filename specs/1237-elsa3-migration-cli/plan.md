# Implementation Plan: Elsa 3 workflows migration CLI

**Branch**: `agent/claude/3052-spec-migration-cli` | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/1237-elsa3-migration-cli/spec.md`

## Summary

Add a console project, `Elsa3.Migration.Cli`, at `extensions/Elsa3/src/Migration/Cli/`, that engineers run from their
clone with `dotnet run`. It references the Elsa 3 migration host of specification 1236 (`apps/Elsa.Migration.Host`),
so every feature the host composes, the engineer's own included, is in its build, plus the Elsa 3 projects of the
addition list. It is a thin entry over a new tooling library, `Elsa3.Migration.Tooling`, in
`extensions/Elsa3/src/Migration/Tooling/`. The tooling:

1. composes one shell without a web host, from the host's configuration (committed only for `inventory`, with the
   engineer's local sources for `assess`) plus the Elsa 3 addition list, on one in-memory SQLite database;
2. runs the Elsa 3 startup reconciliation sequence, extracted into a shared service;
3. exports the published content from the store into `migrated/`, through new JSON reconciliation writers;
4. writes a redacted, versioned `report.json` and a self-contained `report.html`.

## Technical Context

**Language/Version**: C# / .NET 10.

**Primary Dependencies**: System.CommandLine; the migration host's two public wiring methods (configuration layers
and root services, including CShells with `WithHostAssemblies()`), extracted from its `CreateBuilder` by T004a; the
Elsa 3 converter, import and reconciliation projects; JSON workflow and activity reconciliation; EF Core SQLite; the
migration host project (specification 1236).

**Storage**: One in-memory shared-cache SQLite database per run (R-4). There is no other storage.

**Testing**: xunit.

- Unit tests: the report builder, the code table and the bucket rule.
- Integration tests: run the tooling in-process over fixture exports, on the host's composition plus the addition list,
  or on a test-only host composition set through the tooling's internal seam.
- CLI tests: argument rules, refusal reasons and exit codes.
- Smoke test: `dotnet run --project extensions/Elsa3/src/Migration/Cli` from a clean build on the fixture export.

**Target Platform**: wherever the repository builds; run with `dotnet run` from the clone.

**Project Type**: a console project and a tooling library.

**Performance Goals**: The 34-definition specification 1235 acceptance set completes in under two minutes on a developer machine.

**Constraints**:

- No network access, and nothing persists outside `--out`.
- The output is deterministic, and the report is redacted.
- The CLI references the host, never the other way round, so the host carries no Elsa 3 assembly (1236 FR-003).
- The CLI starts no web host, although it carries the ASP.NET Core shared framework through the host.

**Scale/Scope**: Exports of hundreds of definitions. The fixture export has about 15 definitions, and the specification 1235 acceptance set 34.

## Constitution Check

*Gate before research, re-checked after design: no change.*

| Gate | Status | Notes |
|---|---|---|
| §E2.7 Elsa 3 stays behind the import boundary | PASS | The CLI project, the tooling, the converter and the addition list are under `extensions/Elsa3/`. `essentials/src` gains no reference containing `Elsa3`. The CLI references the migration host; the host references no Elsa 3 project. The boundary tests stay green (R-7). |
| §E2.2 Runtime takes no Design reference | PASS | No runtime project changes. The CLI is a composition root, as a host is. |
| §2.2 project paths follow the domain tree | PASS | `Elsa3.Migration.Cli` and `Elsa3.Migration.Tooling` derive their paths from their names under the `Elsa3` extension bucket; no `ExpectedProjectPath` entry is needed (R-7). |
| Framework gate §2.20 "Only admitted projects resolve EF Core packages"; ADR 0073 | PASS | The CLI is admitted in `EfCoreDependencyGuardTests` as a composition root with a reviewed package array; the tooling and the migration test project get reviewed closures next to `Adr0073Elsa3ImportEf` (R-7, T001). |
| ADR 0080 internal by default | PASS | The shared import run stays internal (`InternalsVisibleTo` for the tooling assembly), and so does the CLI's test seam. The only public additions are the two writer contracts beside their reader contracts, the startup-only member on `ITaskManager` with the Tasks `StartupTasksOnly` setting, the tooling's `Elsa3MigrationImportRun` feature, and the migration host's two extracted wiring methods (T004a). |
| §2.24.2 sanctioned patterns | PASS | R-10. There is no new seam and no dry-run abstraction: the real pipeline runs on a disposable store. |
| §2.21.1 refactoring keeps behaviour | PASS | Extracting `Elsa3ImportRun` keeps the startup task's log lines and skip behaviour, and the existing reconciliation integration tests cover it. |
| §2.23 risk-based tests | PASS | Risk sits in redaction, determinism, the bucket rule, code-table completeness, the single-database rule and the composition parity with the host. Each has a test (tasks). |
| §2.22 feature documentation | PASS | The reconciliation READMEs document the new writers, and a tooling README documents the CLI. |
| §E7 CI test tiers | PASS | New test projects follow the existing Elsa 3 test placement, and the solution filters are regenerated. |
| xunit-only test rule | PASS | xunit only. |
| Studio | PASS | No Studio surface. |
| Refuse over workaround | PASS | FR-026. Converter message changes only remove input text, and the CLI scans no scripts. |

## Decisions

| # | Decision | Detail |
|---|---|---|
| P-1 | Console project | R-1: `Elsa3.Migration.Cli`, a thin System.CommandLine entry over the tooling, run with `dotnet run`; exit codes per FR-004 and [contracts/cli.md](contracts/cli.md). |
| P-2 | Code closure | R-1: project references to `apps/Elsa.Migration.Host` and to the tooling, which references the Elsa 3 projects of the addition list. The engineer adds a feature project once, to the host. |
| P-3 | Shared pipeline | R-2: `Elsa3ImportRun`, extracted from `Elsa3FolderImportTask`. |
| P-4 | Code table | R-3: `Elsa3MigrationCodeTable` in the tooling project, which references every project that holds code constants, with a completeness test in the migration tests. It also holds the fixed texts for codes the converter does not own. |
| P-5 | Database | R-4: one named in-memory shared-cache database, held open for the run. |
| P-6 | Composition | R-5: an empty generic host (`Host.CreateEmptyApplicationBuilder`, host folder as content root, Development) that is never started. In order: the migration host's `MigrationHost.AddHostConfiguration` (extracted by T004a) through a key filter for the reconciliation path keys; the addition list from the CLI project folder; the run-override layer (in-memory SQLite, empty reconciliation inputs in a temporary folder outside `--out`, the Tasks `StartupTasksOnly` setting); `MigrationHost.AddHostServices`; then Data Protection replaced with an ephemeral key ring. The tooling's shell feature `Elsa3MigrationImportRun` registers the import run inside the shell. No background or recurring task or hosted service starts. Rejected: replicating the host wiring, and building a `WebApplication` through `MigrationHost.CreateBuilder`. |
| P-7 | The engineer's features | R-6: project references on the migration host; nothing is loaded at run time. |
| P-8 | Architecture guards | R-7: EF admissions for the CLI, the tooling and the test project, and the CLI as a build-only reference of the architecture tests; no path, extension-boundary or Elsa 3 boundary entry. |
| P-9 | Migrated export | R-8: writers next to the readers, reading back from the design stores. |
| P-10 | HTML | R-9: an embedded template with the JSON inline. |
| P-11 | Output swap | data-model: a hidden staging folder inside `--out`, renamed into place last. |

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
│   └── report.schema.json
├── checklists/requirements.md
└── tasks.md
```

### Source Code (repository root)

```text
essentials/src/Tasks/
├── Core/ITaskManager.cs                        # + startup-only member
├── Elsa.Tasks/TasksFeature.cs                  # + StartupTasksOnly setting
├── Elsa.Tasks/Services/RunShellTasksInitializer.cs  # reads it
└── Elsa.Tasks/Services/TaskManager.cs          # implements the member, keeping the dependency order

apps/Elsa.Migration.Host/
└── MigrationHost.cs                            # + AddHostConfiguration, AddHostServices, extracted from CreateBuilder (T004a)

essentials/src/Workflows/Design/Reconciliation/Json/
├── Contracts/IJsonWorkflowCatalogWriter.cs     # + writer contract
└── Services/JsonWorkflowCatalogWriter.cs       # + writer

essentials/src/Activities/Design/Reconciliation/Json/
├── Contracts/IJsonActivityCatalogWriter.cs     # + writer contract
└── Services/JsonActivityCatalogWriter.cs       # + writer

extensions/Elsa3/src/Activities/Design/Import/Reconciliation/Services/
├── Elsa3ImportRun.cs                           # + shared sequence (extracted)
├── Elsa3ImportRunRegistration.cs               # + shared service registration (extracted)
└── Elsa3FolderImportTask.cs                    # now calls Elsa3ImportRun

extensions/Elsa3/src/Migration/Cli/             # Elsa3.Migration.Cli (console; references apps/Elsa.Migration.Host)
├── Elsa3.Migration.Cli.csproj                  # writes the host project folder as assembly metadata
├── Program.cs                                  # workflows inventory, assess; argument rules; exit codes
└── elsa3-migration.features.json               # the Elsa 3 addition list

extensions/Elsa3/src/Migration/Tooling/         # Elsa3.Migration.Tooling
├── Elsa3MigrationRun.cs                        # the pipeline the CLI calls
├── Elsa3MigrationImportRunFeature.cs           # shell feature: registers the import run inside the shell
├── Elsa3MigrationCodeTable.cs                  # code → remedy, component kind, fixed text
├── MigrationComposition.cs
├── ExportInputReader.cs
├── VersionSelection.cs
├── ReportBuilder.cs
├── MigratedExporter.cs
├── ReportHtml.cs + report.html (embedded)
└── README.md

extensions/Elsa3/tests/Migration/
├── Tests/                                      # unit + in-process integration
└── Fixtures/export/                            # fixture export (sentinel-marked)
```

**Structure Decision**: Elsa 3 code, the CLI and the addition list stay under `extensions/Elsa3/` (§E2.7). The
migration host stays free of Elsa 3; the CLI references it. The format writers live with their readers, so each format
has one owner.

## Complexity Tracking

| Item | Why | Simpler alternative rejected because |
|---|---|---|
| The CLI references an `apps/` host | The test migration must run on the composition the engineer's host runs, with the engineer's features added in one place | Referencing the feature projects from the CLI as well means two places to keep in sync; a container image needs an engine image that 1236 no longer plans |
| EF admission for the CLI | It resolves the host's EF closure | None: every project that resolves EF is admitted with a reviewed closure |
