# Implementation Plan: Foundation Identity permission policy bridge

**Branch**: `1305-permission-policy-bridge` | **Date**: 2026-08-15 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/151-foundation-identity-permission-policy-bridge/spec.md`

## Summary

Make Foundation Identity's existing ASP.NET Core policy provider the only permission-policy owner for first-party endpoints. Add canonical single/any/all policy descriptors and framework-neutral `IEndpointConventionBuilder` extensions in Identity Abstractions, retain the existing single-permission evaluator/resource interfaces as the atomic primitive, and route all six Elsa FastEndpoints bases through one policy name instead of FastEndpoints claim matching. Preserve routes and wildcard-plus-action OR behavior, accept legacy single-policy names for one documented major-version window, expose module/contributor provenance in the catalog, and prove parity in one host across Minimal API and transitional FastEndpoints endpoints.

## Technical Context

**Language/Version**: C# / .NET 10 (`net10.0` repository default)

**Primary Dependencies**: The .NET 10 `Microsoft.AspNetCore.App` shared framework for current authorization, endpoint-routing, middleware-result, and `IHttpContextAccessor` contracts; Foundation Identity Abstractions; CShells/FastEndpoints 7.2.0 only in the transitional adapter and integration fixture

**Storage**: N/A. The permission catalog is an immutable DI-built snapshot; shell/module replacement creates a new service provider and catalog.

**Testing**: xUnit unit, policy-provider, same-host HTTP adapter, module-composition/catalog-lifecycle, and architecture-boundary tests

**Target Platform**: Elsa.Server / CShells modular ASP.NET Core hosts on all currently supported platforms

**Project Type**: Modular framework libraries plus ASP.NET Core endpoint adapters

**Performance Goals**: Authorization remains bounded by the number of permissions in one declaration and the existing catalog implication graph; no reflection or provider lookup is added per endpoint declaration. Same-host tests focus on correctness rather than a new throughput target.

**Constraints**: Preserve public routes/methods and current `ConfigurePermissions()` OR semantics; no Identity-to-FastEndpoints dependency; no new project; legacy policy alias remains parse-only for one major-version window; anonymous/unmarked-principal = 401, normalized denial = 403, operational failure/cancellation propagates; request cancellation comes only from the active `HttpContext.RequestAborted` without replacing the protected resource; FastEndpoints-host tests must be serialized.

**Scale/Scope**: 175 existing `ConfigurePermissions(...)` call sites continue unchanged behind six base classes; two endpoint styles × three composition modes; Studio Preferences and Module Management are the catalog-provenance canaries. Transport-adjacent direct-claim contexts are separated into follow-up #1356. No shipping request-authentication handler currently calls `IClaimsNormalizer`; this slice tests the real principal factory's failure boundary and the mandatory representative `AuthenticateResult.Fail` adapter contract rather than adding an unused provider handler.

## Constitution Check

*GATE: Passed before Phase 0 research and re-checked after Phase 1 design.* Both `.specify/memory/constitution-framework.md` and `.specify/memory/constitution.md` were loaded in full.

| Gate | Requirement | Status |
|---|---|---|
| framework §2.1 dependency direction | Identity Abstractions remains framework-neutral with only ASP.NET Core abstractions; `Elsa.Api.FastEndpoints` points inward to it. Identity never references the transitional adapter. | PASS |
| framework §2.6.2 replacement contracts | `IPermissionEvaluator`, `IPermissionPolicyNameFormatter`, and `IPermissionCatalog` are documented as single replacements. Each has an explicit tagged `Replace*` path; pre-default untagged descriptors are rejected immediately and startup validation rejects zero, multiple, post-default, or tag-mismatched registrations with named diagnostics. Existing public interfaces remain source/binary compatible. | PASS (implementation obligation) |
| framework §2.6.1/§2.6.5 additive seams | `IPermissionContributor` and `IPermissionResourceHandler` remain intentional fan-in seams. The catalog owns aggregation/provenance; resource decisions are evaluated deterministically per permission member. | PASS |
| framework §2.7 adapter/bridge | Foundation Identity owns policy semantics and standard metadata; FastEndpoints owns only its thin `Policies(...)` adapter. | PASS |
| framework §2.8 extension methods | Three mechanical endpoint-convention extensions have far more than three consumers and contain no domain behavior; policy semantics stay in the provider/handlers. | PASS |
| framework §§2.16/2.17 library boundaries | Reuse the existing Identity Abstractions package and target its already ASP.NET-specific contracts at the .NET 10 `Microsoft.AspNetCore.App` shared framework. The centrally pinned 2.3 packages do not expose modern `IEndpointConventionBuilder`; the framework reference avoids mixing legacy and current ASP.NET assemblies. No new project, FastEndpoints dependency, or Elsa endpoint DSL is introduced. | PASS (implementation-corrected) |
| framework §2.21.1 golden rule | Existing routes, `ConfigurePermissions` call sites, wildcard administrator behavior, policy-provider fallback lifetime, token/session projection, and unrelated host policies remain covered. | PASS (test obligation) |
| framework §§2.22/2.22.1 docs/catalog | Update both affected `EXTENSION_POINTS.md` files and the authentication integrator guide, including replacement, additive, legacy, and failure contracts. | PASS (documentation obligation) |
| framework §2.23 tests | Contract, implementation, same-host HTTP, module registration/lifecycle, architecture, full build, and generated-map gates are specified. Tests are written failing-first. | PASS (test obligation) |
| framework §4.2 SemVer/replacement | Existing formatter interface, single requirement constructor/property, attribute, endpoint helper, and endpoint-base method remain. New metadata emits v1; legacy single names are accepted for one major-version deprecation window and documented. | PASS (replacement window recorded) |
| Elsa §E2.1 / ADR 0068 | FastEndpoints remains a transitional Layer-3 adapter; the new dependency does not change the domain tree and implements the accepted Minimal API/authorization ownership decision. | PASS |
| framework §2.24 closed pattern catalog | §2.24 is draft/unratified and is not used as a gate. The design nevertheless uses only existing adapter, replacement, and contribution mechanisms. | NOT A RATIFIED GATE |

**Result: PASS.** No constitutional violation requires Complexity Tracking. The pre-existing transport-adjacent synchronous direct-claim contexts are not hidden; issue #1356 owns their bounded asynchronous replacement after this bridge lands.

## Project Structure

### Documentation (this feature)

```text
specs/151-foundation-identity-permission-policy-bridge/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── permission-policy-contract.md
│   └── endpoint-adapter-contract.md
└── tasks.md              # generated by speckit-tasks after plan review
```

### Source Code (repository root)

```text
src/Elsa/Foundation/Identity/Abstractions/
├── Elsa.Foundation.Identity.Abstractions.csproj        # current Microsoft.AspNetCore.App framework contracts
├── FoundationIdentityOptions.cs                        # trusted runtime authentication types, empty by default
├── Authorization/
│   ├── AuthorizationContracts.cs                       # canonical keys, wildcard, requirements,
│   │                                                   # provider and per-member handler semantics
│   ├── PermissionPolicyCodec.cs                        # v1 codec + legacy parse result
│   ├── PermissionEndpointConventionBuilderExtensions.cs # RequirePermission/Any/All metadata
│   ├── NormalizedPrincipalValidator.cs                 # scheme + marker validation/filtering
│   └── PermissionAuthorizationMiddlewareResultHandler.cs # permission-only untrusted -> challenge
├── Extensions/FoundationIdentityServiceCollectionExtensions.cs
└── EXTENSION_POINTS.md

