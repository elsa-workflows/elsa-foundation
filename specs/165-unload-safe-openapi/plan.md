# Implementation Plan: Unload-Safe OpenAPI Boundary

**Branch**: `codex/1392-unload-safe-openapi-boundary` | **Date**: 2026-08-16 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/165-unload-safe-openapi/spec.md`

## Summary

Define the first-party OpenAPI lifetime boundary as ordinary three-layer contract separation: API-visible request and response types live in stable `*.Api.Core` assemblies, while dynamically replaceable API assemblies contain mapping, binding, handlers, and adapters. Add a small ASP.NET Core endpoint convention that validates the completed endpoint metadata before publication and rejects collectible request/response types, metadata objects, methods, delegates, transformers, or serializer artifacts with owner-aware diagnostics. Preserve native ASP.NET Core API Explorer/OpenAPI generation and prove the boundary with a framework-only retention control, a shared-contract canary, real document generation, endpoint invocation, source-generated serialization, candidate rejection, and three repeated unload cycles.

The work unit establishes the reusable boundary and a representative first-party canary. The blocked Workflows Design and Runtime waves move their wire contracts into their own stable API Core packages as follow-up implementation inside those already-open waves. A serialized OpenAPI contribution store remains a future option for independently authored third-party plugins that cannot share stable contract assemblies; it is not needed for the first-party program and would require a separate §2.24/ADR decision.

## Technical Context

**Language/Version**: C# 14 on .NET 10 (`net10.0`; shared libraries retain their repository target-framework matrix where already multi-targeted)

**Primary Dependencies**: ASP.NET Core Minimal APIs, API Explorer, `Microsoft.AspNetCore.OpenApi` 10.0.10, System.Text.Json source generation, CShells/Nuplane dynamic module loading, `Elsa.Api.AspNetCore`

**Storage**: N/A; endpoint metadata and generated documents are in-memory lifetime artifacts

**Testing**: xUnit, ASP.NET Core TestServer, Roslyn-generated collectible test modules, real `IApiDescriptionGroupCollectionProvider` and `IOpenApiDocumentProvider`, bounded weak-reference collection evidence

**Target Platform**: ASP.NET Core hosts on Linux, Windows, and macOS; validation must not depend on OS-specific GC timing or private runtime APIs

**Project Type**: Shared endpoint convention library plus architecture/integration tests and documentation

**Performance Goals**: No request-path overhead; one bounded metadata validation per endpoint build; no additional document-generation pass for accepted endpoints

**Constraints**: Preserve exact HTTP/OpenAPI contracts; no private cache clearing, reflection mutation, sleeps, production forced GC, hidden operations, `object` schema substitution, or custom endpoint framework

**Scale/Scope**: All first-party dynamically unloadable Minimal API modules; immediate implementation is one shared boundary plus a representative canary, followed by owner-local API Core splits in program waves

## Constitution Check

*GATE: Passed before Phase 0 research and re-checked after Phase 1 design.*

- **Framework §2.1 / §2.3 — contract dependency envelope: PASS.** Stable API Core packages contain only wire models and zero/heavy-dependency-free contracts. ASP.NET/OpenAPI integration stays in `Elsa.Api.AspNetCore` or owner implementation packages.
- **Framework §2.7 — Adapter/Bridge: PASS.** The shared convention adapts standard endpoint metadata into a lifetime validation gate; it does not replace routing, binding, API Explorer, or OpenAPI.
- **Framework §2.8 / §2.17 — thin shared conventions only: PASS.** The convention is justified by multiple first-party owners and centralizes only one cross-owner invariant. Owner-local mapping and serialization remain local.
- **Framework §2.16 / §2.16.1 — project identity: PASS.** Later `*.Api.Core` projects are contracts-only seams and therefore explicitly exempt from minimum-size pressure. Existing public namespaces are preserved and moved types require forwarders from their former assemblies.
- **Framework §2.21.1 / §2.23 — golden rule and tests: PASS.** No behavioral test is removed. New logic-bearing validation receives direct branch coverage; the dynamic lifecycle uses real integration evidence and mutation bites.
- **Framework §2.22 — documentation: PASS.** The decision report and ADR define the boundary and owner obligations. No contributor/event catalog changes are introduced.
- **Framework §2.24 — sanctioned patterns: PASS WITH DRAFT WARNING.** §2.24 is draft and unratified. This design uses existing three-layer separation and Adapter/Bridge patterns. A serialized contribution store would be a broader structural pattern and is therefore deferred unless separately ratified.
- **Framework §3 / Elsa §E5 — Nuplane Strategy B: PASS.** Stable API contracts have the shared-contract/restart lifetime; handlers remain hot-replaceable and collectible.
- **Elsa §E2.9 — read-model placement: PASS.** Workflow API projections remain in the API sub-domain via `Elsa.Workflows.<Area>.Api.Core`; they are not moved into workflow state, runtime artifacts, or a generic domain Core.
- **ADR 0068 — bounded endpoint conventions: PASS.** The boundary is a thin, evidence-backed OpenAPI convention and does not recreate FastEndpoints or an Elsa endpoint DSL.
- **Security and compatibility: PASS.** Existing ownership, security-disposition, authorization, operation identity, and schema metadata remain intact; unsafe metadata fails before endpoint visibility.

## Project Structure

### Documentation (this feature)

```text
specs/165-unload-safe-openapi/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── openapi-lifetime-boundary.md
├── checklists/
│   └── requirements.md
└── tasks.md
```

### Source Code (repository root)

```text
src/Elsa/Api/AspNetCore/
├── EndpointConventionBuilderExtensions.cs
├── OpenApiLifetimeMetadata.cs
├── OpenApiLifetimeValidator.cs
└── UnsafeOpenApiMetadataException.cs

src/Elsa/Diagnostics/StructuredLogs/
└── Endpoints/StructuredLogsApi.cs          # representative already-safe first-party canary

tests/Elsa/Architecture/
├── OpenApiLifetimeBoundaryTests.cs         # public API + negative metadata validation
└── OpenApiLifetimeCollectibilityTests.cs   # real API Explorer/OpenAPI + three-cycle evidence

docs/adr/
└── 0069-openapi-contract-types-use-stable-api-core.md

docs/reports/
└── unload-safe-openapi-boundary-2026-08.md
```

**Structure Decision**: Extend the existing shared ASP.NET endpoint-metadata owner rather than introduce a new runtime or documentation project. Use Structured Logs as the production canary because its OpenAPI-visible types already come from stable Core/framework assemblies and its existing combined lifecycle proves native OpenAPI can release the implementation generation. Owner-specific API Core projects remain in their migration waves, which keeps #1392 focused on the reusable gate and proof.

## Complexity Tracking

No constitutional violation is required. The more general serialized-snapshot design was rejected for this work unit because first-party modules can use stable contract assemblies, while a custom document source would add a new publication model, schema format, merge algorithm, host endpoint, and lifecycle store without evidence that the sanctioned contract split is insufficient.

## Post-Design Constitution Re-check

The Phase 1 design retains the same result: no new architectural pattern, no heavy dependency in Core, no API read model moved into authored/runtime state, and no framework cache workaround. The only conditional follow-up is architectural: if a future independently collectible third-party module cannot participate in a stable contract lifetime, its serialized contribution proposal must go through framework §2.24.3 before adoption.
