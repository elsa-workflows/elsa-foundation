# Quickstart: Elsa 3 workflows migration CLI

The CLI runs from a clone of this repository, next to the Elsa 3 migration host of specification 1236. You need the
.NET SDK the repository pins. The migration skill normally starts these commands for you. Run them from the
repository root.

## Inventory

```bash
dotnet run --project extensions/Elsa3/src/Migration/Cli -- workflows inventory ../my-elsa3-export --out ../migration
```

The last line printed is the output path. Open `../migration/report.html`, or read `../migration/report.json`.

- Exit code 0: nothing to fix or reconsider.
- Exit code 1: the report has findings.
- Exit code 2: the tool refused to run, and standard error says why.

`inventory` runs on the migration host's committed composition plus the Elsa 3 additions. It ignores your local
settings.

## Assess with your own features

1. Add your Elsa 4 feature project as a project reference to `apps/Elsa.Migration.Host`.
2. List its feature in the host's `shells.Local.json` (or in user secrets), with any settings it needs.
3. Rerun:

```bash
dotnet run --project extensions/Elsa3/src/Migration/Cli -- workflows assess ../my-elsa3-export --out ../migration
```

`assess` composes exactly what your migration host runs, plus the Elsa 3 additions. Fix, rebuild and rerun until
every remaining finding is clean or accepted. You can set a breakpoint in your feature and debug the CLI from the IDE.

## Go live (outside the tool)

Start the migration host with its two folder settings pointing at your `migrated/` folder:

```bash
dotnet run --project apps/Elsa.Migration.Host -- \
  --CShells:Shells:default:Features:JsonWorkflowReconciliation:Options:FolderPath=<absolute path>/migration/migrated/workflows \
  --CShells:Shells:default:Features:JsonActivityReconciliation:Options:FilePath=<absolute path>/migration/migrated/activities/activities.json
```

You can set the same two settings in `shells.Local.json` instead. The host imports `workflows/` and
`activities/activities.json` at startup. Its README names the two settings.

## Validation scenarios (for implementers)

1. Fixture export → `inventory` → the expected `report.json` (golden file) and `migrated/` layout. A second run is
   byte-identical.
2. Sentinel fixture: no sentinel appears in `report.json` or `report.html`.
3. Fixture feature project → `assess` clears exactly its findings; a value-only read of
   its function becomes `ELS3-REPLACED-FUNCTION-REFERENCE`, and the function leaves `components` (SC-007).
4. Specification 1235 acceptance set → `inventory --all-versions` from the clone: every definition gets a bucket, and
   the published set equals startup reconciliation's.
5. The migration host, with JSON reconciliation, loads every workflow and reusable in `migrated/`.
6. A fixture feature that throws during composition refuses the run with `composition-failed`. No database file or
   temporary folder remains, and the previous `--out` is intact.
7. `dotnet run --project extensions/Elsa3/src/Migration/Cli` from a clean build gives the same output as the
   in-process run.