src/Elsa/Api/FastEndpoints/
├── Elsa.Api.FastEndpoints.csproj                       # -> Identity Abstractions
├── Abstractions/ElsaEndpointPermissions.cs             # one canonical wildcard/action policy
├── Abstractions/ElsaEndpoint*.cs                       # six bases: Policies, never Permissions
└── EXTENSION_POINTS.md

src/Elsa/Foundation/Identity/AspNetCoreIdentity/Services/
├── AspNetCoreIdentityPrincipalFactory.cs               # preserve trusted normalized marker
├── AspNetCoreIdentityUserClaimsPrincipalFactory.cs     # mark trusted cookie projection
└── IdentityClaimsProjector.cs                          # shared trusted projection marker
src/Elsa/Foundation/Identity/OpenIddict/Behavior/OpenIddictTokenService.cs # mark trusted token projection
src/Elsa/Foundation/Identity/AspNetCoreIdentity/Extensions/AspNetCoreIdentityServiceCollectionExtensions.cs # register factory/cookie runtime types
src/Elsa/Foundation/Identity/OpenIddict/Behavior/Extensions/OpenIddictBehaviorServiceCollectionExtensions.cs # register validation runtime type

src/Elsa/Modularity/Api/Authorization/ModuleManagementPermissions.cs
src/Elsa/Studio/Preferences/Api/StudioPreferencesPermissions.cs
docs/reference/authentication-architecture.md
docs/program-goals/first-party-rest-api-consolidation.md

tests/Elsa/Foundation/Identity/Tests/
├── AuthorizationContractsTests.cs
├── ClaimsNormalizationTests.cs
├── ReplacementContractRegistrationTests.cs
└── Api/PermissionEndpointAdapterIntegrationTests.cs

tests/Elsa/Api/FastEndpoints/Tests/ElsaEndpointPermissionsTests.cs
tests/Elsa/Modularity/Tests/PermissionCatalogOwnershipLifecycleTests.cs
tests/Elsa/Architecture/PermissionAuthorizationBoundaryTests.cs # Roslyn symbol/data-flow guard + mutations
```

**Structure Decision**: Keep all reusable permission policy identity, normalized-principal validation/transport result handling, metadata, catalog, and evaluation behavior in the existing Foundation Identity Abstractions package. Add a one-way project reference from the transitional FastEndpoints adapter. The six endpoint bases are the only production FastEndpoints edits; all 175 endpoint declarations retain their existing method calls. Catalog provenance is enriched at aggregation time and tested with two existing module contributors. No new shared endpoint framework or project is justified.

## Complexity Tracking

No constitution violations. Table intentionally empty.
