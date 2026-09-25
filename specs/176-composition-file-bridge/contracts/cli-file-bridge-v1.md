# CLI file bridge v1

This command contract implements the [semantic file bridge v1 contract](file-bridge-v1.md). `composition import` is the first implemented checkpoint; `composition generate` remains planned. `composition plan` remains a separate, read-only command.

```text
dotnet elsa composition import
  --host-dir <directory> --shell <id> --environment <name>
  --catalog <catalog.json> [--setting-review <review.json>]
  --output <new-composition.json>

dotnet elsa composition generate
  --host-dir <directory> --shell <id> --environment <name>
  --catalog <catalog.json> --composition <accepted-composition.json>
  [--setting-review <review.json>] --output-dir <new-directory>
```

`--host-dir` is a file source, not the persistence worker's runtime host argument. No package root, connection, restore, migration, or save option is accepted. Both commands require an interactive terminal for the review decision. The import command displays a redacted selected-file preview and shared planner findings, then asks for explicit acceptance before writing an authored file. The generate command reopens the source bundle, validates the accepted document/catalog pin, displays a redacted semantic diff, and asks for a separate approval before writing a candidate directory. A noninteractive call or cancellation returns `bridge-review-required`; there is no `--yes` option. The review input is the [local setting review v1 file](setting-review-v1.md), not an instruction to copy unknown fields.

The source layout is the root-level Workbench-style base `shells.json` and `appsettings.json`, plus `shells.<Environment>.json` and `appsettings.<Environment>.json` when present. An explicitly named environment whose shell overlay is absent refuses. Every supported sibling shell/appsettings environment file is copied, frozen, and rechecked, even if it is not inspected for selected effective values. Symlinks, path escapes, duplicates, and unsupported layer shapes refuse. An import preview computes effective values over selected files only; process environment, command-line overrides, and unselected overlays remain unchecked.

Normal stdout shows only safe feature IDs, reviewed values, masked identities/types, logical resource names, provenance labels, redacted planner findings, and the outcome. Prompts and refusals use stderr. Neither stream includes source path strings, connection values, raw unknown values, exception messages, or full source excerpts. A successful import creates one fresh, strict authored v1 file. A successful generate creates one fresh candidate directory with supported files; it does not modify the selected host. `--output` and `--output-dir` must not exist or overlap the source; all writes use private staging and no-overwrite publication. The authored and generated files have separate review decisions and are atomic independently.

| Result | Exit | Published output |
|---|---:|---|
| Explicitly accepted import or generation | 0 | One new authored file or one new candidate directory. |
| Missing review, cancellation, malformed/duplicate source, unsafe portable field, or unresolved reviewed mapping | 2 | None. Stable `bridge-*` refusal code on stderr. |
| Missing/unreadable/changed source, existing/overlapping destination, or output preparation failure | 3 | None. Stable `bridge-*` refusal code on stderr. |

The stable refusal codes and their triggers are enumerated in [file bridge v1](file-bridge-v1.md#failure-and-publication-boundary). An unexpected implementation error must still avoid raw exception text or partial output. A changed snapshot requires a new preview, never silent retry against new source bytes. The CLI may use the existing `CliRefusal`/`ToolExitCode` conventions, but must not use the persistence worker for these operations.
