# Diagnostics Observability Readiness

Status: active.

Area: diagnostics/observability subsystem port — structured logs and OpenTelemetry across `elsa-foundation` (backend) and `elsa-foundation-studio` (frontend).

Steward(s): Joey plus active architects/agents.

## Purpose

Coordinate porting the diagnostics observability subsystem from `elsa-core`/`elsa-studio` into the foundation repos, adapted to foundation architecture (CShells `IShellFeature`, `CShells.FastEndpoints`, EF Core persistence under ADR 0073, and the studio TypeScript module SDK) rather than a literal lift of the elsa-core `IFeature` modules and elsa-studio Blazor pages.

The bucket keeps the observability port coherent across two repos and two sub-domains (Structured Logs, OpenTelemetry), while reusing the existing console-log streaming surface (`ConsoleLogStreaming` package + `Elsa.Studio.ConsoleStream` bottom-panel tab) instead of re-porting ConsoleLogs.

## In Scope

- Backend `Elsa.Diagnostics.StructuredLogs` (capture via `ILoggerProvider`, in-memory live feed, `CShells.FastEndpoints` with an SSE `text/event-stream` live endpoint) and its durable EF Core replacement under #1681 for SQLite, SQL Server, PostgreSQL, and MySQL. The removed `*.Persistence.EFCore/.EFCore.Sqlite` paths are historical only.
- Backend `Elsa.Diagnostics.OpenTelemetry` (OTLP HTTP/protobuf ingestion, in-memory live feed, query API + live streaming) and its durable EF Core replacement under #1681 for the same four providers. Its earlier EF Core paths are historical only.
- Studio bottom-panel tabs (TypeScript module SDK) for Structured Logs and OTEL, siblings of the existing Console tab via `api.panels.add`.
- Feature-registration and per-implementation unit tests, EXTENSION_POINTS catalog updates, and generated-map refreshes for the new domain.
- Host wiring in `Elsa.Server` (and studio host); Structured Logs needs no explicit hub mapping (its SSE/HTTP endpoints auto-map via `app.MapShells()`).

## Out Of Scope

