# Quickstart: Elsa 3 workflows migration CLI

## Inventory

```bash
dotnet tool install --global dotnet-elsa
dotnet elsa elsa3-migration workflows inventory ./my-elsa3-export --out ./migration
```

The first run restores the pinned reference host package into the NuGet cache. Later runs work offline. The last line
printed is the output path. Open `./migration/report.html`, or read `./migration/report.json`.

Exit code 0 means nothing to fix or reconsider. 1 means the report has findings. 2 means the tool refused to run, and
standard error says why.

## Assess with your own features

```bash
dotnet elsa elsa3-migration workflows assess ./my-elsa3-export \
  --packages ./nupkgs --settings ./appsettings.json --out ./migration
```

`./nupkgs` holds your Elsa 4 feature packages. `appsettings.json` enables them, exactly as you would configure the
essentials engine image. Fix, rebuild the packages, and rerun until every remaining finding is clean or accepted.

## Go live (outside the tool)

Mount `./migration/migrated/` into the essentials engine image, together with your `appsettings.json` and packages.
Point JSON workflow reconciliation at `migrated/workflows/`, and JSON activity reconciliation at
`migrated/activities/activities.json`. A reusable marked `needsCompositeSourceSupport` loads only on an engine that
supports source-owned composites with dependencies.

## Validation scenarios (for implementers)

1. Fixture export → `inventory` → the expected `report.json` (golden file) and `migrated/` layout. Run it twice and
   check the output is byte-identical.
2. The sentinel fixture: no sentinel in `report.json` or `report.html`.
3. The fixture feature package → `assess` clears exactly its findings.
4. The S-15 acceptance set → `inventory`: every definition is bucketed, and the published set equals startup
   reconciliation's.
5. A fresh reference composition loads `migrated/workflows/`.
6. Make the run fail mid-way (a fixture feature that throws during composition): no database file or temporary
   install root remains, and the previous `--out` is intact.
