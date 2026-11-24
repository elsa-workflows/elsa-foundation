# Contract: command line and report compatibility

## Commands

```text
dotnet elsa elsa3-migration workflows inventory <export> [--out <dir>] [--all-versions]
dotnet elsa elsa3-migration workflows assess <export> --packages <dir> --settings <file> [--out <dir>] [--all-versions]
dotnet elsa elsa3-migration workflows assess <export> --host <dir> [--settings <file>] [--out <dir>] [--all-versions]
```

| Argument | Rule |
|---|---|
| `<export>` | A folder (searched recursively for `*.json`) or a `.zip`. Anything else exits 2. |
| `--out` | Default `./elsa3-migration`, created if it is missing. Only `report.json`, `report.html` and `migrated/` are replaced. |
| `--all-versions` | Converts every version instead of the FR-010 selection. |
| `--packages` | A folder of `.nupkg` files, read as a directory feed. `--settings` is required with it. It cannot be combined with `--host`. |
| `--settings` | An `appsettings.json`, merged as the essentials engine image merges it. |
| `--host` | An Elsa 4 host build output that has a `.runtimeconfig.json` and a `.deps.json`, and carries the Elsa 3 migration tooling assembly. |

## Exit codes

| Code | Meaning |
|---|---|
| 0 | The report was written and has no `mustFix` or `reconsider` finding. |
| 1 | The report was written and has at least one `mustFix` or `reconsider` finding. |
| 2 | Refused to run. No report is written, and the previous output is untouched. |

## Standard output

On exit 0 or 1, the last line is the absolute path of `--out`. On exit 2, standard error carries one refusal line:
`elsa3-migration: refused: <reason>`, with a reason code from the table below and a sentence.

| Reason code | When |
|---|---|
| `input-missing` | `<export>` does not exist or is neither a folder nor a zip. |
| `input-empty` | No file holds an Elsa 3 definition. |
| `input-unreadable` | The zip cannot be opened. |
| `arguments` | An invalid combination of options. |
| `reference-host-unavailable` | The pinned reference host package is neither cached nor restorable. |
| `host-entry-point-missing` | `--host` does not carry the migration entry point. The reason names the minimum version. |
| `host-layout` | `--host` lacks `.runtimeconfig.json` or `.deps.json`. |
| `package-unresolved` | A package dependency is in neither `--packages` nor the closure. The reason names the id and range. |
| `settings-unreadable` | `--settings` is missing or is not valid JSON. |
| `composition-failed` | The shell could not be composed (a feature failed to configure). The reason names the feature. |

## Report compatibility

- `schemaVersion` is an integer. Version 1 is [report.schema.json](report.schema.json).
- A consumer MUST refuse a `schemaVersion` it does not know.
- A consumer MUST ignore properties it does not know. Adding a property does not change `schemaVersion`. Removing,
  renaming, or changing the meaning of a property or an enum value does.
- Codes, messages and guidance come from the converter and from Elsa 4. A consumer MUST NOT hard-code their meaning.
  It reads `category`, `remedy` and `componentKind`.
