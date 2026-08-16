# Implementation Plan: Workflow-authored Dynamic HTTP Publication

**Branch**: `codex/1366-dynamic-http-metadata` | **Date**: 2026-08-16 | **Spec**: [spec.md](spec.md)

## Summary

Extend the existing workflow HTTP route projection with immutable ownership/security metadata, deterministic owner-aware collision validation, and an optional generation snapshot/lease seam. Preserve the existing `IRouteTable` contract and custom middleware compatibility while making the production `RouteTable` publish candidates atomically and keeping matched requests on their exact snapshot through drain.

## Technical Context

**Language/Version**: C# / .NET 10 (`net10.0`)
**Primary Dependencies**: ASP.NET Core route templates, existing Elsa HTTP Core/Runtime HTTP contracts, xUnit
**Storage**: In-memory per-shell route snapshots; no persistence changes
**Testing**: Elsa.Http unit tests, Runtime HTTP resolver/synchronizer tests, Activities HTTP middleware/integration tests, architecture/maps checks
**Constraints**: Preserve public routes, existing middleware authorization behavior, and third-party `IRouteTable` implementations; do not depend on CShells internals or create a new endpoint DSL

## Constitution Check

- **Layering**: Dynamic metadata and snapshot contracts remain in the HTTP contract layer; resolver and publication behavior remain in their existing feature projects.
- **Security**: Publication records a disposition but does not authenticate callers or duplicate Foundation Identity evaluation.
- **Atomicity**: Candidate construction and validation happen before one snapshot publication; rejected candidates do not mutate the live cache.
- **Compatibility**: New snapshot behavior is discovered through an optional interface so existing route-table doubles continue to compile.
- **Subtractive scope**: This slice hardens the existing dynamic route model; it does not migrate static endpoint owners or rebuild FastEndpoints conventions.
- **Provisional material**: Framework constitution §2.24 remains draft/provisional; ADR 0068 and issue #1366 are the approved direction.

## Project Structure

```text
src/Elsa/Http/Core/
├── Contracts/IRouteTableSnapshotProvider.cs
├── Models/HttpRouteData.cs
├── Models/HttpRouteOwnershipMetadata.cs
├── Models/HttpRouteSecurityDispositionMetadata.cs
└── Models/HttpRouteTableSnapshot.cs
src/Elsa/Http/Services/
├── HttpRouteManifestValidator.cs
└── RouteTable.cs
src/Elsa/Http/
└── HttpRouteManifestProvider.cs (ASP.NET/CShells composition adapter registered by HttpFeature)
src/Elsa/Workflows/Runtime/Http/Services/
└── HttpEndpointRoutesResolver.cs
src/Elsa/Activities/Http/Middleware/
└── HttpEndpointMiddleware.cs
tests/Elsa/Http/Tests/
└── DynamicHttpRoutePublicationTests.cs
tests/Elsa/Workflows/Runtime/Http/Tests/
└── HttpEndpointRoutesResolverTests.cs (metadata/method assertions)
tests/Elsa/Activities/Http/IntegrationTests/
└── ... (generation lease regression)
tests/Elsa/Architecture/
└── DynamicHttpRouteCompositionTests.cs (real root-to-shell activation/reload and collectibility)
```

## Design Decisions

1. Use small typed HTTP-core metadata records as the lower-layer projection for dynamic routes; static ASP.NET endpoint metadata remains owned by `Elsa.Api.AspNetCore`, while the Elsa.Http integration adapter registered by `HttpFeature` supplies the complete host/current-shell manifest without a generic API-to-HTTP dependency.
2. Aggregate workflow methods into one route entry while preserving method overlap semantics; missing method metadata remains the compatibility wildcard.
3. Validate route template equivalence by canonicalizing parameter names while retaining constraints/defaults for diagnostics and matching semantics.
4. Publish a complete immutable snapshot under a per-shell gate and expose request leases through `IRouteTableSnapshotProvider`, leaving `IRouteTable` source compatibility intact.
5. Resolve workflow authorization options into one route disposition record for inventory; the established `IHttpEndpointAuthorizationHandler` remains the runtime enforcement authority.

## Verification

- Focused HTTP route publication, resolver, and middleware tests.
- Full affected test projects and architecture suite.
- `dotnet run --project tools/maps/Elsa.Maps.Generator --no-restore -- check`.
- Clean worktree and diff review before local commit.
