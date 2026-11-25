# Data model: Elsa 3 to Elsa 4 migration skill

No persisted data. The entities are files the user or the repository holds.

## Migration plan (user's workspace, markdown)

Filled in from `migration-plan-template.md` ([contract](contracts/plan-template.md)).

| Field | Content | Rule |
|---|---|---|
| Elsa 4 clone | Path and commit of the user's clone of this repository, from which the tool and the migration host run | Set at inventory; moving the clone to another commit means rerunning the assessment; the handoff's local run uses the same commit. |
| Stage | `inventory` → `prepare` → `test-migration` → `handoff` | Moves forward only; a new finding in `test-migration` sends one component back to `porting`, not the stage. |
| Export | Path of the Elsa 3 export | Required from `inventory`. |
| Source locations | Folders or repositories of Elsa 3 source | Added when asked (FR-009). |
| JavaScript prefixes | Prefix → functions | Required before any function is ported. |
| Features | Elsa 4 feature name → project → components | The migration host must reference every project and list every feature (FR-011). |
| Components | One row per inventory item | See below. |
| Accepted refusals | Definition, finding code, reason, date | Only the user adds a row. |
| Last report | Path of the last `report.json` the `dotnet run` command and its exit code | Updated after every run. |

### Component row

| Field | Content |
|---|---|
| Kind | activity type, JavaScript function, variable type, storage driver |
| Elsa 3 name | As the report names it |
| Uses | Definitions that use the component (the count of the report's definition list for it), then the number of references |
| Source | Path, or `not found` |
| Elsa 4 target | Full name, host-function name or dotted alias |
| Cases | Catalog case numbers, in catalog order |
| Tests to port | Paths |
| Status | `todo` → `porting` → `ported` → `verified`, or `redesign` / `dropped`; a storage-driver row: `todo` → `interviewed` |

`verified` means no finding of any category in the last assessment names this component (by kind and name).
`interviewed` means the user's objective is recorded with the chosen Elsa 4 feature or the link to their own plan;
the storage-driver finding repeats on every run and does not reopen the row. `redesign` and `dropped`
need the user's decision recorded in the row.

## Porting case (repository, `porting-catalog.md`)

| Field | Content |
|---|---|
| Number and title | 1–19, as FR-014 |
| Applies when | What in Elsa 3 source triggers it |
| Search | The focused search (R-6) |
| Before | Elsa 3 example (markdown only) |
| After | For *port*: the sample region `case-NN`, verbatim |
| Verdict | `port`, `redesign` or `interview`, or port and redesign with the part each covers |
| Redesign alternatives | For *redesign* |
| Interview | For *interview*: the questions, and per objective the Elsa 4 features and how to use them |

## Compiled sample (repository)

One synthetic feature `Acme` with one activity or registration per *port* case, each inside
`// catalog:case-NN begin` and `// catalog:case-NN end`. Tests cover each region's behaviour.
