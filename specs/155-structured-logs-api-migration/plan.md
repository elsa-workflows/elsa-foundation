# Implementation Plan: Structured Logs API Minimal API Migration

**Branch**: `codex/1349-structured-logs-minimal-api` | **Date**: 2026-08-16 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/155-structured-logs-api-migration/spec.md`

## Summary

Migrate the complete three-operation Structured Logs surface from FastEndpoints to an explicit `MapStructuredLogsApi(IEndpointRouteBuilder)` entry point on the existing `IWebShellFeature`. Preserve the option-driven recent, sources, and stream routes; exact query and JSON behavior; SSE headers, framing, 15-second heartbeat, opaque replay, durable-only payload ordering, polling fallback, cancellation, and bounded cleanup; and the existing wildcard-or-`Diagnostics:StructuredLogs` authorization contract. Capture a real FastEndpoints HTTP/SSE/OpenAPI baseline before replacement, prove mixed-host authorization through the same Foundation evaluator, and remove the production FastEndpoints/SSE-helper dependency. The collectible test will generate the actual API document, inspect ASP.NET OpenAPI's operation-context cache for module-owned metadata, and either demonstrate release with stable metadata or record a GC-root-backed boundary that blocks dynamic documentation.

## Technical Context

**Language/Version**: C# / .NET 10 (`net10.0`)

**Primary Dependencies**: ASP.NET Core Minimal APIs, HTTP streaming and OpenAPI; CShells `IWebShellFeature`; Foundation Identity authorization policies/catalog; existing Structured Logs Core stores, replay cursors and live feed; xUnit; Microsoft.AspNetCore.TestHost; `Elsa.Api.Compatibility.Testing`

**Storage**: Existing in-memory, Groundwork, and EF compatibility implementations of `IStructuredLogStore`; deterministic in-memory and shared-writer fixtures for API evidence

**Testing**: Existing unit/provider suites plus real TestServer HTTP/authorization/SSE tests, immutable FastEndpoints-before observations, route/OpenAPI comparison, mixed Minimal API/FastEndpoints coexistence, repeated lifecycle/cleanup checks, and collectible `AssemblyLoadContext` evidence

**Target Platform**: Cross-platform ASP.NET Core hosts running CShells, including shell generations that may load endpoint modules into collectible contexts

**Project Type**: Existing modular ASP.NET Core library and existing backend test project

**Performance Goals**: Durable polling retains the configured maximum delay; an idle stream writes the legacy heartbeat at 15 seconds; cancellation and pending-enumerator cleanup remain bounded; no request-time endpoint discovery or permission-claim parsing is added

**Constraints**: Preserve public HTTP/OpenAPI/SSE behavior; use an explicit `RequestDelegate` boundary; local feed items remain wake hints only; durable storage remains payload/order authority; do not introduce a shared streaming framework; remove every production Structured Logs FastEndpoints dependency and transition entry

**Scale/Scope**: Three configurable GET routes, one stable permission, one JSON entry model, one source model, opaque cursor replay, one representative transitional FastEndpoints route, and module-specific compatibility/collectibility evidence

## Constitution Check

*GATE: Passed before Phase 0 research; re-checked after Phase 1 design.*

- **Layering and domain boundary**: mapping, streaming response mechanics, serialization, and permission contribution remain in `Elsa.Diagnostics.StructuredLogs`. Core store/feed/models/options do not acquire CShells, Foundation Identity, FastEndpoints, or OpenAPI dependencies.
- **Explicit composition**: the accepted ADR's module-owned mapper is invoked through the existing CShells `IWebShellFeature` seam. No process-global endpoint discovery, new registry, application-root switch, or Elsa endpoint DSL is introduced.
- **Framework honesty**: mappings produce ordinary ASP.NET Core endpoints, authorization metadata, OpenAPI metadata, and response streaming. The module-local SSE loop exists only to preserve the current framing/lifecycle contract after removing the FastEndpoints helper.
- **Golden rule**: existing capture, store, replay, feed, persistence, and feature tests remain. Immutable real-host HTTP/SSE/OpenAPI evidence is captured before legacy removal; no existing test objective is deleted.
- **Security and least privilege**: Foundation Identity is the authorization authority. `Diagnostics:StructuredLogs` receives one module catalog owner and no implication; wildcard remains a grant rather than a catalog entry. Handlers do not inspect claims or infer permissions from paths.
- **Feature documentation**: the Structured Logs documentation and final report will list its mapper, services, permission/catalog ownership, three configurable routes, stream lifecycle, compatibility evidence, and transition scope.
- **Dependency direction and project count**: no new production or test project is introduced. The existing feature package replaces its FastEndpoints references with the narrow CShells, Foundation, ASP.NET Core, and compatibility references it consumes.
- **Streaming boundary**: the writer remains module-local because ADR 0068 requires three demonstrated consumers before a shared streaming convention. Existing general FastEndpoints helpers are not moved wholesale into another shared framework.
- **Collectibility**: handlers use explicit `RequestDelegate`s. OpenAPI metadata uses only host/framework or stable Core contract types and no module-owned operation-transformer delegate. Tests inspect the framework operation-context cache, release route/service/stream/document owners, and retain only weak-reference/string diagnostics.
- **Provisional material**: framework constitution §2.24 remains draft/provisional and is not treated as ratified. ADR 0068, the named program, and the existing CShells seam provide the approved direction.
- **Post-design re-check**: passed. The design adds no constitution exception, project-count waiver, domain redesign, test deletion, or shared abstraction beyond approved seams.

## Project Structure

### Documentation (this feature)

```text
specs/155-structured-logs-api-migration/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── authorization-and-coexistence-contract.md
│   ├── collectibility-and-openapi-contract.md
│   └── structured-logs-http-sse-contract.md
└── tasks.md
```

### Source Code (repository root)

```text
src/Elsa/Diagnostics/StructuredLogs/
├── Authorization/
│   └── StructuredLogsPermissionContributor.cs
├── Endpoints/
│   ├── StructuredLogsApi.cs
│   ├── StructuredLogEntrySerializer.cs
│   ├── StructuredLogFilterBinder.cs
│   ├── StructuredLogSseFormatter.cs
│   └── StructuredLogSseWriter.cs
├── StructuredLogsFeature.cs
├── Elsa.Diagnostics.StructuredLogs.csproj
└── README.md

