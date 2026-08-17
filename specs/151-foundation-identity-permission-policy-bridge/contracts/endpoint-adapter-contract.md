# Contract — Minimal API and transitional FastEndpoints adapters

## Fixed parity matrix

One test host exposes six routes:

| Adapter | Single | Any | All |
|---|---|---|---|
| Minimal API | `RequirePermission` | `RequireAnyPermission` | `RequireAllPermissions` |
| FastEndpoints | canonical single policy | canonical any policy | canonical all policy |

For the same normalized principal, resource, provider, catalog, and replacement evaluator, paired routes MUST return the same result.

Required cases: anonymous 401; an authenticated raw-provider/unmarked principal 401 with zero evaluator/resource calls; forged marker plus raw permission on an unregistered runtime authentication type 401 with zero calls; a composite principal that excludes an untrusted identity from evaluator input; two trusted matching identities with different tenants/providers fail closed rather than unioning grants; both orderings of an unauthenticated identity plus an authenticated untrusted identity produce the same 401; provider normalization failure 401 with no marked ticket; trusted normalized missing grant 403; exact, implied, wildcard, and replacement-evaluator 200; request-for-wildcard without wildcard 403; any partial 200; all partial 403; member-local resource denial behavior; resource exception and resource/evaluator `TimeoutException` short-circuit fixtures with no fall-through; and cancelled requests proving `RequestAborted` reaches both endpoint styles without replacing the resource and propagates. Focused tests separately prove external-factory, reconstructed-cookie, and validated-bearer runtime `AuthenticationType` mappings; `HttpContext`-resource lookup; `IHttpContextAccessor` fallback with a domain resource; no-active-context `CancellationToken.None`; and equality of the context-property/method tokens. A non-permission policy with an untrusted/unmarked authenticated principal is delegated unchanged, and the permission result remains 401 when authentication middleware is deliberately ordered before route selection in a regression host.

## Transitional endpoint-base behavior

All six Elsa endpoint bases retain:

```csharp
protected void ConfigurePermissions(params string[] permissions)
```

Their route/action call sites do not change.

- `ConfigurePermissions()` -> one canonical single `*` policy.
- `ConfigurePermissions("action")` -> one canonical any(`*`, `action`) policy.
- Multiple actions -> one canonical any set containing `*` and every action.
- The base calls FastEndpoints `Policies(onePolicyName)`.
- The base never calls `Permissions(...)` or `PermissionsAll(...)`.

The existing `ElsaEndpointPermissions.Compose(...)` array helper remains compatible; a new policy helper supplies the base implementation.

## Non-contract changes forbidden in this slice

- No route, HTTP method, request/response, binding, action permission, OpenAPI, or endpoint discovery change.
- No broad Minimal API module migration.
- No removal of the transitional FastEndpoints claim-type configurator needed by non-Elsa routes.
- No synchronous-to-asynchronous migration of activity-design/runtime-inspection authorization contexts; #1356 owns it.

## Isolation

FastEndpoints integration tests run in `FastEndpointsHostCollection`, never start concurrent hosts, use exact discovery assemblies, and dispose one host before another. This is required because FastEndpoints serializer/security/discovery configuration is process-global.
