# Contract: command line and report compatibility

## Commands

The commands run inside the Elsa 3 migration image, whose entry point is `dotnet-elsa elsa3-migration`.

```text
dotnet elsa elsa3-migration workflows inventory <export> [--out <dir>] [--all-versions]
dotnet elsa elsa3-migration workflows assess <export> --packages <dir> --settings <file> [--out <dir>] [--all-versions]
```

A typical start, with the paths mounted at the engine image's own mount points plus `/app/export` and `/app/out`.
The image is `elsaworkflows/elsa-elsa3-migration`, tagged like the essentials engine image it is built from:

```text
docker run --rm --network none \
  -v "$PWD/export:/app/export:ro" -v "$PWD/nupkgs:/app/packages:ro" \
  -v "$PWD/appsettings.json:/app/config/appsettings.json:ro" -v "$PWD/migration:/app/out" \
  elsaworkflows/elsa-elsa3-migration:<essentials tag> workflows assess /app/export --packages /app/packages \
  --settings /app/config/appsettings.json
```

| Argument | Rule |
|---|---|
| `<export>` | A folder (searched recursively for `*.json`) or a `.zip`. Anything else exits 2. |
| `--out` | Default `/app/out`, the documented output mount, created if it is missing. Only `report.json`, `report.html` and `migrated/` are replaced. |
| `--all-versions` | Converts every version instead of the FR-012 selection. |
| `--packages` | A folder of `.nupkg` files, read as a directory feed. Required for `assess`. Unlike the persistence group's `--packages` (an install root of extracted packages), it never names an install root. |
| `--settings` | An `appsettings.json`, merged as the essentials engine image merges it. Required for `assess`. |

The image sets `ELSA3_MIGRATION_HOST=/app` and `ELSA3_MIGRATION_IMAGE_VERSION=<essentials tag>`. The front end
resolves the host directory as the persistence group resolves `--host`. When the variable is unset or the directory has
no single `.runtimeconfig.json` and `.deps.json` pair, the run is refused with `entry-point-missing`.

## Exit codes

| Code | Meaning |
|---|---|
| 0 | The report was written and has no `mustFix` or `reconsider` finding. |
| 1 | The report was written and has at least one `mustFix` or `reconsider` finding. |
| 2 | Refused to run. No report is written, and the previous output is untouched. |

## Standard output

On exit 0 or 1, the last line is the output path as it was given (container path). On exit 2, standard error carries
one refusal line, `elsa3-migration: refused: <reason>`, with a reason code from the table below and a sentence.

| Reason code | When |
|---|---|
| `input-missing` | `<export>` does not exist, or is neither a folder nor a zip. |
| `input-empty` | No file holds an Elsa 3 definition. |
| `input-unreadable` | The zip cannot be opened. |
| `arguments` | An invalid combination of options. |
| `package-unresolved` | A package dependency is in neither `--packages` nor the image. The reason names the id and range. |
| `settings-unreadable` | `--settings` is missing, or is not valid JSON. |
| `entry-point-missing` | The migration tooling assembly or its entry point type is in neither the host closure nor the loaded package set (an image build defect). |
| `composition-failed` | A user package or setting the composition cannot boot. The reason names the feature, package or setting key, never a value. |

## Output layout

The layout under `--out` is fixed: `report.json`, `report.html`, `migrated/workflows/` and
`migrated/activities/activities.json`. Both `migrated/` entries always exist (FR-023). The report holds only paths
relative to `--out` (`exportedTo`). A consumer finds the folder from the `--out` it passed, or from the printed path.

## Report compatibility

- `schemaVersion` is an integer. Version 1 is defined by [report.schema.json](report.schema.json).
- A consumer MUST refuse a `schemaVersion` it does not know.
- A consumer MUST ignore properties it does not know. Adding a property does not change `schemaVersion`. Removing or
  renaming a property, or changing the meaning of a property or an enum value, does.
- Codes, messages and guidance come from the converter and from Elsa 4. A consumer MUST NOT hard-code their meaning.
  It reads `category`, `remedy` and `componentKind`.
