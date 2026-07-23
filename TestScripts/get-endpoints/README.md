# get-endpoints — GET-endpoint coverage

Systematic backend coverage of the **reachable GET endpoints** on the default `Elsa.Server`, one script per API
area. Each script builds a controlled fixture, hits every GET endpoint in its area with valid + edge/negative
parameters, and asserts the HTTP status (and a light body predicate). Shared harness: `_GetCommon.ps1`
(`Invoke-Get` / `Assert-Get` / `Complete-GetSuite`). Runs against a from-source server (see ../README.md).

## Scripts (areas)

| Script | Endpoints covered |
|--------|-------------------|
| `Test-RuntimeGets.ps1` | health; executables (list/get/provenance/input-sources); instances (list/filtered/paged/get/incidents); activity-executions (get/descendants/layout/value-evidence payload); dispatches (filtered/get); diagnostics settings — 20 cases |
| `Test-DesignWorkflowGets.ps1` | definitions (list/get/versions); versions/{id}; drafts/{id}; drafts/{id}/validations — 8 cases |
| `Test-PublishingGets.ps1` | activities (list) + construct; value-conversion/profiles; workflows/{id}/slots + /policy — 7 cases |
| `Test-DesignActivityGets.ps1` | authoring-capabilities; catalog; definitions (list/get/picker/drafts/versions); versions/{id} + dependencies; drafts/{id} — 13 cases |
| `Test-IdentityGets.ps1` | session; capabilities; bootstrap; token — 4 cases |

Every area also asserts negative `{bogus-id} -> 404` cases where applicable.

## Contract notes learned

- **`runtime/workflows/dispatches` requires a filter** — a bare list is a **400**; you must pass
  `parentWorkflowExecutionId` / `childWorkflowExecutionId` / `status`.
- **`activity-executions/{ae}/descendants` and `/layout` are data-dependent** — `200` with data, `404` when the
  ae has none (a simple `Sequence[WriteLine]` yields 404). Asserted as `200|404`.
- **`publishing/workflows/{definitionId}/slots` is lenient** — an unknown definition returns an empty list
  (`200`), not `404`.
- **Instance filters** (`status`/`definitionId`/`correlationId`/`artifactId`) and **cursor paging**
  (`instances/page`) are covered here at the endpoint level (behavioural depth is in `../persistence-querying/`).

## Scope

Only endpoints **composed on the default `Elsa.Server`** are covered. Studio/Nuplane/tooling GET endpoints that
exist in the codebase but aren't composed by the reference server (workspaces, projects, builds, migration,
opentelemetry diagnostics, studio preferences, javascript document rendering, secrets descriptors, …) are out of
scope. POST/PUT/DELETE coverage is a separate future effort.
