# persistence-querying — instance & incident query APIs

Backend REST tests for Foundation's **read-only runtime query surface**: listing/filtering workflow instances,
cursor paging, and incident retrieval. Shared helper: `_PersistenceCommon.ps1`. Runs against a from-source
`Elsa.Workbench` (see ../README.md).

## Surface

- `GET runtime/workflows/instances` — legacy bare-array list (default take 100, max 500).
- `GET runtime/workflows/instances/page` — paged view `{ Items, NextCursor, HasNext, Count, TotalCount }` (default take 25, max 100).
  - Filters (both endpoints): `status`, `definitionId`, `correlationId`, `artifactId`, `runKind`, `take`, `cursor`.
- `GET runtime/workflows/instances/{id}/incidents` — `{ workflowExists, incidents:[...], count }`.

## Scripts

| Script | What it exercises |
|--------|-------------------|
| `Test-InstanceFilters.ps1` | `artifactId` / `definitionId` / `correlationId` / `status` filters each return the correct subset (with a controlled fixture) |
| `Test-InstancePaging.ps1` | cursor paging over `instances/page`: every page <= `take`, union covers all instances with no duplicates, `TotalCount` correct |
| `Test-IncidentQuery.ps1` | poisoned scheduler work records a system-authored `Blocking` / `WaitForIntervention` incident while the instance remains `Running`; the incident is retrievable and counted (summary `incidentCount` + incidents endpoint) |

## Notes learned

- The instances endpoints are **read-only** (there are no mutation endpoints on `instances/…` — see the Alterations gap, issue #1016).
- Ordinary activity faults are resolved by the executable's pinned incident strategy. Elsa Foundation provides `Fault/1` and `ContinueWithIncidents/1`; this suite instead covers the system-owned poisoned-scheduler-work path.
- A convenient reliable poison scenario for tests is a `JavaScript` input binding that throws. It surfaces as a `SchedulerWorkPoisoned`, `Blocking` incident with a system-authored `WaitForIntervention` outcome while the instance remains `Running`.
- PowerShell gotcha baked into the helper usage: a function returning a **1-element array unwraps to a scalar** on assignment, so the scripts wrap query results in `@(...)` before counting.
