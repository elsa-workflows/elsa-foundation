# Contract: command line and report compatibility

## Commands

The commands run from a clone of this repository, through the console project `Elsa3.Migration.Cli`:

```text
dotnet run --project extensions/Elsa3/src/Migration/Cli -- workflows inventory <export> [--out <dir>] [--all-versions]
dotnet run --project extensions/Elsa3/src/Migration/Cli -- workflows assess <export> [--out <dir>] [--all-versions]
```

`inventory` composes the migration host's committed composition plus the Elsa 3 addition list. `assess` adds the
host's local configuration sources (`shells.Local.json`, user secrets, environment variables), in the host's order
(spec FR-008). Neither command takes a host, package or settings option: the engineer's own features come in as
project references on the migration host, and their settings through the host's configuration sources.

| Argument | Rule |
|---|---|
| `<export>` | A folder (searched recursively for `*.json`) or a `.zip`. Anything else exits 2. Relative paths resolve against the current directory. |
| `--out` | Default `elsa3-migration-out` in the current directory, created if it is missing. Only `report.json`, `report.html` and `migrated/` are replaced. |
| `--all-versions` | Converts every version instead of the FR-012 selection. |

## Exit codes

| Code | Meaning |
|---|---|
| 0 | The report was written and has no `mustFix` or `reconsider` finding. |
| 1 | The report was written and has at least one `mustFix` or `reconsider` finding. |
| 2 | Refused to run. No report is written, and the previous output is untouched. |

## Standard output

On exit 0 or 1, the last line is the output path as it was given (the default if none was given). On exit 2,
standard error carries one refusal line, `elsa3-migration: refused: <reason>`, with a reason code from the table below
and a sentence. The report is written only on exit 0 or 1.

| Reason code | When |
|---|---|
| `arguments` | An unknown command or option, or an invalid combination of options. |
| `input-missing` | `<export>` does not exist, or is neither a folder nor a zip. |
| `input-empty` | No file holds an Elsa 3 definition. |
| `input-unreadable` | The zip cannot be opened. |
| `composition-failed` | A listed feature that the CLI's build does not provide, or a feature that throws while the shell is composed. The reason names the feature or the setting key, never a value. |

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