tests/Elsa/Diagnostics/StructuredLogs/Tests/
├── Baselines/
│   ├── structured-logs-http-fastendpoints.json
│   └── structured-logs-openapi-fastendpoints.json
├── Support/
│   ├── StructuredLogsApiHost.cs
│   ├── StructuredLogsCollectibleFixture.cs
│   └── StructuredLogsCompatibilityCases.cs
├── StructuredLogsApiAuthorizationTests.cs
├── StructuredLogsApiCollectibilityTests.cs
├── StructuredLogsApiCoexistenceTests.cs
├── StructuredLogsApiContractTests.cs
├── StreamEndpointTests.cs
└── Elsa.Diagnostics.StructuredLogs.Tests.csproj

tests/Elsa/Architecture/Baselines/
└── fastendpoints-transition-exceptions.json

docs/reports/
└── structured-logs-minimal-api-migration-2026-08.md
```

**Structure Decision**: keep mapping, query translation, SSE mechanics, serialization, and permission contribution in the existing Structured Logs feature package; extend the existing test project with one deterministic real host and shared compatibility cases. Reuse the program's compatibility/collectibility infrastructure and explicit-request-delegate pattern. Delete the three legacy endpoint classes and transition records only after replacement evidence passes.

## Design Decisions

1. Change `StructuredLogsFeature` from `FastEndpointsFeatureBase` to `IWebShellFeature`. Preserve all current service registrations, add `StructuredLogsPermissionContributor`, and have `MapEndpoints` delegate to public `StructuredLogsApi.MapStructuredLogsApi(IEndpointRouteBuilder)`.
2. Map the configured recent, sources, and stream paths with explicit `RequestDelegate` handlers. Resolve query values, services, options, request headers, response, and cancellation from `HttpContext`; do not use typed handler signatures that can populate process-wide request-delegate caches with collectible module types.
3. Apply owner `Elsa.Diagnostics.StructuredLogs`, Minimal API authoring metadata, and canonical `RequireAnyPermission("*", "Diagnostics:StructuredLogs")` to all routes. Add a module contributor for the stable diagnostics permission with no implication.
4. Freeze the real FastEndpoints contract before deleting endpoints: route manifests for default and custom paths; ordinary HTTP observations for recent/sources; bounded SSE observations for validation, replay, first entry and heartbeat; and operation projections from the actual ASP.NET Core OpenAPI document.
5. Preserve the dedicated `StructuredLogEntrySerializer` so recent and SSE payloads retain their exact web/camel-case shape and PascalCase enum values. Do not replace it with ambient endpoint serialization without evidence.
6. Preserve `StructuredLogFilterBinder` and pass raw query values with the same conversion rules. Add differential cases for missing, blank, repeated, mixed-case, zero, negative, invalid and culture-sensitive values so a framework binding change cannot broaden queries.
7. Keep `StructuredLogSseFormatter` framework-neutral by removing `ISseStreamFormatter<T>` while retaining `Format`, `FormatEntry`, `FormatDropped`, and `Heartbeat`. Do not expose drop frames from the durable-tail endpoint merely because the formatter supports them.
8. Implement a module-local `StructuredLogSseWriter` using ASP.NET Core response primitives. Preserve the current 15-second heartbeat, five-second pending-`MoveNextAsync` cleanup bound, per-frame flush, cancellation propagation, and safe disposal behavior. Avoid extracting a shared SSE framework in this work unit.
9. Preserve `StreamEndpoint`'s durable-tail algorithm: capture or parse a durable cursor, subscribe for wake hints, perform and validate the first durable read before starting SSE, emit only bounded durable pages, poll at least every configured interval, and degrade to polling when the local feed fails or completes.
10. Start SSE only after filter/cursor/first-page validation. Set status `200`, content type `text/event-stream`, `Cache-Control: no-cache`, `Connection: keep-alive`, and `X-Accel-Buffering: no`, flush once, then run the writer. Preserve the generic `409` cursor response and cancellation cleanup.
11. Prove real HTTP lifecycle behavior rather than only direct handler invocation: initial boundary race, `Last-Event-ID` resume, remote-only writer, filter, invalid/NUL/wrong-binding cursor, idle heartbeat with an injectable test interval, disconnect, feed failure, cancellation-ignoring enumerator, slow reader, and repeated cleanup.
12. Prove coexistence with one real secured FastEndpoints test route and an instrumented Foundation evaluator. Both authoring models must reach the same evaluator instance for exact, wildcard, missing, adjacent, anonymous, and untrusted principals.
13. Keep OpenAPI metadata collectible-safe: use `RequestDelegate.Invoke` as the stable description method, stable Core response model types where the legacy document exposes schemas, framework-owned metadata records, and no module-defined operation-transformer delegate or `MethodInfo`.
14. In the collectible fixture, reflect the keyed ASP.NET `OpenApiDocumentService` and inspect `_operationTransformerContextCache`. Record whether cached `ApiDescription.ActionDescriptor.EndpointMetadata` contains module-owned `Type`, `MethodInfo`, or delegate references. Compare no-transformer/stable-type stages, then generate the actual document and release all owners.
15. If the context still remains after stable metadata and disposal, capture `dotnet-dump gcroot` evidence for the collectible context/type before attributing the root definitively. Keep dynamic OpenAPI support blocked or document a host-owned serialized-contract boundary; do not weaken the test or claim unload safety from aggregate memory.
16. Remove the three transition-exception entries, production FastEndpoints project/package references, endpoint classes, base type, and shared SSE-helper use only after immutable comparisons and replacement tests pass. Leave remaining modules and the shared FastEndpoints helper untouched.
17. Publish a streaming-migration report covering HTTP/OpenAPI parity, SSE lifecycle, authorization/catalog, coexistence, production dependency removal, framework-cache inspection, collectibility, remaining risks, and a proceed/revise/stop recommendation for the remaining waves.

## Complexity Tracking

No constitution violations require justification.
