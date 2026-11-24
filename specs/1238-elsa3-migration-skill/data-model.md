# Data model: Elsa 3 to Elsa 4 migration skill

No persisted data. The entities are files the user or the repository holds.

## Migration plan (user's workspace, markdown)

Filled in from `migration-plan-template.md` ([contract](contracts/plan-template.md)).

| Field | Content | Rule |
|---|---|---|
| Elsa 4 version | The essentials version the user targets; the migration image tag | Set at inventory; changing it means rerunning the assessment. |
| Stage | `inventory` → `prepare` → `test-migration` → `handoff` | Moves forward only; a new finding in `test-migration` sends one component back to `porting`, not the stage. |
| Export | Path of the Elsa 3 export | Required from `inventory`. |
| Source locations | Folders or repositories of Elsa 3 source | Added when asked (FR-009). |
| JavaScript prefixes | Prefix → functions | Required before any function is ported. |
| Features | Elsa 4 feature name → package → components | Every feature must appear in `appsettings.json` (FR-011). |
| Components | One row per inventory item | See below. |
| Accepted refusals | Definition, finding code, reason, date | Only the user adds a row. |
| Last report | Path of the last `report.json` and the `docker run` command | Updated after every run. |

### Component row

| Field | Content |
|---|---|
| Kind | activity type, JavaScript function, variable type, storage driver |
| Elsa 3 name | As the report names it |
| Uses | Usage count from the report |
| Source | Path, or `not found` |
| Elsa 4 target | Full name, host-function name or dotted alias |
| Cases | Catalog case numbers, in catalog order |
| Tests to port | Paths |
| Status | `todo` → `porting` → `ported` → `verified`, or `redesign` / `dropped` |

`verified` means the last assessment shows no Must fix finding caused by this component. `redesign` and `dropped`
need the user's decision recorded in the row.

## Porting case (repository, `porting-catalog.md`)

| Field | Content |
|---|---|
| Number and title | 1–19, as FR-014 |
| Applies when | What in Elsa 3 source triggers it |
| Search | The focused search (R-6) |
| Before | Elsa 3 example (markdown only) |
| After | For *port*: the sample region `case-NN`, verbatim |
| Verdict | `port`, `redesign`, or both with the part each covers |
| Redesign alternatives | For *redesign* |

## Compiled sample (repository)

One synthetic feature `Acme` with one activity or registration per *port* case, each inside
`// catalog:case-NN begin` and `// catalog:case-NN end`. Tests cover each region's behaviour.
