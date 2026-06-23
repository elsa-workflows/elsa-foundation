# Elsa.Diagnostics.OpenTelemetry

Collects OpenTelemetry signals — traces, metrics, and logs — pushed by the host's OTLP exporter over **OTLP/HTTP protobuf**, normalizes them into a queryable diagnostics store, and exposes them to Elsa Studio over HTTP query endpoints and a Server-Sent Events (SSE) live stream. It is a **server** shell feature. Storage, ingestion, redaction, and the live feed are each isolated behind separate `.Core` contracts so a durable backend or external transport can replace one role without touching the rest.

Feature name (manifest / appsettings key): **`DiagnosticsOpenTelemetry`**.
SQLite persistence feature name: **`DiagnosticsOpenTelemetryPersistenceEFCoreSqlite`**.

## What this feature provides

- **Decomposed roles** behind separate contracts (all registered with `TryAdd*` so a persistence/transport feature can replace just one):
  - **`OpenTelemetryIngestor`** → `IOpenTelemetryIngestor` — the write path: redacts the batch, writes it to the store, then publishes it to the live feed.
  - **`InMemoryOpenTelemetryStore`** → `IOpenTelemetryStore` — capacity-bounded ring buffers per signal (traces, spans, metric points, log records, resources). On every write it also marks the batch's resource as seen in the source registry (so resource and storage views stay populated). Registered with `TryAddSingleton` so a persistence feature can override it — **any override must also populate `IOpenTelemetrySourceRegistry`**, or the resources/storage views go empty.
  - **`EfCoreOpenTelemetryStore`** → `IOpenTelemetryStore` (via `DiagnosticsOpenTelemetryPersistenceEFCoreSqlite`) — durable EF Core-backed history for resources, traces, spans, metric instruments, metric points, and logs. It uses the same non-blocking write pattern as Structured Logs persistence: ingestion enqueues batches onto a bounded channel, a startup task starts the async drain loop after migrations, and retention pruning keeps high-volume tables bounded by the configured capacities. It also marks resources seen synchronously before enqueueing.
  - **`InMemoryOpenTelemetryLiveFeed`** → `IOpenTelemetryLiveFeed` — an independent bounded channel per live subscriber (in-process fan-out) with the same backpressure/drop model as the Structured Logs feed; a slow consumer's overflow is dropped and surfaced in-band as a `dropped` signal.
  - **`OpenTelemetryRedactor`** → `IOpenTelemetryRedactor` — strips sensitive attribute values (by name) and masks sensitive text patterns (by regex) on ingestion.
  - **`OpenTelemetrySourceRegistry`** → `IOpenTelemetrySourceRegistry` — tracks the most-recently-seen telemetry resources. Populated by the store on each write (not by the ingestor); read by the resource and storage query endpoints.
  - **`DefaultOpenTelemetryProvider`** → `IOpenTelemetryProvider` — the read facade the query endpoints call.
  - **`CollectorConfigurationProvider`** → `ICollectorConfigurationProvider` — surfaces the endpoint paths/auth shape a collector needs to push to this host.
- **OTLP/HTTP protobuf collector** (FastEndpoints, auto-mapped via `app.MapShells()`): `POST {base}/traces`, `POST {base}/metrics`, `POST {base}/logs` (base default `/elsa/otlp/v1`). The protobuf wire format is parsed by a self-contained, dependency-free hand-rolled parser (`OtlpHttpProtobufParser` + an internal `ProtobufReader`) — no protobuf NuGet package is required. `Content-Encoding: gzip` / `deflate` / `br` bodies are decompressed (size cap applied to the decompressed bytes). These endpoints are authenticated by `OtlpIngestionSecurity` (API-key header or loopback), not by the studio permission model.
- **Query endpoints** (FastEndpoints):
  - `POST /diagnostics/opentelemetry/resources/search`
  - `POST /diagnostics/opentelemetry/traces/search`
  - `GET  /diagnostics/opentelemetry/traces/{traceId}`
  - `POST /diagnostics/opentelemetry/metrics/search`
  - `POST /diagnostics/opentelemetry/logs/search`
  - `GET  /diagnostics/opentelemetry/storage` — store diagnostics (counts, dropped totals).
  - `GET  /diagnostics/opentelemetry/collector-configuration` — collector push configuration.
- **SSE live stream**: `GET /_elsa/studio/diagnostics/opentelemetry/stream` — live telemetry as Server-Sent Events, fed by `IOpenTelemetryLiveFeed.SubscribeAsync`.
- **Serialization helpers** (`internal`, branch-tested): `OpenTelemetryStreamItemSerializer` (wire JSON shape), `OpenTelemetrySseFormatter` (SSE framing), `OpenTelemetryTraceFilterBinder` (query-string → `OpenTelemetryTraceFilter`, rejecting malformed input with `InvalidTelemetryQueryException`).

## Options (`OpenTelemetryDiagnosticsOptions`)

Exposed manifest settings: **Trace / Span / Metric point / Log record / Resource capacity** (ring-buffer bounds), **Subscriber channel capacity** (live backpressure), **Max query size**, **Max HTTP request body size**, **API key** + **API key header**, **Allow unauthenticated loopback**. Additional tunables on the options type: `HttpEndpointPath` (default `/elsa/otlp/v1`), `StreamPath`, the redaction lists (`SensitiveNames`, `SensitiveTextPatterns`, `SensitiveTextPatternTimeout`), and the deferred gRPC knobs (`EnableGrpc`, `GrpcEndpointPath`, `GrpcDisabledReason`).

## Authorization

Two distinct auth surfaces:

