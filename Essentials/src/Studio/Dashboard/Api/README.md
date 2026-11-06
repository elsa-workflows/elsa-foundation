# Named dashboard and question APIs

`StudioDashboardApi` exposes native authoring, persistence and module query capabilities. It inherits `DashboardManagementFeature` and publishes capability `elsa.api.studio-dashboard`, contract version `1`. Relations include `datasets`, `query-preview`, `query-validate`, `query-drill`, `questions`, `dashboards` and `audience-candidates`.

Enable Dashboard persistence as well. The API diagnoses missing or duplicate actor, store, definition-validator and audience-directory registrations. Existing Home routes, contributed widgets and preferences keep their current behavior.

## Cross-domain contributions

`DashboardResourcesApiFeature.ConfigureServices` registers [`DashboardResourcePermissionContributor`](DashboardApiActor.cs) through `services.AddPermissionContributor<DashboardResourcePermissionContributor>()`. It contributes the ten question/dashboard resource-operation permission declarations through `IPermissionContributor` to Foundation Identity's `CompositePermissionCatalog`. The [Identity extension-points catalog](../../../Foundation/Identity/Core/EXTENSION_POINTS.md#ipermissioncontributor) owns the shared contract, registration and aggregation rules. Native resource ownership and source-data authorization govern each operation independently.

## Identity and access

Requests require one trusted normalized identity with an unambiguous canonical Elsa user ID and tenant. External authentication subjects and display names are never IAM user IDs. Weaver correlation uses the separately retained authentication subject; resource ownership uses the canonical user.

Permissions are `studio/questions:{read,create,edit,share,delete}` and `studio/dashboards:{read,create,edit,share,delete}`. Each verb is a separate catalog key. The permission and current resource grant both apply. Wildcard permission alone does not make a user another resource's owner.

Owners manage audiences and deletion. Editors change content but cannot acquire ownership or change sharing. Viewers can read and, with create permission and current definition disclosure, save private copies. Definition access and source-data/field/aggregate access are independent.

Audiences bind stable tenant-local user/role IDs. Current user memberships are filtered against currently existing roles; token role labels are not audience IDs. Directory browsing needs `identity/users:read` or `identity/roles:read` plus the corresponding bounded IAM paging contract. Unsupported discovery is unavailable rather than an empty directory.

## Conditional resource operations

Collections are `/_elsa/studio/questions` and `/_elsa/studio/dashboards`. GET accepts `ownership=mine|shared`, optional `search` and the returned `continuationToken`. Pages contain at most 100 accessible resources.

POST creates a personal resource from a validated native definition. Keep its `operationKey` on retries; reusing a key with different content conflicts. GET returns an ETag. PUT, DELETE, audience replacement, saved-definition copy and historical restoration require that exact quoted revision in `If-Match`. An obsolete revision returns 412; a missing/invalid precondition returns 428. Failed writes never fall back to device-local shared storage.

| Subroute | Purpose |
| --- | --- |
| `/{id}` | Current definition; conditional content replacement or owner deletion |
| `/{id}/audience` | Owner-only audience read/replacement |
| `/{id}/copies` | Keyed private copy of one exact saved definition revision |
| `/{id}/revisions` | Current-authorized bounded definition history |
| `/{id}/revisions/{revision}` | Retained revision under current disclosure rules |
| `/{id}/restorations` | New validated content revision preserving current ownership/grants |

Questions store the native typed query request, visualization and parameters. Accepted explicit values become saved defaults. Dashboards store independently identified tiles with question references and typed parameter/presentation overrides. Each execution resolves one current question revision and validates effective overrides; it never reuses an author's result cache.

Sharing/copying a dashboard does not share/copy its questions. A dashboard copy retains references and gets new tile IDs. Copy questions separately when independent ownership is intended. Deleting a question leaves its tile references unavailable; deleting a dashboard leaves questions intact.

## Protected projections and recovery

An inaccessible question reference returns its dashboard-local tile ID and safe layout with `referenceState: unavailable`, omitting question identity/overrides. Editors can preserve that exact binding with `preserveReference: true` while changing order/size. The marker cannot be transferred to another dashboard or used to extract the reference.

A withheld question permits metadata-only editing with `preserveDefinition: true` and its current revision. Full copying/restoration requires current disclosure of the complete content. Never round-trip a redacted projection as a replacement definition.

Audience candidates use `/_elsa/studio/dashboard-audience-candidates?kind=user|role&search=...`. Forward the signed `continuationToken` unchanged. A page can have no matches while still offering continuation, for example inactive users or unmatched role names. Do not present that as global no matches. Changing actor, tenant, kind or search invalidates the token.

Responses use `no-store`. Missing, unauthorized and cross-tenant IDs have the same safe not-found shape. Source incompatibility, access denial, persistence failure and successful emptiness remain distinct. Removing AI does not affect compatible native resource operations.