- Re-porting ConsoleLogs (already solved by the `ConsoleLogStreaming` package and `Elsa.Studio.ConsoleStream`).
- A literal port of elsa-core's ADO.NET + FluentMigrator persistence stack (foundation uses EFCore).
- A literal port of elsa-studio Blazor/MudBlazor pages, Dashboard widgets, or `RemoteFeature` gating.
- Eagerly extracting a shared real-time live-feed package before a second consumer proves the seam.
- gRPC OTLP ingestion (source ships it disabled/stubbed); revisit only on demand.
- **Engine telemetry (the workflow engine's own `ActivitySource` self-instrumentation)** — this bucket owns telemetry *ingestion* (receiving OTLP pushed by other processes), not the engine *emitting* its own spans. The engine's self-instrumentation is the `IWorkflowEngineTracer` / `WorkflowsRuntimeTracing` surface delivered by W19 (MS-9) in the [Elsa 4 Review Remediation](elsa-4-review-remediation.md) bucket; the two are independent and neither depends on the other. See [`docs/reference/engine-telemetry.md`](../reference/engine-telemetry.md).

## Objective History and Active Work

Items 1–4 record what earlier slices delivered at the time. The Structured Logs and OpenTelemetry
EF Core implementations in items 2 and 4 are no longer present in the current tree; they are prior
oracle evidence, not current completed implementations. Their all-EF replacement and four-provider
proof are active under #1681.

1. ✅ **Done** — Speckit slice: `Elsa.Diagnostics.StructuredLogs` capture + live feed + in-memory store + SSE/HTTP API (spec 073). Shipped: capture `ILoggerProvider`, in-memory ring-buffer store, per-subscriber backpressure live feed, `recent`/`sources` HTTP + `stream` SSE endpoints (with `Last-Event-ID` resume), feature registration, 43 unit tests, EXTENSION_POINTS + README, host wiring. Host-wide capture validated against `Elsa.Server`.
2. 🕘 **Historical delivery record; implementation no longer present** — Speckit slice: Structured Logs EFCore persistence (EFCore/Sqlite). It decomposed the in-memory store into store (`IStructuredLogStore` history) + live feed (`IStructuredLogLiveFeed`/`IStructuredLogLivePublisher` fan-out) + sink (`IStructuredLogSink` sequencing); `EfCoreStructuredLogStore` provided a durable override with non-blocking channel-buffered `Append`, async batch-draining writer, `NullLoggerFactory` feedback-loop break, auto-increment `Id` durable cursor, and retention pruning. This is retained as oracle evidence for #1681, not as a claim about the current tree.
   - ✅ **Replay hardening #635** — [spec 091](../../specs/091-structured-logs-replay-cursors/spec.md) replaces numeric SSE resume with committed opaque cursors, makes the replay/live handoff cursor-based, defines uniform stale/wrong-binding errors, and adds the first-party Groundwork diagnostic-record adapter. `Sequence` remains display-only.
3. ✅ **Done** — Speckit slice: `Elsa.Diagnostics.OpenTelemetry` OTLP ingestion + live feed + in-memory store + query API + live streaming (spec 074). Shipped: two-package domain (`Elsa.Diagnostics.OpenTelemetry.Core` models/contracts/options + `Elsa.Diagnostics.OpenTelemetry` feature) registered as a CShells shell feature (`FastEndpointsFeatureBase`, name `DiagnosticsOpenTelemetry`); decomposed roles via `TryAdd*` (`IOpenTelemetryStore`/`IOpenTelemetryLiveFeed`/`IOpenTelemetryIngestor`/`IOpenTelemetryRedactor`/`IOpenTelemetrySourceRegistry`/`IOpenTelemetryProvider`/`ICollectorConfigurationProvider`); verbatim self-contained OTLP/HTTP protobuf parser (`OtlpHttpProtobufParser` + internal `ProtobufReader`, no protobuf NuGet) + `OtlpIngestionSecurity` (API-key/loopback); capacity-bounded in-memory ring-buffer store per signal; per-subscriber bounded-channel live feed; redaction (sensitive attr names + regex text patterns); 3 OTLP collector FastEndpoints + 6 query endpoints (resources/traces/trace/metrics/storage/collector-config) + SSE `stream`; 36 unit tests; EXTENSION_POINTS + README; spec 074 (spec + plan); host wiring (slnx, Program.cs, csproj, shells.json); maps refresh. `QueryLogsAsync` exists in the store but no logs HTTP endpoint shipped; that spec/API drift remains follow-up work. **Deviations applied:** (a) SSE over SignalR (RealTime hub/subscription-manager/client not ported; no `Last-Event-ID` resume since stream items carry no sequence); (b) OTLP collector as FastEndpoints (not `EndpointRouteBuilderExtensions`/`IWebShellFeature`), avoiding a new CShells.AspNetCore dep; (c) gRPC deferred (`EnableGrpc=false`, option retained). Live-validated against `Elsa.Server`: OTLP/protobuf POST → 200, ingest→query round-trip, and SSE `resource`/`trace` frames confirmed end-to-end.
4. 🕘 **Historical delivery record; implementation no longer present** — Speckit slice: OpenTelemetry EFCore persistence (enhancement beyond source, which was in-memory only). It used `Elsa.Diagnostics.OpenTelemetry.Persistence.EFCore` + `.Sqlite`, `OpenTelemetryDbContext`, durable resource/trace/span/metric/log tables, `EfCoreOpenTelemetryStore`, non-blocking channel-buffered writes, and retention pruning. This is retained as oracle evidence for #1681, not as a claim about the current tree.
5. ▶ **Next** — Studio slice (elsa-foundation-studio): Structured Logs bottom-panel tab. **Cross-repo — cannot build from this worktree.** Handoff: add a bottom-panel tab via `api.panels.add` (sibling of the existing Console tab in `Elsa.Studio.ConsoleStream`) that consumes the foundation SSE `stream` endpoint + `recent`/`sources` HTTP, using native `EventSource` (no `@microsoft/signalr`). Matches the user's bottom-panel mockup (Structured Logs + OTEL as tabs alongside Console).
6. ⛔ Studio slice (elsa-foundation-studio): OTEL bottom-panel tab. **Cross-repo.** Sibling bottom-panel tab consuming the OTEL query API + live SSE stream; trace/span/metric/log views.
7. Follow-up (evaluate): unify the Console tab onto the foundation-owned SSE transport — retire/replace the third-party `ConsoleLogStreaming` SignalR package — once SSE is proven on the Structured Logs slice. SSE is now proven (slices 1–2 shipped + live-validated), so this is unblocked for evaluation.

## Linked Surfaces

- Session plan: `diagnostics-port-plan.md` (session artifact).
- [Diagnostics storage workload](../reports/diagnostics-storage-workload.md) - historical Groundwork
  capability evidence and the domain inventory carried into EF Core Persistence issue #1681.
- Source: `elsa-core @ 8e20386a28f5754cc4e3b7f02e2fec5c8f676bdc`; `elsa-studio @ fb5e371febe37146a84679f7cc11b5461851f11a`.
- Existing console streaming: `src/Apps/Elsa.Server/Program.cs` (`ConsoleLogStreaming`), `elsa-foundation-studio:src/Elsa.Studio.ConsoleStream`.

## Current Roadmap Notes

- Backend-first in `elsa-foundation`; studio panels follow in a separate `elsa-foundation-studio` session that consumes the backend hubs/endpoints.
- Structured Logs has a Groundwork diagnostic-record adapter and OpenTelemetry has existing EF/Groundwork
  evidence. Their all-EF replacement and four-provider proof now belong to #1681 under
  [EF Core Persistence](ef-core-persistence.md).
- Studio UX consolidates observability into the bottom panel (tabs) rather than separate nav pages.
- **Live transport decision (Structured Logs, spec 073):** Server-Sent Events (SSE), not SignalR — the workload is one-way server→client browser streaming, SSE adds no dependency (no `@microsoft/signalr`, no shared-framework hub), and native `EventSource` gives auto-reconnect + `Last-Event-ID` resume. The Console tab keeps its third-party SignalR transport for now; unifying it onto SSE is objective 7. OTEL's transport is decided per-slice (SSE preferred for consistency).

## Drift / Review Notes

- If shared real-time/live-feed code materializes across StructuredLogs and OTEL, evaluate a `Elsa.Diagnostics.RealTime.Core` seam (defer until the second consumer proves it; §2.17/§2.20).
- If observability work turns mostly into persistence-framework concerns, check against [EF Core Persistence](ef-core-persistence.md). [Groundwork Persistence Readiness](groundwork-persistence-readiness.md) is historical only.
- If a diagnostics rule becomes a general framework gate, move it to the constitution and leave a link here.

## Removal or Completion Conditions

Complete or pause when Structured Logs and OTEL ship in both foundation repos with persistence, live streaming, tests, catalog updates, and studio bottom-panel tabs, with any remaining work tracked in implementation-specific specs.
