# Execution-core SC-005: HTTP-level BPMN authorization on the Owned Workbench

Point-in-time statement for execution-core milestone slice D2 / goal §C SC-005 (owner decision 11 November 2026, section 2: HTTP-level real-host tests now; Studio half deferred). Tests and this report only. No production change.

**Pinned main SHA at branch:** `e81b0da15`  
**This slice HEAD:** `560532749`  
**Limit:** SQLite Owned Workbench, **one in-process host**, Root composition with `directBpmn: true` and `authoredDirectPublication: true`. No PostgreSQL, SQL Server or MySQL cell. No second tenant on this host.

**Status vocabulary:** **PROVED** (hosted HTTP on this slice HEAD); **EXPLICITLY DEFERRED** (owner record: this slice); **unit** (existing component tests, not hosted).

This statement does not complete Program [#2892](https://github.com/elsa-workflows/elsa-foundation/issues/2892), [#2907](https://github.com/elsa-workflows/elsa-foundation/issues/2907), or [#3650](https://github.com/elsa-workflows/elsa-foundation/issues/3650). It does not close FR-012, FR-014, or the Studio half of SC-005.

---

## 1. Scope

Owner decision 11 November 2026, section 2: a real-host test in which a signed-in user without the BPMN permissions is refused on each BPMN endpoint, and a second tenant cannot read or command the first tenant's executions. Studio/browser SC-005 remains deferred with the milestone's Studio deferral.

Fixture reuse: `OwnedWorkbench.SeedAdministratorAsync` plus IAM directory writes, as in `UserTaskIdentityJourneyTests` (`Essentials/tests/Activities/Bpmn/Hosting/Tests/UserTaskIdentityJourneyTests.cs`).

## 2. Endpoint inventory (from registrations)

Composed on the Owned Workbench used by the hosted test (`ActivitiesBpmnHostingApi`, default `ActivitiesBpmnInterchange`, `ActivitiesBpmnDirectPublishing`).

| # | Method | Path | Permission | Hosted non-admin | First hop |
|---|---|---|---|---|---|
| 1 | GET | `/bpmn/admission` | `bpmn:read` | refused | cookie Forbid → `AccessDenied` |
| 2 | GET | `/bpmn/publications` | `bpmn:read` | refused | cookie Forbid → `AccessDenied` |
| 3 | GET | `/bpmn/executions` | `bpmn:read` | refused | cookie Forbid → `AccessDenied` |
| 4 | POST | `/bpmn/artifacts` | `bpmn:publish` | refused | cookie Forbid → `AccessDenied` |
| 5 | POST | `/bpmn/executions` | `bpmn:execute` | refused | cookie Forbid → `AccessDenied` |
| 6 | GET | `/bpmn/executions/{id}` | `bpmn:read` | refused | cookie Forbid → `AccessDenied` |
| 7 | GET | `/bpmn/executions/{id}/inspection` | `bpmn:read` | refused | cookie Forbid → `AccessDenied` |
| 8 | GET | `/bpmn/executions/{id}/work` | `bpmn:read` | refused | cookie Forbid → `AccessDenied` |
| 9 | GET | `/bpmn/executions/{id}/incidents` | `bpmn:read` | refused | cookie Forbid → `AccessDenied` |
| 10 | GET | `/bpmn/executions/{id}/commands/{request}` | `bpmn:read` | refused | cookie Forbid → `AccessDenied` |
| 11 | POST | `/bpmn/executions/{id}/work/{invocation}/complete` | `bpmn:complete-human` | refused | cookie Forbid → `AccessDenied` |
| 12 | POST | `/bpmn/executions/{id}/work/{invocation}/claim` | `bpmn:complete-human` | refused | cookie Forbid → `AccessDenied` |
| 13 | POST | `/bpmn/executions/{id}/work/{invocation}/complete-claimed` | `bpmn:complete-human` | refused | cookie Forbid → `AccessDenied` |
| 14 | POST | `/bpmn/executions/{id}/cancel` | `bpmn:cancel` | refused | cookie Forbid → `AccessDenied` |
| 15 | POST | `/bpmn/executions/{id}/retry-service` | `bpmn:retry-service` | refused | cookie Forbid → `AccessDenied` |
| 16 | POST | `/interchange/bpmn/analyze` | `bpmn-interchange:read` | refused | cookie Forbid → `AccessDenied` |
| 17 | POST | `/interchange/bpmn/import` | `bpmn-interchange:manage` | refused | cookie Forbid → `AccessDenied` |
| 18 | POST | `/interchange/bpmn/export` | `bpmn-interchange:read` | refused | cookie Forbid → `AccessDenied` |
| 19 | POST | `/interchange/bpmn/versions/{versionId}/publish-direct` | `bpmn:publish` | refused | cookie Forbid → `AccessDenied` |

**Not covered, with why:** none of the registered BPMN HTTP endpoints. Studio UI surfaces are **EXPLICITLY DEFERRED** (owner decision section 2; spec 1184 SC-005 visible behavior).

Sources: `Essentials/src/Activities/Bpmn/Api/Endpoints/BpmnHostingApi.cs`, `BpmnCatalog.cs`, `Interchange/Endpoints/BpmnInterchangeApi.cs`, `Interchange/Endpoints/DirectPublication/Endpoint.cs`.

## 3. Non-admin half

**PROVED** (hosted HTTP).

- Test: `Signed_in_user_without_bpmn_permissions_is_refused_on_every_bpmn_endpoint`
- File: `Essentials/tests/Activities/Bpmn/Hosting/Tests/BpmnHostingAuthorizationTests.cs`
- SHA: `560532749`
- Setup: second real user seeded through `SeedAdministratorAsync("operator")`; IAM user and tenant-membership roles cleared; `user-tasks:read` kept so the subject remains a signed-in non-BPMN caller (`GET /runtime/user-task-identity` 200). Administrator `GET /bpmn/admission` remains 200.
- Expected refusal: HTTP 401 or 403 as `[RequirePermission]` / `[RequireAnyPermission]` document, **or** the hosted cookie Forbid redirect to `AccessDenied` (302/303/307). BPMN endpoints do not call `DisableCookieRedirect` (unlike Decisions and Structured Logs). Following that redirect lands on an unmapped path (404); the test disables auto-redirect so the first hop is the refusal. Unauthenticated Challenge remains 401 (`DirectBpmnWorkbenchTests`).
- Private payload: refusal bodies must not carry execution `payload` or `authoredInputs`.

## 4. Tenant half

**EXPLICITLY DEFERRED.** The Owned Workbench cannot host a second tenant without production host changes.

- `IdentitySeedCoordinator.EnsureSeededAsync` always seeds into `AspNetCoreIdentityOptions.DefaultTenantId`.
- `OwnedWorkbench.SeedAdministratorAsync` only changes the next seed username/email; the second user is another administrator in the same default tenant (the `UserTaskIdentityJourneyTests` fixture).
- First-party login binds persistence to the requested or default tenant, then `UserManager.FindByNameAsync` is tenant-scoped. There is no Owned-Workbench API to materialize an ASP.NET Identity user in a second tenant.
- Spec 1184 evidence already records: "The owned Workbench has one tenant and seeds only administrators."

Unit proofs that remain the tenant-disclosure record (not re-run on this slice HEAD; labelled **unit**):

- `BpmnCommandAuthorizationOrderTests` — `Essentials/tests/Activities/Bpmn/Tests/BpmnCommandAuthorizationOrderTests.cs`
- `BpmnCatalogTests.Execution_page_filters_protocol_scope_tenant_and_resource_without_disclosing_private_payload` — `Essentials/tests/Activities/Bpmn/Tests/BpmnCatalogTests.cs`
- `BpmnAuthoredDirectPublisherTests.Foreign_tenant_sources_never_reach_publication` — `Essentials/tests/Activities/Bpmn/Interchange/Tests/BpmnAuthoredDirectPublisherTests.cs`
- `BpmnDirectExecutionTests.Published_source_commands_keep_exact_replay_human_attempts_and_tenant_disclosure_boundaries` — `Essentials/tests/Activities/Bpmn/Tests/BpmnDirectExecutionTests.cs`

## 5. Studio half

**EXPLICITLY DEFERRED** (owner decision 11 November 2026, section 2). SC-005 in spec 1184 is a Studio criterion; this slice proves the server-side HTTP property only. FR-012 / FR-014 / Studio SC-005 stay with [#3650](https://github.com/elsa-workflows/elsa-foundation/issues/3650) and are not taken over here.

## 6. SQLite / one-host limit

Every hosted cell is SQLite Owned Workbench, one process. No second host, no second tenant, no other providers. Cookie and IAM writes use the fixture's `elsa.db`.

## 7. Out of scope (honoured)

No production code change. [#3650](https://github.com/elsa-workflows/elsa-foundation/issues/3650), [#4038](https://github.com/elsa-workflows/elsa-foundation/issues/4038), ETL [#3804](https://github.com/elsa-workflows/elsa-foundation/issues/3804)/[#3789](https://github.com/elsa-workflows/elsa-foundation/issues/3789), [#3204](https://github.com/elsa-workflows/elsa-foundation/issues/3204) untouched. No issue closed.
