# Implementation Plan: Diagnostics — OpenTelemetry (Ingestion, Live Streaming & Query)

**Branch**: `sfmskywalker/port-otel-diagnostics-backend` | **Date**: 2026-06-18 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/074-diagnostics-opentelemetry/spec.md`

## Summary

Port the OpenTelemetry diagnostics capability from elsa-core (`Elsa.Diagnostics.OpenTelemetry`) into
elsa-foundation, adapted to foundation architecture. This slice (slice 3 of the
diagnostics-observability-readiness program goal) delivers the **backend**: OTLP/HTTP protobuf
ingestion of traces/metrics/logs, on-ingest redaction, capacity-bounded in-memory retention per
signal, a query API (resources, traces + single-trace detail, metrics, logs, storage diagnostics,
collector configuration), and a multi-subscriber live feed with backpressure/drop signalling exposed
over **Server-Sent Events (SSE)**. Durable persistence (slice 4) and the studio UI are separate
follow-up slices that reuse the same `.Core` contracts.

Technical approach: a two-package domain (`Elsa.Diagnostics.OpenTelemetry.Core` contracts/models/
options + `Elsa.Diagnostics.OpenTelemetry` feature/impl) registered as a CShells shell feature
(`FastEndpointsFeatureBase`, name `DiagnosticsOpenTelemetry`). The OTLP/HTTP collector, the seven
query endpoints, and the SSE `stream` are all **FastEndpoints**, auto-mapped via the existing
`app.MapShells()`, so there is **no host hub/route wiring** in `Program.cs` (the host only adds the
feature assembly to the CShells list + the shell entry). The protobuf wire format is decoded by a
self-contained, dependency-free hand-rolled parser (`OtlpHttpProtobufParser` + an internal
`ProtobufReader`) ported verbatim — **no protobuf NuGet**. Each pipeline role
(ingestor/store/live-feed/redactor/source-registry/provider/collector-config) is a separate `.Core`
contract registered with `TryAdd*`, so slice 4 can replace just the store.

### Deviations from the elsa-core source

1. **SSE over SignalR** (mandated). The source streamed live telemetry via a SignalR hub
   (`OpenTelemetryHub` + `OpenTelemetrySubscriptionManager` + `IOpenTelemetryClient`). This port drops
   SignalR entirely and replaces it with an SSE `stream` endpoint mirroring the Structured Logs slice
   — native `EventSource` reconnect, no `@microsoft/signalr` in studio, one transport pattern across
   diagnostics domains. Because OTEL stream items carry no monotonic sequence, the stream offers no
   `Last-Event-ID` resume (the client resumes the live tail).
2. **OTLP collector as FastEndpoints** (engineering decision). The source mapped the collector via
   `EndpointRouteBuilderExtensions`/`IWebShellFeature`. This port implements the collector as
   FastEndpoints endpoints (consistent with the rest of the foundation, auto-mapped via
   `app.MapShells()`), avoiding a new `CShells.AspNetCore.Abstractions` host-plumbing dependency. The
   parser and `OtlpIngestionSecurity` are ported verbatim and reused by the FastEndpoints ingestion
   endpoints; `EndpointRouteBuilderExtensions`, `MapOpenTelemetryHub`, and the gRPC collector stub are
   not ported.
3. **gRPC deferred** (source parity). `EnableGrpc` defaults to `false` and no gRPC route is mapped;
   the option + disabled-reason are retained.

## Technical Context

**Language/Version**: C# / .NET 10 (`net10.0`), nullable + implicit usings enabled (repo default).

**Primary Dependencies**:
- `.Core`: `Microsoft.Extensions.Options` only (§2.1 layer-1 envelope) — models/contracts/options are
  otherwise BCL-only; the OTLP parser is a pure byte-span hand-roll.
- Feature: `CShells.Abstractions`, `CShells.FastEndpoints.Abstractions`, `Elsa.Api.FastEndpoints`
  (project ref), the `Microsoft.AspNetCore.App` framework reference (for `HttpContext`/SSE), and
  `Elsa.Platform.PackageManifest.Generator` (PrivateAssets). **No SignalR, no protobuf NuGet, no gRPC.**

**Storage**: In-memory capacity-bounded ring buffers only this slice (default impl of
`IOpenTelemetryStore`). EFCore-based durable persistence deferred to slice 4 implementing the same
`.Core` contract.

**Testing**: xUnit + `Microsoft.NET.Test.Sdk`, matching existing `tests/Elsa/**/Tests` projects. The
internal parser is exercised via `InternalsVisibleTo`. No integration tests (§2.23.6); the SSE/feature
surfaces are covered by unit tests + a manual ingest→query→stream smoke run.

**Target Platform**: ASP.NET Core server hosts (Elsa.Server now; host-agnostic feature).

**Project Type**: Backend .NET class libraries + a CShells feature contributing an ASP.NET surface.

**Performance Goals**: Ingested batch visible to a subscriber < 1s under normal load (SC-002); ingest→
query round-trip within the same interaction (SC-001).

**Constraints**: Diagnostics memory bounded by configured per-signal capacities under burst (SC-003);
ingestion must not block the host; no behavioural change when disabled (SC-005, FR-007).

**Scale/Scope**: Push-based OTLP/HTTP ingestion from the host's own exporter; bounded buffers
(defaults: 5k traces / 25k spans / 25k metric points / 10k logs / 500 resources); concurrent live
subscribers with per-subscriber bounded channels.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Gate | Requirement | Status / How met |
|---|---|---|
| §2.1 Three-layer per feature | Core contracts + feature impl; Core envelope is MS abstractions only | PASS — `Elsa.Diagnostics.OpenTelemetry.Core` (models/contracts/options) references only `Microsoft.Extensions.Options`; `Elsa.Diagnostics.OpenTelemetry` holds ingestion, store, live feed, redactor, endpoints, feature class. |
| §2.2 Domain-only naming | No `Features.*`/`Modules.*` segments; `.Core` + bare impl + `Feature` type suffix allowed | PASS — packages `Elsa.Diagnostics.OpenTelemetry[.Core]`; type `OpenTelemetryFeature`. |
| §2.17 Duplication beats dependency / §2.20 rule-1 | No premature umbrella / provider stubs | PASS — single in-memory impl in the feature package; no empty `.Api`/`.Persistence` stubs created now (persistence is slice 4). |
| §2.19 Feature identity | Stable `name`, used as options binding key, unique | PASS — `name: "DiagnosticsOpenTelemetry"`; options projected from manifest settings. |
| §2.20 Provider decomposition | Roles behind `.Core` contracts; in-memory is the default impl; provider split deferred | PASS — `IOpenTelemetryStore`/`...LiveFeed`/`...Ingestor`/`...Redactor`/`...SourceRegistry`/`...Provider`/`ICollectorConfigurationProvider` are the seams (all `TryAdd*`); EFCore store is slice 4. |
| §2.20 rule-2 Dependency envelope | No meta-packages; envelope reflects use | PASS — collector/SSE use the pinned ASP.NET framework ref; no SignalR/protobuf/gRPC pulled in. |
| §2.22.1 Domain extension-points catalog | Domain `EXTENSION_POINTS.md` | DONE — `src/Elsa/Diagnostics/OpenTelemetry/EXTENSION_POINTS.md`. |
| §2.22.2 Repo-wide index | Update root `EXTENSION_POINTS.md` | DONE — added OpenTelemetry row under Diagnostics. |
| §2.23.1 Feature-registration test | Build SP, assert every registered service resolves | DONE — `OpenTelemetryFeatureTests`. |
| §2.23.2 Per-impl branch-covered tests | Each logic-bearing impl, every branch | DONE — parser, ring buffer, store trace/metric/log queries, redactor, collector config, ingestion security, SSE formatter, filter binder, live-feed filter/backpressure. |
| §2.23.3 Visibility | Feature class `public` non-sealed; impls appropriately scoped | PASS — `OpenTelemetryFeature` public; endpoints/serializers internal; the OTLP parser internal (verbatim from source) with `InternalsVisibleTo` for tests. |
| §2.23.5 Exception boundaries | Wrap infra exceptions in domain exceptions; never throw out of the ingestion path | PASS — malformed protobuf → `InvalidDataException` → `400`; oversize body → `413`; query parse failures → `InvalidTelemetryQueryException` → `400`. |

**Result**: No violations requiring Complexity Tracking. SSE adds no new dependency (plain HTTP over
the already-referenced ASP.NET framework), and the in-memory store/feed are the default impls of
`.Core` contracts, so the persistence-provider split is correctly deferred per §2.20 rule-1.

## Project Structure

### Documentation (this feature)

```text
specs/074-diagnostics-opentelemetry/
├── plan.md              # This file
└── spec.md              # Feature specification
```

### Source Code (repository root)

```text
src/Elsa/Diagnostics/OpenTelemetry/
├── Core/
│   └── Elsa.Diagnostics.OpenTelemetry.Core.csproj    # models, contracts, options (MS.Options only)
│       ├── Models/        OpenTelemetryModels, OpenTelemetryContracts (resources/traces/spans/metrics/logs/filters/results/stream item)
│       ├── Contracts/     IOpenTelemetryStore, IOpenTelemetryLiveFeed, IOpenTelemetryIngestor,
│       │                  IOpenTelemetryRedactor, IOpenTelemetrySourceRegistry, IOpenTelemetryProvider,
│       │                  ICollectorConfigurationProvider
│       └── Options/       OpenTelemetryDiagnosticsOptions
├── EXTENSION_POINTS.md
├── README.md
└── Elsa.Diagnostics.OpenTelemetry.csproj             # feature impl + ASP.NET surface (excludes Core/** globs)
    ├── OpenTelemetryFeature.cs                         # FastEndpointsFeatureBase (name "DiagnosticsOpenTelemetry")
    ├── Ingestion/   OtlpHttpProtobufParser (+ internal ProtobufReader), OtlpIngestionSecurity   # verbatim
    ├── Providers/InMemory/  RingBuffer, InMemoryOpenTelemetryStore, InMemoryOpenTelemetryLiveFeed
    ├── Services/    OpenTelemetryIngestor, OpenTelemetryRedactor, OpenTelemetrySourceRegistry,
    │                DefaultOpenTelemetryProvider, CollectorConfigurationProvider
    ├── Extensions/  ServiceCollectionExtensions (AddOpenTelemetryDiagnosticsServices, TryAdd*)
    └── Endpoints/
        ├── OpenTelemetry/{Resources,Traces,Trace,Metrics,Logs,Storage,CollectorConfiguration}/Endpoint.cs  # query API
        ├── Ingestion/{Traces,Metrics,Logs}IngestionEndpoint + OtlpIngestionEndpointBase                    # OTLP collector
        └── StreamEndpoint + OpenTelemetrySseFormatter + OpenTelemetryStreamItemSerializer +
            OpenTelemetryTraceFilterBinder + InvalidTelemetryQueryException                                  # SSE

tests/Elsa/Diagnostics/OpenTelemetry/Tests/
└── Elsa.Diagnostics.OpenTelemetry.Tests.csproj
```

**Structure Decision**: Two packages under the existing `Diagnostics` domain root, mirroring the
Structured Logs slice (Core + feature). The ASP.NET surface is all FastEndpoints (3 collector + 7
query + 1 SSE stream), auto-mapped by the existing `app.MapShells()`. Host wiring is limited to adding
the feature assembly to the CShells list in `Program.cs`, a `ProjectReference`, the `Elsa.Server.slnx`
entries, and `"DiagnosticsOpenTelemetry": {}` in `shells.json` — no hub/route extension method
(that was only needed under the rejected SignalR/`IWebShellFeature` options). Endpoints live in the
feature package (not a premature `.Api` split, §2.20 rule-1).

## Complexity Tracking

> No constitution violations to justify. SSE is plain HTTP over the already-referenced ASP.NET
> framework (no new dependency envelope); the OTLP parser is a self-contained hand-roll (no protobuf
> NuGet); the in-memory store/feed are the default impls of `.Core` contracts, so the
> persistence-provider split is correctly deferred to slice 4 per §2.20 rule-1.
