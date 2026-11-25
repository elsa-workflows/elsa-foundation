# Data Model: Elsa 3 migration host

No persisted entity is added. The host stores what its composed features already store. The entities below are
files and records this feature defines.

## Host composition

- **Where**: `apps/Elsa.Migration.Host/shells.json`, under `CShells:Shells:default:Features`.
- **Fields per feature**: the CShells feature name (the key) and its settings (the value, often `{}`).
- **Reason**: one entry per feature in `composition-reasons.json` next to `shells.json` (feature name → reason), shown
  by the README. The reason is one
  of the following: a JTest suite or template; "Studio designer"; "migrated folder"; or "required by <feature>".
- **Rule**: the set of features in `shells.json` equals the set of keys in `composition-reasons.json` (FR-004, R-5).

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
| Run | The next run number in the ledger |
| Engine | `Elsa 4` |
| Commit | The initiative head the host was built from |
| Source | `Elsa.Migration.Host`, the migration CLI version that wrote `migrated/`, and whether spec 1207 is implemented on the commit |
| Environment file | `environments/elsa4.json`, with its variables set as R-10 lists |
| jtest, Scope, Result, Receipt | As spec 1235 defines them |
| Notes | The Workbench run on the same commit it is compared with, and the API and executor URLs and user name used |

No secret values. A failed run stays recorded.
