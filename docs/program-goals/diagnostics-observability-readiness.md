# Diagnostics Observability Readiness

Status: active.

Area: diagnostics/observability subsystem port — structured logs and OpenTelemetry across `elsa-foundation` (backend) and `elsa-foundation-studio` (frontend).

Steward(s): Joey plus active architects/agents.

## Purpose

Coordinate porting the diagnostics observability subsystem from `elsa-core`/`elsa-studio` into the foundation repos, adapted to foundation architecture (CShells `IShellFeature`, `CShells.FastEndpoints`, EFCore persistence base, and the studio TypeScript module SDK) rather than a literal lift of the elsa-core `IFeature` modules and elsa-studio Blazor pages.

The bucket keeps the observability port coherent across two repos and two sub-domains (Structured Logs, OpenTelemetry), while reusing the existing console-log streaming surface (`ConsoleLogStreaming` package + `Elsa.Studio.ConsoleStream` bottom-panel tab) instead of re-porting ConsoleLogs.

## In Scope

- Backend `Elsa.Diagnostics.StructuredLogs` (capture via `ILoggerProvider`, in-memory live feed, CShells.FastEndpoints + SignalR hub) and its EFCore persistence (`*.Persistence.Core/.EFCore/.EFCore.Sqlite`).
- Backend `Elsa.Diagnostics.OpenTelemetry` (OTLP HTTP/protobuf ingestion, in-memory live feed, query API + SignalR hub) and its EFCore persistence.
- Studio bottom-panel tabs (TypeScript module SDK) for Structured Logs and OTEL, siblings of the existing Console tab via `api.panels.add`.
- Feature-registration and per-implementation unit tests, EXTENSION_POINTS catalog updates, and generated-map refreshes for the new domain.
- Host wiring in `Elsa.Server` (and studio host) mirroring the existing console-logs endpoint/hub mapping.

## Out Of Scope

- Re-porting ConsoleLogs (already solved by the `ConsoleLogStreaming` package and `Elsa.Studio.ConsoleStream`).
- A literal port of elsa-core's ADO.NET + FluentMigrator persistence stack (foundation uses EFCore).
- A literal port of elsa-studio Blazor/MudBlazor pages, Dashboard widgets, or `RemoteFeature` gating.
- Eagerly extracting a shared real-time live-feed package before a second consumer proves the seam.
- gRPC OTLP ingestion (source ships it disabled/stubbed); revisit only on demand.

## Active Objectives

1. Speckit slice: `Elsa.Diagnostics.StructuredLogs` capture + live feed + in-memory store + API/hub.
2. Speckit slice: Structured Logs EFCore persistence (Core/EFCore/Sqlite).
3. Speckit slice: `Elsa.Diagnostics.OpenTelemetry` OTLP ingestion + live feed + in-memory store + API/hub.
4. Speckit slice: OpenTelemetry EFCore persistence (enhancement beyond source, which was in-memory only).
5. Studio slice (elsa-foundation-studio): Structured Logs bottom-panel tab.
6. Studio slice (elsa-foundation-studio): OTEL bottom-panel tab.

## Linked Surfaces

- Session plan: `diagnostics-port-plan.md` (session artifact).
- Source: `elsa-core @ 8e20386a28f5754cc4e3b7f02e2fec5c8f676bdc`; `elsa-studio @ fb5e371febe37146a84679f7cc11b5461851f11a`.
- Existing console streaming: `src/Apps/Elsa.Server/Program.cs` (`ConsoleLogStreaming`), `elsa-foundation-studio:src/Elsa.Studio.ConsoleStream`.

## Current Roadmap Notes

- Backend-first in `elsa-foundation`; studio panels follow in a separate `elsa-foundation-studio` session that consumes the backend hubs/endpoints.
- Both sub-domains persist via the foundation EFCore base; OTEL persistence is a deliberate enhancement over the source.
- Studio UX consolidates observability into the bottom panel (tabs) rather than separate nav pages.

## Drift / Review Notes

- If shared real-time/live-feed code materializes across StructuredLogs and OTEL, evaluate a `Elsa.Diagnostics.RealTime.Core` seam (defer until the second consumer proves it; §2.17/§2.20).
- If observability work turns mostly into persistence-framework concerns, check against [Groundwork Persistence Readiness](groundwork-persistence-readiness.md).
- If a diagnostics rule becomes a general framework gate, move it to the constitution and leave a link here.

## Removal or Completion Conditions

Complete or pause when Structured Logs and OTEL ship in both foundation repos with persistence, live streaming, tests, catalog updates, and studio bottom-panel tabs, with any remaining work tracked in implementation-specific specs.
