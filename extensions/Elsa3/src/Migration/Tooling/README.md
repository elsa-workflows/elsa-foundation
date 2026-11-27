# Elsa 3 workflows migration tooling

The pipeline of the Elsa 3 workflows migration CLI ([spec 1237](../../../../../specs/1237-elsa3-migration-cli/spec.md)).
It test-migrates an Elsa 3 export on the composition of the [Elsa 3 migration host](../../../../../apps/Elsa.Migration.Host/README.md),
writes a redacted report, and writes the published result as a `migrated/` folder the host loads. The console project
[`Elsa3.Migration.Cli`](../Cli) is its only caller; this library is never a package.

## Commands

Run from a clone, at the repository root:

```bash
dotnet run --project extensions/Elsa3/src/Migration/Cli -- workflows inventory <export> [--out <dir>] [--all-versions]
dotnet run --project extensions/Elsa3/src/Migration/Cli -- workflows assess <export> [--out <dir>] [--all-versions]
```

- `<export>` is an Elsa 3 export: a folder, searched recursively for `*.json`, or a `.zip`.
- `--out` defaults to `elsa3-migration-out` in the current directory. Only `report.json`, `report.html` and
  `migrated/` are replaced, and they are replaced as a whole: the run writes them to a hidden `.staging-<guid>` folder
  inside `--out` and moves them into place last.
- `--all-versions` converts every version. Without it, each definition converts its latest published version (its
  latest version when none is published), plus every version a reusable reference pins.

Exit codes: `0` the report has no Must fix or Reconsider finding, `1` it has, `2` the run was refused. On `0` and `1`
the last line printed is the output path; on `2` standard error carries one line,
`elsa3-migration: refused: <reason>: <sentence>`. The reasons and the report format are the contracts in
[contracts/cli.md](../../../../../specs/1237-elsa3-migration-cli/contracts/cli.md) and
[report.schema.json](report.schema.json).

| | `inventory` | `assess` |
|---|---|---|
| The host's `appsettings*.json`, `shells.json`, `shells.Development.json` | yes | yes |
| The Elsa 3 addition list, [`elsa3-migration.features.json`](../Cli/elsa3-migration.features.json) | yes | yes |
| The host's local sources: `shells.Local.json`, its user secrets, environment variables | no | yes |

Neither command takes the command line's arguments into the host's configuration, nor reads `appsettings.json` from
the current directory.

## Your own features (`assess`)

1. Add your Elsa 4 feature project as a project reference of `apps/Elsa.Migration.Host/Elsa.Migration.Host.csproj`.
   The CLI references the host, so your project is part of the CLI's build and its features are found the way the host
   finds them.
2. List the feature, with any settings it needs, in the host's git-ignored `shells.Local.json` or its user secrets.
3. Run `workflows assess`. A listed feature no project provides, or a feature that throws while the shell is composed,
   refuses the run with `composition-failed`, naming the feature.

The CLI composes in process, so you can set a breakpoint in your feature and debug the CLI.

## The database rule

The run never touches the host's database. Every EF module of the composition, the Elsa 3 import ledger included, uses
one in-memory SQLite database named for the run (`file:elsa3-migration-<guid>`, shared cache), so the import ledger and
the design stores commit in one transaction. The run holds the database open until it ends, then disposes it. Each
connection setting of the host is replaced for the run, which the report records as `run.persistenceOverridden`. The
empty reconciliation inputs and the file-system lock folder live in a temporary folder the run deletes, so nothing but
the output folder outlives a run.

The shell is composed on a generic host that is never started: its shell initializers run, the Tasks feature runs its
startup tasks only (`StartupTasksOnly`), and no web server, background task, recurring task, trigger or workflow
execution starts. Data Protection is replaced with an ephemeral key ring.

## Redaction

The report names identities only: definition ids and names, version ids, codes, paths, node ids, feature names and
component names (activity types, function names, variable types, storage drivers). It never copies a literal, a script,
a variable value or a setting value (`report.redacted` is always `true`):

- converter messages are written without input text;
- a code the converter does not own (an Elsa 4 publication code, for example) gets the code table's fixed text instead
  of its message;
- skips and run-level refusals are reported by their codes, never by their cause or exception message;
- `run.settingKeys` lists the keys under the host's shell section and `ConnectionStrings`, never their values.

## Layout

| File | Role |
|---|---|
| `Elsa3MigrationRun.cs` | The pipeline the CLI calls: read, select, compose, import, export, report, swap. |
| `MigrationComposition.cs` | The host's composition in process, with the run-override layer. |
| `Elsa3MigrationImportRunFeature.cs` | The shell feature the addition list enables: the shared import run and the catalog writers. |
| `ExportInputReader.cs`, `VersionSelection.cs`, `MissingTargetDetector.cs` | Input, version selection and missing dispatch targets. |
| `MigratedExporter.cs` | Writes `migrated/` from the published versions. |
| `Elsa3MigrationCodeTable.cs` | Remedy, component kind, routine flag and fixed text of every code. |
| `Report/` | The report model, builder, JSON and the offline HTML page. |
| `OutputSwap.cs` | Writes `--out` as a whole. |
