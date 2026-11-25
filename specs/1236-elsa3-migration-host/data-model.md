# Data Model: Elsa 3 migration host

No persisted entity is added. The host stores what its composed features already store. The entities below are
files and records this feature defines.

## Host composition

- **Where**: `extensions/Elsa3/src/Migration/Host/shells.json`, under `CShells:Shells:default:Features`.
- **Fields per feature**: the CShells feature name (the key) and its settings (the value, often `{}`).
- **Reason**: one row per feature in the README's composition table, with columns feature and reason. The reason is one
  of the following: a JTest suite or template; "Studio designer"; "migrated folder"; or "required by <feature>".
- **Rule**: the set of features in `shells.json` equals the set of rows in the table (FR-004, R-5).

## Migrated folder

- **Workflows folder**: a flat folder of JSON files in the JSON workflow reconciliation format (an array of workflow
  version models per file). Subfolders are ignored.
- **Activities file**: one JSON file in the JSON activity reconciliation format. `[]` when there are no reusables.
- **Settings**:
  - `CShells:Shells:default:Features:JsonWorkflowReconciliation:Options:FolderPath`;
  - `CShells:Shells:default:Features:JsonActivityReconciliation:Options:FilePath`.

  Both default to the sample folder in the host project.

## Acceptance run record

A row in `extensions/Elsa3/tests/JTest/ledger/runs.md`, next to the S-15 rows:

| Field | Meaning |
|---|---|
| Date | When the run happened |
| Engine | `migration-host` |
| Commit | The initiative head the host was built from |
| CLI version | The migration CLI version that wrote `migrated/` |
| jtest version | The `jtest` tool version |
| Result per suite | Pass, fail, or exempt with its S-15 reason |
| Compared with | The S-15 Workbench run on the same commit |

No secret values. A failed run stays recorded.
