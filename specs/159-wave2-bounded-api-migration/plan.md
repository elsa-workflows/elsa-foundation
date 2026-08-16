# Implementation Plan: Wave 2 Bounded API Migration

**Branch**: `codex/1368-wave2-minimal-apis` | **Date**: 2026-08-16 | **Spec**: [spec.md](spec.md)

## Summary

Replace exactly 13 first-party FastEndpoints registrations with four explicit module-owned Minimal API mappers. Preserve the existing domain services, routes, request/response records, diagnostics, polling, pagination, multipart/XML/JSON wire behavior, tenant scope, and Foundation authorization while replacing wildcard-only transition checks with catalog-owned action permissions. Freeze real FastEndpoints-before evidence first, compare the real migrated host through `Elsa.Api.Compatibility.Testing`, and exercise repeated route/DI/serializer/disposal release for every owner.

## Technical Context

**Language/Version**: C# / .NET 10 (`net10.0`)

**Primary Dependencies**: ASP.NET Core Minimal APIs and OpenAPI, CShells `IWebShellFeature`, Foundation Identity, existing owner services/models, xUnit/TestServer, `Elsa.Api.Compatibility.Testing`.

**Testing**: Immutable before HTTP/OpenAPI fixtures, real-host compatibility and authorization tests, owner manifest/security tests, mixed-host coexistence, repeated collectible lifecycle tests, affected feature suites, architecture/maps/build/E2E gates.

**Constraints**: Four named owners only; exactly 13 baseline entries removed; no shared endpoint DSL; no wildcard endpoint policy operand; no blanket unloadability waiver; Wave 1 rebase ratchets 156 to 143.

## Constitution Check

- Mapping stays in the owner feature packages; domain/core contracts remain framework-neutral.
- Foundation Identity remains the only permission authority. Catalog contributors provide action keys and implications; the wildcard is evaluator-level behavior.
- The approved CShells web-feature seam is used; no global discovery or application-root switch is introduced.
- Immutable before evidence is captured before endpoint deletion. Tests remain behavior gates rather than being removed.
- Framework §2.24 and Elsa §E2.9 are draft/provisional sections and are treated as review inputs, not ratified exceptions.

## Project Structure

```text
src/Elsa/Activities/Bpmn/Interchange/
├── ActivitiesBpmnInterchangeFeature.cs
└── Endpoints/BpmnInterchangeApi.cs
src/Elsa/Modularity/Api/
├── ModularityApiFeature.cs
└── Endpoints/ModularityApi.cs
src/Elsa/Workflows/ExecutionEvidence/
├── WorkflowsExecutionEvidenceFeature.cs
├── Authorization/ExecutionEvidencePermissionContributor.cs
└── Endpoints/ExecutionEvidenceApi.cs
src/Elsa3/Activities/Design/Import/
├── Elsa3ImportActivitiesFeature.cs
└── Endpoints/ReusableActivityImportApi.cs
tests/Elsa/Architecture/
├── Baselines/wave2-http-fastendpoints.json
├── Baselines/wave2-openapi-fastendpoints.json
└── Wave2*Tests.cs
docs/reports/wave-2-minimal-api-migration-2026-08.md
```

## Design

1. Convert each feature to `IWebShellFeature`, retain service registration, and map one explicit API entry point from `MapEndpoints`.
2. Use ordinary `MapGet`, `MapPost`, and `MapDelete` endpoints with explicit request delegates/context binding. Attach one owner, Minimal authoring, response/error metadata, operation IDs/tags, and a single `RequirePermission(action)` policy.
3. Reuse current BPMN importer/exporter, Modularity service, EvidencePolling/store, and Elsa3 operation service. Preserve all existing response status/error paths; retain raw upload streams and content length.
4. Add Execution Evidence catalog permissions and implications. Keep Modularity's existing contributor. Prove wildcard through the Foundation evaluator, not mapping metadata.
5. Replace the temporary legacy capture with differential after-host tests loading committed before fixtures. Add manifest, authorization, normalized identity, tenant isolation, coexistence, and four-owner repeated collectibility evidence.
6. Delete only the 13 FE classes and owner FE references after before evidence and after-host checks exist. Rebase Wave 1 before the final transition ratchet and review the generated maps.

## Risks and Mitigations

- OpenAPI schema/media-type drift: capture the actual document and assert operation/schema projections.
- Raw multipart or XML binding drift: include deterministic multipart and BPMN XML cases.
- Permission widening: reconcile endpoint metadata with catalog ownership and exercise exact/implied/wildcard/normalized cases.
- Collectible owner retention: release route, DI, serializer, OpenAPI, and disposal references and repeat real cycles.
- Main nightly issue #1323: report separately from owner regressions.
