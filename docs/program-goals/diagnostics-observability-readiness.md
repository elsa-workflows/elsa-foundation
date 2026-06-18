# Diagnostics Observability Readiness

Status: active.

Area: diagnostics/observability subsystem port — structured logs and OpenTelemetry across `elsa-foundation` (backend) and `elsa-foundation-studio` (frontend).

Steward(s): Joey plus active architects/agents.

## Purpose

Coordinate porting the diagnostics observability subsystem from `elsa-core`/`elsa-studio` into the foundation repos, adapted to foundation architecture (CShells `IShellFeature`, `CShells.FastEndpoints`, EFCore persistence base, and the studio TypeScript module SDK) rather than a literal lift of the elsa-core `IFeature` modules and elsa-studio Blazor pages.

The bucket keeps the observability port coherent across two repos and two sub-domains (Structured Logs, OpenTelemetry), while reusing the existing console-log streaming surface (`ConsoleLogStreaming` package + `Elsa.Studio.ConsoleStream` bottom-panel tab) instead of re-porting ConsoleLogs.

## In Scope

- Backend `Elsa.Diagnostics.StructuredLogs` (capture via `ILoggerProvider`, in-memory live feed, `CShells.FastEndpoints` with an SSE `text/event-stream` live endpoint) and its EFCore persistence (`*.Persistence.Core/.EFCore/.EFCore.Sqlite`).
- Backend `Elsa.Diagnostics.OpenTelemetry` (OTLP HTTP/protobuf ingestion, in-memory live feed, query API + live streaming) and its EFCore persistence.
- Studio bottom-panel tabs (TypeScript module SDK) for Structured Logs and OTEL, siblings of the existing Console tab via `api.panels.add`.
- Feature-registration and per-implementation unit tests, EXTENSION_POINTS catalog updates, and generated-map refreshes for the new domain.
- Host wiring in `Elsa.Server` (and studio host); Structured Logs needs no explicit hub mapping (its SSE/HTTP endpoints auto-map via `app.MapShells()`).

## Out Of Scope

- Re-porting ConsoleLogs (already solved by the `ConsoleLogStreaming` package and `Elsa.Studio.ConsoleStream`).
- A literal port of elsa-core's ADO.NET + FluentMigrator persistence stack (foundation uses EFCore).
- A literal port of elsa-studio Blazor/MudBlazor pages, Dashboard widgets, or `RemoteFeature` gating.
- Eagerly extracting a shared real-time live-feed package before a second consumer proves the seam.
- gRPC OTLP ingestion (source ships it disabled/stubbed); revisit only on demand.

## Active Objectives

1. ✅ **Done** — Speckit slice: `Elsa.Diagnostics.StructuredLogs` capture + live feed + in-memory store + SSE/HTTP API (spec 073). Shipped: capture `ILoggerProvider`, in-memory ring-buffer store, per-subscriber backpressure live feed, `recent`/`sources` HTTP + `stream` SSE endpoints (with `Last-Event-ID` resume), feature registration, 43 unit tests, EXTENSION_POINTS + README, host wiring. Host-wide capture validated against `Elsa.Server`.
2. ✅ **Done** — Speckit slice: Structured Logs EFCore persistence (EFCore/Sqlite). Shipped: decomposed the in-memory store into store (`IStructuredLogStore` history) + live feed (`IStructuredLogLiveFeed`/`IStructuredLogLivePublisher` fan-out) + sink (`IStructuredLogSink` sequencing); `EfCoreStructuredLogStore` durable override with non-blocking channel-buffered `Append`, async batch-draining writer, `NullLoggerFactory` feedback-loop break, auto-increment `Id` durable cursor, and retention pruning; `DiagnosticsStructuredLogsPersistenceEFCoreSqlite` shell feature + initial migration; 56 unit tests (46 + 10), EXTENSION_POINTS + README updates, host wiring, maps refresh. Live-validated against `Elsa.Server` (logs persisted to SQLite, no feedback loop).
3. Speckit slice: `Elsa.Diagnostics.OpenTelemetry` OTLP ingestion + live feed + in-memory store + API/hub.
4. Speckit slice: OpenTelemetry EFCore persistence (enhancement beyond source, which was in-memory only).
5. Studio slice (elsa-foundation-studio): Structured Logs bottom-panel tab.
6. Studio slice (elsa-foundation-studio): OTEL bottom-panel tab.
7. Follow-up (evaluate): unify the Console tab onto the foundation-owned SSE transport — retire/replace the third-party `ConsoleLogStreaming` SignalR package — once SSE is proven on the Structured Logs slice.

## Linked Surfaces

- Session plan: `diagnostics-port-plan.md` (session artifact).
- Source: `elsa-core @ 8e20386a28f5754cc4e3b7f02e2fec5c8f676bdc`; `elsa-studio @ fb5e371febe37146a84679f7cc11b5461851f11a`.
- Existing console streaming: `src/Apps/Elsa.Server/Program.cs` (`ConsoleLogStreaming`), `elsa-foundation-studio:src/Elsa.Studio.ConsoleStream`.

## Current Roadmap Notes

- Backend-first in `elsa-foundation`; studio panels follow in a separate `elsa-foundation-studio` session that consumes the backend hubs/endpoints.
- Both sub-domains persist via the foundation EFCore base; OTEL persistence is a deliberate enhancement over the source.
- Studio UX consolidates observability into the bottom panel (tabs) rather than separate nav pages.
- **Live transport decision (Structured Logs, spec 073):** Server-Sent Events (SSE), not SignalR — the workload is one-way server→client browser streaming, SSE adds no dependency (no `@microsoft/signalr`, no shared-framework hub), and native `EventSource` gives auto-reconnect + `Last-Event-ID` resume. The Console tab keeps its third-party SignalR transport for now; unifying it onto SSE is objective 7. OTEL's transport is decided per-slice (SSE preferred for consistency).

## Drift / Review Notes

- If shared real-time/live-feed code materializes across StructuredLogs and OTEL, evaluate a `Elsa.Diagnostics.RealTime.Core` seam (defer until the second consumer proves it; §2.17/§2.20).
- If observability work turns mostly into persistence-framework concerns, check against [Groundwork Persistence Readiness](groundwork-persistence-readiness.md).
- If a diagnostics rule becomes a general framework gate, move it to the constitution and leave a link here.

## Removal or Completion Conditions

Complete or pause when Structured Logs and OTEL ship in both foundation repos with persistence, live streaming, tests, catalog updates, and studio bottom-panel tabs, with any remaining work tracked in implementation-specific specs.
