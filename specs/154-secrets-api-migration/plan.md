# Implementation Plan: Secrets API Minimal API Migration

**Branch**: `codex/1348-secrets-minimal-api` | **Date**: 2026-08-15 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/154-secrets-api-migration/spec.md`

## Summary

Migrate the complete ten-operation Secrets REST surface from FastEndpoints to an explicit module-owned Minimal API mapper. `SecretsApiFeature` will implement CShells' existing `IWebShellFeature` seam and delegate to a standard `IEndpointRouteBuilder` mapping entry point. The mapper will preserve the current routes, binding, camel-case JSON and enum representation, statuses, ProblemDetails behavior, tenant rules, safe metadata projections, consumed OpenAPI, and wildcard-or-action permission policies. Before any production endpoint is changed, a real FastEndpoints host will produce reviewed HTTP and OpenAPI baselines. The replacement must compare with zero unapproved differences, uniquely contribute all eight stable Secrets permissions, prove two-tenant isolation and redaction, coexist with an unmigrated route, and release materialized route/service/documentation owners under collectible-context verification.

## Technical Context

**Language/Version**: C# / .NET 10 (`net10.0`)

**Primary Dependencies**: ASP.NET Core Minimal APIs and OpenAPI, CShells `IWebShellFeature`, Foundation Identity authorization policies/catalog, System.Text.Json web defaults, existing Secrets services and Groundwork persistence, xUnit, Microsoft.AspNetCore.TestHost, and `Elsa.Api.Compatibility.Testing`

**Storage**: Existing `ISecretRepository` implementations, including deterministic in-memory fixtures and existing Groundwork provider tests; compatibility baselines committed in the Secrets test project

**Testing**: xUnit domain and persistence tests, real TestServer HTTP/authorization/tenant/redaction integration tests, endpoint-manifest and consumed OpenAPI comparison, mixed Minimal API/FastEndpoints coexistence, and collectible `AssemblyLoadContext` evidence

**Target Platform**: Cross-platform ASP.NET Core hosts running CShells, including shell generations that may load endpoint modules into collectible contexts

**Project Type**: Existing modular ASP.NET Core library and existing backend test project

**Performance Goals**: Ten unchanged evidence captures are byte-identical; no request-time endpoint discovery, permission-claim parsing, or path-specific authorization middleware is added

**Constraints**: Preserve public HTTP/OpenAPI behavior and sensitive-data boundaries; route identity and authenticated tenant remain authoritative; use explicit `RequestDelegate` boundaries to avoid collectible request-type retention; remove every production Secrets FastEndpoints registration/dependency; no shared endpoint DSL or contract redesign

**Scale/Scope**: Ten routes, nine data-bound operations plus tenant-independent descriptor discovery, eight stable permissions with five HTTP consumers, two deterministic tenants, one representative transitional FastEndpoints route, and the module-specific evidence set

## Constitution Check

*GATE: Passed before Phase 0 research; re-checked after Phase 1 design.*

- **Layering and domain boundary**: transport mapping and permission-catalog registration remain in `Elsa.Secrets.Api`. Core models/services and persistence contracts do not acquire ASP.NET Core, CShells, or Foundation Identity dependencies.
- **Explicit composition**: the accepted ADR's module-owned mapper is invoked through the existing CShells `IWebShellFeature` seam. No new provider registry, endpoint builder, process-global discovery, or application-root switch is introduced.
- **Framework honesty**: mappings produce ordinary ASP.NET Core endpoints, authorization metadata, OpenAPI metadata, and results. Module-local compatibility translation is allowed only where captured legacy evidence requires it.
- **Golden rule**: existing Secrets behavior, audit, tenant, and persistence tests remain. Immutable real-host HTTP/OpenAPI evidence is captured before legacy removal; no existing test objective is deleted.
- **Security and least privilege**: Foundation Identity is the authorization authority. All stable permission names receive one catalog owner; `write -> read` is the only new implication. Tenant identity comes only from the normalized principal, descriptors retain their intentional tenant-independent behavior, and sensitive markers are bite-tested across every output.
- **Feature documentation**: the API documentation will list its explicit mapper, services, permissions/implications, handlers/contributors, route inventory, tenant behavior, and transition scope.
- **Dependency direction and project count**: no new production or test project is introduced. The API package replaces its FastEndpoints reference with the narrow CShells, ASP.NET Core, and Foundation abstractions it consumes.
- **Collectibility**: production handlers use explicit `RequestDelegate` boundaries and manual web-JSON deserialization so process-wide binding caches do not own collectible request DTO types. Tests materialize routes and documentation, release every owner, and retain only weak-reference evidence.
- **Provisional material**: framework constitution §2.24 remains draft/provisional and is not treated as ratified. ADR 0068, the named program, and the existing CShells seam provide the approved direction.
- **Post-design re-check**: passed. The design adds no constitution exception, project-count waiver, test deletion, or shared abstraction beyond the already-approved seams.

## Project Structure

### Documentation (this feature)

```text
specs/154-secrets-api-migration/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── authorization-and-disclosure-contract.md
│   ├── collectibility-contract.md
│   └── secrets-http-contract.md
└── tasks.md
```

### Source Code (repository root)

```text
src/Elsa/Secrets/Api/
├── Authorization/
│   └── SecretsPermissionContributor.cs
├── Constants/
│   └── RouteConstants.cs
├── Features/
│   └── SecretsApiFeature.cs
├── Requests/
├── SecretsApi.cs
├── Elsa.Secrets.Api.csproj
└── README.md

