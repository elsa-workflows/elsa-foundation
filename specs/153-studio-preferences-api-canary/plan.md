# Implementation Plan: Studio Preferences API Canary

**Branch**: `codex/1347-studio-preferences-minimal-api` | **Date**: 2026-08-15 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/153-studio-preferences-api-canary/spec.md`

## Summary

Migrate the complete Studio Preferences GET/PUT surface from FastEndpoints to an explicit module-owned Minimal API mapper. `StudioPreferencesApiFeature` will implement CShells' existing `IWebShellFeature` composition seam and delegate its `MapEndpoints(IEndpointRouteBuilder, ...)` method to `MapStudioPreferencesApi`. The mapping will retain the current route, binding, JSON, ETag, precondition, error, OpenAPI, and `studio.preferences.read`/`studio.preferences.write` contracts while using Foundation Identity's canonical wildcard-or-action policies. Before changing production code, the current FastEndpoints host will be captured as reviewed HTTP/OpenAPI evidence; the migrated host must compare with zero unapproved differences.

## Technical Context

**Language/Version**: C# / .NET 10 (`net10.0`)

**Primary Dependencies**: ASP.NET Core Minimal APIs, CShells `IWebShellFeature`, Foundation Identity authorization policies, System.Text.Json, existing Studio Preferences Core services, xUnit, Microsoft.AspNetCore.TestHost, and `Elsa.Api.Compatibility.Testing`

**Storage**: Existing `IStudioPreferenceStore` implementations; deterministic canary evidence committed under the Studio Preferences test project

**Testing**: xUnit service tests, real CShells/TestServer HTTP and authorization integration tests, endpoint-manifest and consumed OpenAPI comparison, mixed Minimal API/FastEndpoints coexistence, and collectible `AssemblyLoadContext` evidence

**Target Platform**: Cross-platform ASP.NET Core hosts running CShells; dynamically loaded modules on supported .NET runtimes

**Project Type**: Existing modular ASP.NET Core library plus its existing test project

**Performance Goals**: Ten unchanged manifest/evidence captures are byte-identical; no additional request-time discovery or path middleware

**Constraints**: Preserve public HTTP/OpenAPI behavior; map through ordinary `IEndpointRouteBuilder`; remove all Studio Preferences FastEndpoints registrations and production dependencies; keep FastEndpoints only in the test fixture used to prove coexistence; no shared endpoint DSL or broad migration

**Scale/Scope**: Two routes, one API feature, the existing read/write permission catalog, one representative transitional FastEndpoints route, and the canary-specific evidence set

## Constitution Check

*GATE: Passed before Phase 0 research; re-checked after Phase 1 design.*

- **Layering and module boundary**: the API project remains a feature/transport implementation over `Elsa.Studio.Preferences.Core`. It adds no transport types to Core and no persistence dependency.
- **Explicit composition**: the accepted ADR's module-owned static mapper is invoked through CShells' existing `IWebShellFeature` seam. This is standard ASP.NET Core endpoint composition, not a new provider registry, endpoint builder, or process-global discovery mechanism.
- **Framework honesty**: handlers use ordinary Minimal API binding, results, route metadata, and Foundation authorization conventions. Module-local transport translation is allowed only where the captured compatibility contract requires it.
- **Golden rule**: current service tests remain; the current FastEndpoints wire behavior is captured before removal and becomes the immutable comparison baseline. No existing test objective is deleted.
- **Security**: endpoints declare wildcard-or-action Foundation policies using `RequireAnyPermission("*", actionPermission)`. Authentication, normalized claims, implication expansion, resource handlers, and evaluator replacement stay owned by Foundation Identity.
- **Feature documentation**: the API feature documentation will identify its explicit mapper, registered service, permissions, and absence of tasks/event handlers.
- **Dependency direction and project count**: no new production or test project is introduced. The API project replaces its `Elsa.Api.FastEndpoints` reference with the narrow ASP.NET Core/CShells abstractions it actually uses.
- **Collectibility**: route and service-provider references are released before bounded weak-reference verification; no collectible object crosses the lifecycle boundary strongly.
- **Provisional material**: framework constitution §2.24 is draft/provisional and is not relied on as a ratified gate. The existing CShells feature seam and accepted ADR 0068 supply the approved composition contract.
- **Post-design re-check**: passed. No constitution exception, test deletion approval, or complexity waiver is required.

## Project Structure

### Documentation (this feature)

```text
specs/153-studio-preferences-api-canary/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── authorization-contract.md
│   ├── collectibility-contract.md
│   └── studio-preferences-http-contract.md
└── tasks.md
```

### Source Code (repository root)

```text
src/Elsa/Studio/Preferences/Api/
├── Elsa.Studio.Preferences.Api.csproj
├── StudioPreferencesApi.cs
├── StudioPreferencesApiFeature.cs
├── StudioPreferencesFeature.cs
├── StudioPreferencesPermissions.cs
├── Models/
├── Services/
└── README.md

