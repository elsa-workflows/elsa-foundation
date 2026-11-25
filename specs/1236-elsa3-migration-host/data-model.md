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

A row in `extensions/Elsa3/tests/JTest/ledger/runs.md`, in the S-15 ledger's existing columns (spec 1235
data-model.md):

| Column | Value for a migration-host run |
|---|---|
| Engine | `Elsa 4` |
| Commit | The initiative head the host was built from |
| Source | `Elsa3.Migration.Host`, the migration CLI version that wrote `migrated/`, and whether spec 1207 is implemented on the commit |
| Environment file, jtest, Scope, Result, Receipt | As spec 1235 defines them |
| Notes | The Workbench run on the same commit it is compared with |

No secret values. A failed run stays recorded.