tests/Elsa/Secrets/Tests/
├── Baselines/
│   ├── secrets-http-fastendpoints.json
│   └── secrets-openapi-fastendpoints.json
├── Support/
│   ├── SecretsCanaryHost.cs
│   ├── SecretsCollectibleFixture.cs
│   └── SecretsCompatibilityCases.cs
├── SecretsApiAuthorizationTests.cs
├── SecretsApiCollectibilityTests.cs
├── SecretsApiCoexistenceTests.cs
├── SecretsApiContractTests.cs
├── SecretsApiLifecycleContractTests.cs
├── SecretsApiReadContractTests.cs
└── Elsa.Secrets.Tests.csproj

tests/Elsa/Architecture/Baselines/
└── fastendpoints-transition-exceptions.json

docs/reports/
└── secrets-minimal-api-migration-2026-08.md
```

**Structure Decision**: keep mapping, transport translation, and catalog contribution in the existing API package; extend the existing Secrets test project with one deterministic host and shared cases. Reuse program compatibility/collectibility infrastructure and the canary's explicit-request-delegate pattern instead of adding a Secrets-specific framework. Delete legacy endpoint source and its ten transition records only after replacement evidence passes.

## Design Decisions

1. `SecretsApiFeature` implements `IWebShellFeature`; `MapEndpoints` delegates to public `SecretsApi.MapSecretsApi(IEndpointRouteBuilder)`. CShells remains responsible for shell services, prefix, ownership metadata, generation publication, and route removal.
2. Map all ten routes with explicit `RequestDelegate` handlers. Resolve route/query/body/services from `HttpContext`, deserialize body payloads with ASP.NET web JSON defaults, and publish explicit request/response/OpenAPI metadata so materialization cannot place collectible DTO types in `RequestDelegateFactory` caches.
3. Apply endpoint owner `Elsa.Secrets.Api`, Minimal API authoring metadata, and one canonical `RequireAnyPermission("*", action)` policy to every route. Handlers do not inspect permission claims.
4. Add `SecretsPermissionContributor` for all eight stable constants with owner `Elsa.Secrets.Api`. Declare only `secrets:write -> secrets:read`; update-value, delete, test, use, import, and export remain independent.
5. Resolve tenant only from `IdentityClaimTypes.TenantId` for the nine data operations. Preserve missing-tenant `403` without invoking services. Descriptor discovery retains no tenant requirement beyond its read authorization policy.
6. Treat route `name` as authoritative for update/rotate/revoke/delete/test. Body models do not select tenant or target identity even where legacy DTOs combined route and body properties.
7. Capture canonical real FastEndpoints HTTP and standard ASP.NET Core OpenAPI evidence before removing production endpoints. Seed two tenants with same-named active/revoked/expired/deleted and encrypted/configuration-backed examples, deterministic identifiers/time/registries, and unique sensitive markers.
8. Preserve exact legacy error behavior by evidence. Translate only known domain/framework outcomes inside the module; do not introduce a shared error framework or improve stale contracts during authoring migration.
9. Prove redaction by scanning all successful and failed bodies, headers, API-description response schemas, and audit observations for submitted values, configuration keys, protected/provider material, and unsafe provider exception text.
10. Prove coexistence with one unrelated FastEndpoints test route using the same Foundation evaluator. The production Secrets assembly must contain no FastEndpoints endpoint/discovery dependency.
11. Extend collectible evidence beyond the canary by materializing endpoints, executing representative JSON traffic, generating the OpenAPI projection, releasing route/service/documentation owners, and classifying route, DI, serializer, documentation, or harness retention without weakening the required assertions.
12. Publish a representative-migration report with compatibility, permission/catalog, tenant, disclosure, coexistence, collectibility, and remaining-risk evidence plus a proceed/revise/stop recommendation for #1349 and remaining waves.

## Complexity Tracking

No constitution violations require justification.