tests/Elsa/Studio/Preferences/Tests/
├── Elsa.Studio.Preferences.Tests.csproj
├── StudioPreferencesApiContractTests.cs
├── StudioPreferencesApiAuthorizationTests.cs
├── StudioPreferencesApiCoexistenceTests.cs
├── StudioPreferencesApiCollectibilityTests.cs
├── Support/
└── Baselines/
    ├── studio-preferences-http-fastendpoints.json
    └── studio-preferences-openapi-fastendpoints.json

tests/Elsa/Architecture/Baselines/
└── fastendpoints-transition-exceptions.json

docs/reports/
└── studio-preferences-minimal-api-canary-2026-08.md
```

**Structure Decision**: keep endpoint mapping and module-local transport translation in the existing API package, and extend the existing Studio Preferences test project with one shared host fixture. Reuse the program's compatibility/collectibility test infrastructure rather than adding another library or a Studio-specific endpoint abstraction. Remove the two obsolete transition-exception entries when the legacy endpoint classes disappear.

## Design Decisions

1. `StudioPreferencesApiFeature` implements `IWebShellFeature`; `MapEndpoints` delegates to a public static `MapStudioPreferencesApi(IEndpointRouteBuilder)` entry point. CShells supplies the shell-scoped service provider, prefix, generation metadata, lifecycle removal, and endpoint publication.
2. Apply module owner `Elsa.Studio.Preferences.Api` and Minimal API authoring metadata to both routes through standard conventions. Each route declares exactly one Foundation permission disposition.
3. Preserve the historical administrative wildcard compatibility by using a single canonical `RequireAnyPermission("*", studio.preferences.<action>)` policy, while leaving `write -> read` implication in the existing catalog.
4. Bind `namespace` from the route and the write payload from the body as separate authorities. A body field never selects the preference scope; subject, tenant, and host continue to come from the normalized session and `X-Elsa-Studio-Host-Id`.
5. Capture the legacy FastEndpoints HTTP and consumed OpenAPI evidence before removing it. Compare the migrated host with `CompatibilityComparer` and the program-wide empty approved-difference registry; do not regenerate expectations from the Minimal API implementation.
6. Translate only the known Studio Preferences exceptions at the module boundary. Select `Results`/ProblemDetails shapes from captured evidence, not convenience, so 401/403, 400, 404, 412, 413, and 422 remain exact.
7. Prove coexistence with one unrelated FastEndpoints test route in the same CShells host. The production Studio Preferences assembly must contain no FastEndpoints endpoint bases or discovery dependency.
8. Exercise route and service-graph release with weak-reference evidence. Prefer loading the production API assembly into a collectible context; if a runtime serializer cache is observed, classify it separately as the shared harness requires rather than weakening route/service release assertions.
9. Publish a concise canary report with compatibility, authorization, coexistence, and unload evidence plus a proceed/revise/stop recommendation for #1348 and #1349.

## Complexity Tracking

No constitution violations require justification.