- **Query + SSE endpoints** call `ConfigurePermissions("Diagnostics:OpenTelemetry")` (the FastEndpoints permission model used across the foundation). They are **default-permissive (anonymous)** while host endpoint security is disabled; once the host enables security (`EndpointSecurityOptions`), it assigns this permission to authorized principals.
- **OTLP collector endpoints** are `AllowAnonymous()` to the FastEndpoints permission model and instead gated by `OtlpIngestionSecurity`: when `ApiKey` is set, requests must carry it in `ApiKeyHeaderName` (constant-time comparison); when unset, ingestion is allowed only from loopback if `AllowUnauthenticatedLoopback` is `true`. Ingestion auth is the API key, not a studio principal.

## Query parameters (SSE stream)

The SSE `stream` endpoint binds an `OpenTelemetryTraceFilter` from the query string via `OpenTelemetryTraceFilterBinder` (`traceId`, `workflowInstanceId`, `resourceId`, `serviceName`, `status`, `from`, `to`, `search`). Malformed values (e.g. an invalid `status` or timestamp) produce `InvalidTelemetryQueryException` → `400`. The `POST .../search` query endpoints bind their typed filter from the JSON body.

## SSE event contract (`stream`)

Frames use typed `event:` names with a `data:` JSON line:

- **`event: resource`** — a newly-seen/updated telemetry resource.
- **`event: trace`** — a completed trace matching the filter.
- **`event: metric`** — a metric point.
- **`event: log`** — an OTLP log record.
- **`event: dropped`** — `data:` carries the dropped-items summary (`signalType`, `count`, `reason`); emitted in-band when a slow consumer's bounded queue overflowed (backpressure), so the client learns of loss without a side channel.

Unlike the Structured Logs stream, OpenTelemetry stream items carry **no monotonic sequence/id**, so the OTEL stream offers **no `Last-Event-ID` resume** — a reconnecting client simply resumes the live tail. (Durable, resumable history is a persistence follow-up; see _Deferred_.)

## Redaction

`OpenTelemetryRedactor` runs on every ingested batch before storage:

- **Sensitive attribute names** (`SensitiveNames`) — attribute values whose key matches are replaced with a redaction marker. Defaults cover `authorization`, `token`, `password`, `secret`, `api-key`/`apikey`, `cookie`, connection strings.
- **Sensitive text patterns** (`SensitiveTextPatterns`) — regexes applied to free-text fields (span/event names, span status descriptions, log bodies) to mask bearer tokens, `key=value` secrets, and storage account keys. Pattern evaluation is bounded by `SensitiveTextPatternTimeout`.

Both lists are surfaced through options so a host can extend or replace them.

## EFCore SQLite persistence

Enable `DiagnosticsOpenTelemetryPersistenceEFCoreSqlite` alongside `DiagnosticsOpenTelemetry` to replace the default in-memory store with durable SQLite-backed history. The persistence feature:

- registers `EfCoreOpenTelemetryStore` as the active `IOpenTelemetryStore` replacement;
- disables generic EF command/query machinery because this is a diagnostics store, not a read-model domain;
- routes this DbContext's EF logging to `NullLoggerFactory` to avoid diagnostics feedback loops;
- uses `IDbContextFactory<OpenTelemetryDbContext>` as a singleton to avoid captive dependencies from the singleton store;
- runs migrations from the SQLite provider package and starts the drain loop through `StartOpenTelemetryDrainingStartupTask`.

The live SSE feed remains in-process (`IOpenTelemetryLiveFeed`) for every storage backend; persistence affects query/history endpoints, not the one-way live tail. Stream frames still carry no monotonic event id, so the OTEL SSE stream still has no `Last-Event-ID` resume.

## Deferred (kept behind contracts/options)

- **gRPC ingestion** — `EnableGrpc` defaults to `false` and no gRPC route is mapped; the binding is host-specific. The option, `GrpcEndpointPath`, and `GrpcDisabledReason` are kept so a host can light it up later. (Source parity: elsa-core also ships gRPC disabled.)

## Deviations from the elsa-core source

This domain was ported from `Elsa.Diagnostics.OpenTelemetry` in elsa-core. Two deliberate deviations:

1. **SSE over SignalR.** The source streamed live telemetry via a SignalR hub (`OpenTelemetryHub` + `OpenTelemetrySubscriptionManager` + `IOpenTelemetryClient`). This port drops SignalR entirely and replaces it with an SSE `stream` endpoint mirroring the Structured Logs slice — for consistency, native `EventSource` reconnect, and no `@microsoft/signalr` dependency in studio.
2. **OTLP collector as FastEndpoints.** The source mapped the collector via `EndpointRouteBuilderExtensions` / `IWebShellFeature`. This port implements the collector as FastEndpoints endpoints (consistent with the rest of the foundation, auto-mapped via `app.MapShells()`), avoiding a new CShells.AspNetCore host-plumbing dependency. The protobuf parser and `OtlpIngestionSecurity` are ported **verbatim** and reused by the FastEndpoints ingestion endpoints.

## Replacing the defaults

All store/feed/ingestor/redactor/registry/provider contracts are overridable — see [`EXTENSION_POINTS.md`](EXTENSION_POINTS.md). The shipped extension is replacing `IOpenTelemetryStore` with `EfCoreOpenTelemetryStore` while leaving ingestion, redaction, transport, and the UI unchanged.

## Owned exception surface

- **`InvalidTelemetryQueryException`** — raised by `OpenTelemetryTraceFilterBinder` for malformed query input; surfaced as `400` by the SSE endpoint. Replaces raw parse failures.

## Cross-references

- Domain extension points: [`EXTENSION_POINTS.md`](EXTENSION_POINTS.md).
- Repo-wide index: [`../../../../EXTENSION_POINTS.md`](../../../../EXTENSION_POINTS.md).
- Sibling diagnostics domain (SSE precedent): [`../StructuredLogs/README.md`](../StructuredLogs/README.md).
