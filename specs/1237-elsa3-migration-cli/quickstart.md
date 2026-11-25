# Quickstart: Elsa 3 workflows migration CLI

Docker is a prerequisite. The migration skill normally starts these commands for you. The image tag below stands for
the Elsa 3 migration image at the version of the essentials images you will go live on.

## Inventory

```bash
docker run --rm --network none \
  -v "$PWD/my-elsa3-export:/app/export:ro" -v "$PWD/migration:/app/out" \
  elsaworkflows/elsa-elsa3-migration:<essentials tag> workflows inventory /app/export
```

The last line printed is the output path. Open `./migration/report.html`, or read `./migration/report.json`.

- Exit code 0: nothing to fix or reconsider.
- Exit code 1: the report has findings.
- Exit code 2: the tool refused to run, and standard error says why.

## Assess with your own features

```bash
docker run --rm --network none \
  -v "$PWD/my-elsa3-export:/app/export:ro" -v "$PWD/nupkgs:/app/packages:ro" \
  -v "$PWD/appsettings.json:/app/config/appsettings.json:ro" -v "$PWD/migration:/app/out" \
  elsaworkflows/elsa-elsa3-migration:<essentials tag> workflows assess /app/export \
  --packages /app/packages --settings /app/config/appsettings.json
```

`./nupkgs` holds your Elsa 4 feature packages. `appsettings.json` enables them, exactly as you would configure the
essentials engine image. Fix, rebuild the packages and rerun until every remaining finding is clean or accepted.

## Go live (outside the tool)

Start the essentials engine image with your `appsettings.json`, your packages, and `./migration/migrated/` mounted at
`/app/migrated`. The engine imports `workflows/` and `activities/activities.json` from there at startup.

## Validation scenarios (for implementers)

1. Fixture export → `inventory` → the expected `report.json` (golden file) and `migrated/` layout. A second run is
   byte-identical.
2. Sentinel fixture: no sentinel appears in `report.json` or `report.html`.
3. Fixture feature package → `assess` clears exactly its findings; a value-only read of
   its function becomes `ELS3-REPLACED-FUNCTION-REFERENCE` (SC-007).
4. Specification 1235 acceptance set → `inventory --all-versions` in the image: every definition gets a bucket, and the published set equals
   startup reconciliation's.
5. The essentials engine, with JSON reconciliation, loads every workflow in `migrated/workflows/` that uses no reusable.
6. A fixture feature that throws during composition makes the run fail part-way. No database or temporary install root
   remains, and the previous `--out` is intact.
7. `docker run --network none` gives the same output as the in-process run.
