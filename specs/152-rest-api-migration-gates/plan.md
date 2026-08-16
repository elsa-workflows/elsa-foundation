# Implementation Plan: REST API Migration Compatibility and Authoring Gates

**Branch**: `codex/1346-rest-api-migration-gates` | **Date**: 2026-08-15 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/152-rest-api-migration-gates/spec.md`

## Summary

Build reusable, framework-neutral evidence and architecture gates for the First-party REST API Consolidation program. A thin ASP.NET Core metadata package will describe endpoint ownership and non-permission security dispositions. A shared test package will inventory actual `EndpointDataSource` output, capture HTTP and consumed OpenAPI behavior, compare exact approved differences, reconcile FastEndpoints registrations against a bounded transition registry, validate permission-catalog ownership, and exercise collectible endpoint modules with weak-reference evidence. This slice establishes migration safety without migrating a production module.

## Technical Context

**Language/Version**: C# / .NET 10 (`net10.0`)

**Primary Dependencies**: ASP.NET Core endpoint routing and authorization, Foundation Identity abstractions, FastEndpoints transition adapter, xUnit, Shouldly, Microsoft.AspNetCore.TestHost, Roslyn

**Storage**: Deterministic JSON baselines committed under `tests/Elsa/Architecture/Baselines/`; no runtime persistence

**Testing**: xUnit unit, architecture, TestServer HTTP/OpenAPI compatibility, mutation fixtures, and collectible `AssemblyLoadContext` lifecycle tests

**Target Platform**: Cross-platform ASP.NET Core hosts; collectible modules on supported .NET runtimes

**Project Type**: Multi-project .NET library and test infrastructure

**Performance Goals**: Ten identical manifest captures are byte-for-byte stable; bounded compatibility and unload checks remain suitable for pull-request CI

**Constraints**: Preserve public HTTP/JSON contracts; use standard ASP.NET Core endpoint metadata; do not introduce an endpoint DSL; no production module migration; no automatic baseline acceptance; no wildcard endpoint permission

**Scale/Scope**: All enabled first-party endpoints in representative hosts, the current FastEndpoints transition surface, and reusable gates for #1347–#1349

## Constitution Check

*GATE: Passed before Phase 0 research; re-checked after Phase 1 design.*

- **Layering**: `Elsa.Api.AspNetCore` is a Layer 2 infrastructure helper. It uses the ASP.NET Core framework reference and does not move framework types into Core or Domain.
- **Deep modules and reuse**: the production package exposes only typed metadata and standard builder conventions. The test package centralizes evidence needed by at least three planned migrations instead of duplicating host setup.
- **Framework honesty**: FastEndpoints remains an explicit transition adapter. Runtime evidence is read from standard ASP.NET Core endpoints, and no custom endpoint builder or routing abstraction is added.
- **Golden rule**: HTTP and consumed OpenAPI evidence protect observable contracts. Differences require exact, reviewed records.
- **Security**: Foundation Identity remains the sole permission-policy and catalog source. Other dispositions are explicit endpoint metadata, not path middleware.
- **Dependency direction**: no Runtime-to-Design production reference is introduced. Roslyn and host-composition dependencies remain test-only.
- **Subtractive change**: the new gate replaces the existing partial route-regex security scan rather than creating a second authority.
- **Provisional material**: framework constitution §2.24 is draft/provisional and is treated as architectural context, not a ratified gate. ADR 0068 and the program issue define this work unit's approved direction.
- **Post-design re-check**: passed. The selected projects and contracts preserve these boundaries; no constitution exception or complexity waiver is required.

## Project Structure

### Documentation (this feature)

```text
specs/152-rest-api-migration-gates/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── compatibility-evidence-contract.md
│   ├── endpoint-metadata-contract.md
│   └── transition-exception-contract.md
└── tasks.md
```

### Source Code (repository root)

```text
src/Elsa/Api/AspNetCore/
├── Elsa.Api.AspNetCore.csproj
├── EndpointOwnershipMetadata.cs
├── EndpointSecurityDispositionMetadata.cs
└── EndpointConventionBuilderExtensions.cs

src/Elsa/Api/FastEndpoints/
└── ... endpoint bases enrich standard endpoint metadata

tests/Elsa/Api/Compatibility/Testing/
├── Elsa.Api.Compatibility.Testing.csproj
├── Manifests/
├── Http/
├── OpenApi/
├── Comparison/
└── Collectibility/

tests/Elsa/Architecture/
├── EndpointArchitectureTests.cs
├── EndpointSecurityTests.cs
├── FastEndpointsTransitionTests.cs
├── PermissionOwnershipTests.cs
├── CollectibleEndpointTests.cs
└── Baselines/
    ├── endpoint-manifest.json
    ├── fastendpoints-transition-exceptions.json
    └── rest-compatibility-approved-differences.json
```

**Structure Decision**: Keep the production seam deliberately small and standard. Put capture, comparison, source reconciliation, and collectible fixtures in reusable test infrastructure; keep policy enforcement and committed baselines in the architecture test project. Existing feature projects contribute granular permissions and typed metadata through their normal endpoint registration paths.

## Design Decisions

1. Build the canonical manifest from the host's actual `EndpointDataSource`; use a Roslyn source scan only to reconcile FastEndpoints registrations and transition exceptions.
2. Represent route ownership and security disposition as typed standard endpoint metadata. Permission disposition continues to use Foundation Identity permission metadata and canonical policies.
3. Compare canonical HTTP observations and a consumed OpenAPI projection. The OpenAPI harness accepts a supplied document and does not add a transient FastEndpoints Swagger dependency.
4. Use exact approved-difference keys containing endpoint, method, facet, expected value, actual value, owner, reason, and follow-up. Broad ignores and environment-driven baseline updates are forbidden.
5. Replace no-argument `ConfigurePermissions()` calls with granular feature-owned permissions and ensure each consumed permission has exactly one catalog owner. The administrative wildcard remains a grant only.
6. Build collectible fixtures in-memory with Roslyn, publish route/service/serializer references in isolated stages, and return only weak references from a non-inlined helper before forcing collection.

## Complexity Tracking

No constitution violations require justification.
