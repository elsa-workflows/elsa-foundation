# Data model: Elsa 3 to Elsa 4 migration skill

No persisted data. The entities are files the user or the repository holds.

## Migration plan (user's workspace, markdown)

Filled in from `migration-plan-template.md` ([contract](contracts/plan-template.md)).

| Field | Content | Rule |
|---|---|---|
| Essentials image tag | The tag of the essentials engine image the user will go live on (a build tag, not a package version); the migration image with the same tag runs the tool | Set at inventory; changing it means rerunning the assessment; the report's image version confirms it. |
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
| Uses | Definitions that use the component (the count of the report's definition list for it), then the number of references |
| Source | Path, or `not found` |
| Elsa 4 target | Full name, host-function name or dotted alias |
| Cases | Catalog case numbers, in catalog order |
| Tests to port | Paths |
| Status | `todo` → `porting` → `ported` → `verified`, or `redesign` / `dropped` |

`verified` means no finding of any category in the last assessment names this component (by kind and name). `redesign` and `dropped`
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
