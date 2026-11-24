# Data model: Final JTest acceptance

Everything here is a file in `extensions/Elsa3/tests/JTest/`. The ledger is Markdown so a reviewer reads it in the
PR diff; no tooling parses it.

## Corpus copy

| Field | Rule |
|---|---|
| Source | JTest-Nexxbiz repository, commit `b1cde13` |
| Copied | `workflow-definitions/`, `tests/`, `templates/`, `environments/`, `test-files/`, `global-configuration.json`, `import-and-publish-workflows.json` |
| Not copied | `pipelines/` and the repository README (Nexxbiz delivery files, not test content) |
| Change rule | Every change after the copy commit is a separate, reviewed diff with its reason in the ledger |

## Suite (`ledger/suites.md`, one row per suite file)

| Field | Values |
|---|---|
| Suite | path under `tests/` |
| Definitions | definition files it exercises |
| Elsa 3 result | `pass`, `fail`, `blocked: <3.6.3 defect>` |
| Elsa 4 result | `pass`, `fail`, `not run`, `blocked: JOIN`, `blocked: <prerequisite>`, `incompatible` |
| Last run | run id |

State flow per engine: `not run` → `fail` or `pass`; `fail` → `pass` after a recorded fix, or → `blocked`/`incompatible`
with its reason. A row never returns to `not run`.

## Baseline failure (`ledger/baseline-failures.md`, one row per failing assertion on 3.6.3)

| Field | Values |
|---|---|
| Suite and assertion | suite path, test name, dataset, step, assertion |
| Observed vs expected | the values `jtest` reported |
| Class | `suite defect`, `corrected behaviour`, `3.6.3 defect` |
| Evidence | why: suite description, Elsa 3 source or release notes, a reproduction |
| Fix | the exact change, or `none (blocked)` |
| Approved | the list as a whole is approved by the coordinator before fixes land (FR-007) |

## Suite change (`ledger/suite-changes.md`, one row per changed assertion or step after the engine-neutral refactor)

| Field | Values |
|---|---|
| Suite and step | location |
| Change | before and after |
| Reason | why it is needed, which engine needs it |
| Approved | coordinator approval (FR-012) |

The engine-neutral refactor itself is one reviewed diff; its rule (values move, expectations stay) is checked by the
reviewer and by a green Elsa 3 rerun, not row by row.

## Definition disposition (`ledger/dispositions.md`, one row per definition)

| Field | Values |
|---|---|
| Definition | file, Elsa 3 definition id and version |
| Bucket | `convert-as-is`, `convert-with-adjustments`, `incompatible`, or `unclassified` until decided |
| Reconciliation | `published`, `refused: <code>`, `skipped: <cause>` from the receipt |
| Adjusted copy | path under `adjusted/`, the change and reason, coordinator approval |
| Incompatibility | diagnostic, guidance, inherent or not yet implemented, condition to reconsider |
| Suites affected | every suite using the definition |
| JOIN | `none`, `contains join, not blocking`, `blocked` with construct and failure |

## Adjusted definition (`adjusted/`)

An adjusted copy keeps the original file name. The Elsa 4 run's definition set is the originals with each adjusted
copy taking the place of its original, passed to the reconciliation as an explicit `Files` list. The original stays in
`workflow-definitions/` unchanged.

## Run record (`ledger/runs.md`, one row per run, newest last)

| Field | Values |
|---|---|
| Run id | sequential |
| Engine | Elsa 3 or Elsa 4 |
| Source | for Elsa 3 the image digest; for Elsa 4 the commit and whether spec 1207 is included |
| jtest | version |
| Scope | suites run |
| Result | per-suite pass/fail counts; failures by suite |
| Receipt | Elsa 4 reconciliation summary |
| Notes | anything unexpected; a failed run is never removed |
