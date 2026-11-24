# Contract: run hosts

## Elsa 3 baseline

| Item | Value |
|---|---|
| Source | elsa-core release tag `3.6.3`, commit `7eac7de6`, `docker/ElsaServer.Dockerfile` |
| Container settings | `ASPNETCORE_ENVIRONMENT=Development`, `HTTP_PORTS=8080`, `HTTP__BASEURL={published base}` |
| Ready when | `GET {base}/` returns 200 with body `Healthy` |
| Definitions | Imported by the corpus's `import-and-publish-workflows.json` |
| Templates and environment | `templates/`, `environments/dev.json` (`ELSA_API_BASE_URL`, `ELSA_EXECUTOR_BASE_URL={base}/workflows`, `ADMIN_USERNAME`, `ADMIN_PASSWORD`, `CORRELATION_ID`, `DATE_NOW_ISO_STRING`) |
| Fresh state | A new container per run |

## Elsa 4 run

| Item | Value |
|---|---|
| Host | `apps/Elsa.Workbench`, built from the commit under test, default SQLite store deleted before the run |
| Features enabled for the run | `Elsa3JsonReconciliation` with `Options:SourceId` `jtest` and `Options:Files`, entries `{Order, FilePath}`, listing the 34 definitions of the acceptance set (spec Q13) with adjusted copies in place of their originals (see data model); `Elsa3ImportActivitiesEntityFrameworkCore` (import persistence, not pulled in by dependencies); `WorkflowsRuntimeCheckpointPersistence` mode `Durable` |
| Capture, after each start | `PUT /runtime/workflows/diagnostics/settings` setting activity input and output capture to snapshot level and a `variableWrites` override; these settings live in memory and reset on restart. The host policy caps capture at bounded snapshots (contracts/templates.md, *Snapshot limits*) |
| Ready when | The reconciliation summary is logged and the API answers an authenticated request |
| Receipt | The reconciliation summary and per-item diagnostics, kept with the run record |
| Templates and environment | `templates-elsa4/`, `environments/elsa4.json` (`ELSA_API_BASE_URL={base}`, `ELSA_EXECUTOR_BASE_URL={base}/workflows/http`, Workbench's seeded development admin through `ADMIN_USERNAME`/`ADMIN_PASSWORD`, plus `CORRELATION_ID` and `DATE_NOW_ISO_STRING` as for Elsa 3) |
| Default shell | Unchanged: the reconciliation feature stays off unless the run enables it |

In the committed environment files, credentials and base URLs are `${VARIABLE}` references, as in the corpus's
`dev.json`; other non-secret literals (dispatch channels, wait times, engine-specific expected values) are allowed.
