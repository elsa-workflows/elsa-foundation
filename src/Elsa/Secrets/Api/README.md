# Secrets API

This package exposes the tenant-scoped Secrets REST surface. It is the representative CRUD and security migration in the [First-party REST API Consolidation program](../../../../docs/program-goals/first-party-rest-api-consolidation.md).

## Composition

`SecretsApiFeature` implements CShells `IWebShellFeature`. It preserves the module's `AddSecrets()` service registration, contributes the permission catalog, and delegates HTTP registration to the public `SecretsApi.MapSecretsApi(IEndpointRouteBuilder)` mapper. A host outside CShells can call that mapper directly after registering the feature's services.

The package registers the Secrets application services plus `SecretsPermissionContributor`. It owns no API-specific background task, scheduled job, or event handler.

## HTTP surface

| Method | Route | Permission | Purpose |
|---|---|---|---|
| GET | `/secrets` | `secrets:read` or `*` | List tenant-visible secret metadata. |
| POST | `/secrets` | `secrets:write` or `*` | Create a secret and return safe metadata. |
| GET | `/secrets/descriptors` | `secrets:read` or `*` | Discover supported types and stores. |
| POST | `/secrets/picker` | `secrets:read` or `*` | Query bounded picker metadata. |
| GET | `/secrets/{name}` | `secrets:read` or `*` | Read one tenant-visible metadata record. |
| PUT | `/secrets/{name}` | `secrets:write` or `*` | Update metadata; the route name is authoritative. |
| DELETE | `/secrets/{name}` | `secrets:delete` or `*` | Delete a secret. |
| POST | `/secrets/{name}/revoke` | `secrets:delete` or `*` | Revoke a secret. |
| POST | `/secrets/{name}/rotate` | `secrets:update-value` or `*` | Rotate sensitive value material. |
| POST | `/secrets/{name}/test` | `secrets:test` or `*` | Test availability and return a safe result. |

All routes use the Foundation Identity policy provider and evaluator. The module catalogs `secrets:read`, `secrets:write`, `secrets:update-value`, `secrets:delete`, `secrets:test`, `secrets:use`, `secrets:import`, and `secrets:export` under owner `Elsa.Secrets.Api`; only `secrets:write` implies `secrets:read`. The administrative wildcard remains a grant and is not cataloged as a module permission.

## Tenant and disclosure boundaries

The authenticated normalized principal's tenant claim is authoritative for every data route. A request body cannot select a tenant or override a route name. Descriptor discovery intentionally remains tenant-independent for compatibility.

Responses expose metadata only. Raw values, configuration keys, protected payloads, and provider-private details must not appear in responses, errors, headers, OpenAPI response schemas, or audit records.

## Transition and collectibility

The production package contains no FastEndpoints dependency or endpoint discovery types. FastEndpoints routes from other modules can coexist in a transitional host because both authoring models publish standard ASP.NET Core endpoints and use Foundation authorization.

Handlers use explicit `RequestDelegate` boundaries and explicit endpoint metadata. This avoids retaining collectible module types in ASP.NET Core request-delegate caches. Compatibility tests materialize the production routes, execute JSON traffic, exercise route/service/serializer release, and invoke the real ASP.NET OpenAPI document provider. The OpenAPI path currently leaves a framework-held collectible reference after all harness owners are disposed, so OpenAPI-enabled dynamic unload is not yet a supported claim. See `docs/reports/secrets-minimal-api-migration-2026-08.md` for the complete evidence and required follow-up.
