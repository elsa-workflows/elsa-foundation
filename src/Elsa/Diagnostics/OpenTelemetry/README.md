# Elsa.Diagnostics.OpenTelemetry

Collects OpenTelemetry signals — traces, metrics, and logs — pushed by the host's OTLP exporter over **OTLP/HTTP protobuf**, normalizes them into a queryable diagnostics store, and exposes them to Elsa Studio over HTTP query endpoints and a Server-Sent Events (SSE) live stream. It is a **server** shell feature. Storage, ingestion, redaction, and the live feed are each isolated behind separate `.Core` contracts so a durable backend or external transport can replace one role without touching the rest.

Feature name (manifest / appsettings key): **`DiagnosticsOpenTelemetry`**.
The current first-party durable/reference-host composition feature is **`DiagnosticsGroundworkPersistence`**. `DiagnosticsOpenTelemetryPersistenceEFCoreSqlite` remains temporarily for comparison, oracle, and compatibility work; it has not been removed.

## What this feature provides

- **Decomposed roles** behind separate contracts (all registered with `TryAdd*` so a persistence/transport feature can replace just one):
  - **`OpenTelemetryIngestor`** → `IOpenTelemetryIngestor` — the write path: redacts the batch, awaits every additive ingestion contributor, writes it to the store, then publishes it to the live feed.
  - **`IOpenTelemetryIngestionContributor`** — additive post-redaction processing for independently composed features. Contributors receive both the redacted batch and an immutable `OpenTelemetryIngestionContext`; register one with `AddOpenTelemetryIngestionContributor<TContributor>()`.
  - **`IOtlpRequestAuthenticator`** — scoped request authentication and trusted source-context construction. A host can replace the default API-key/loopback implementation with per-source credential validation and authoritative workspace/application/environment claims.
  - **`OtlpHttpIngestionHandler`** — the single public OTLP/HTTP request handler shared by the explicit ASP.NET Core route mapper and the retained collector composition.
  - **`InMemoryOpenTelemetryStore`** → `IOpenTelemetryStore` — capacity-bounded ring buffers per signal (traces, spans, metric points, log records, resources). On every write it also marks the batch's resource as seen in the source registry (so resource and storage views stay populated). Registered with `TryAddSingleton` so a persistence feature can override it — **any override must also populate `IOpenTelemetrySourceRegistry`**, or the resources/storage views go empty.
  - **`GroundworkOpenTelemetryStore`** → `IOpenTelemetryStore` (via the aggregate `DiagnosticsGroundworkPersistence` feature) — the active first-party durable history adapter for resources, traces, spans, metric instruments, metric points, and logs. The aggregate installs the concrete Groundwork OpenTelemetry feature, replaces the default store, and contributes its diagnostic-record streams and document schema to the combined Groundwork deployment manifest.
  - **`InMemoryOpenTelemetryLiveFeed`** → `IOpenTelemetryLiveFeed` — an independent bounded channel per live subscriber (in-process fan-out) with the same backpressure/drop model as the Structured Logs feed; a slow consumer's overflow is dropped and surfaced in-band as a `dropped` signal.
  - **`OpenTelemetryRedactor`** → `IOpenTelemetryRedactor` — strips sensitive attribute values (by name) and masks sensitive text patterns (by regex) on ingestion.
  - **`OpenTelemetrySourceRegistry`** → `IOpenTelemetrySourceRegistry` — tracks the most-recently-seen telemetry resources. Populated by the store on each write (not by the ingestor); read by the resource and storage query endpoints.
  - **`DefaultOpenTelemetryProvider`** → `IOpenTelemetryProvider` — the read facade the query endpoints call.
  - **`CollectorConfigurationProvider`** → `ICollectorConfigurationProvider` — surfaces the endpoint paths/auth shape a collector needs to push to this host.
- **OTLP/HTTP protobuf collector**: `POST {base}/traces`, `POST {base}/metrics`, `POST {base}/logs` (base default `/elsa/otlp/v1`). The root `app.MapOpenTelemetryOtlpReceiver()` mapper is the sole collector route surface; shell composition calls the same mapper and must not add a second collector set. The dependency-free parser requires no protobuf package. `Content-Encoding: gzip` / `deflate` / `br` bodies are decompressed and the size cap applies to decompressed bytes.
- **Query endpoints** (owner Minimal APIs):
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

- **Query + SSE endpoints** use the catalog-owned `Diagnostics:OpenTelemetry.Read` action. They are **default-permissive (anonymous)** while host endpoint security is disabled; once the host enables security (`EndpointSecurityOptions`), it assigns this action to authorized principals.
- **OTLP collector endpoints** are outside the studio permission model and instead gated by `IOtlpRequestAuthenticator` before any body read or parse. The default implementation preserves the configured API-key/loopback policy: a valid global API key authenticates the collector but deliberately carries no tenant claims; accepted loopback requests are explicitly untrusted. A host can replace it with a scoped authenticator that validates per-source credentials and returns immutable, server-authoritative source claims. Telemetry resource attributes remain evidence and never become authority merely because a sender emitted them.

## Query parameters (SSE stream)

The SSE `stream` endpoint binds an `OpenTelemetryTraceFilter` from the query string via `OpenTelemetryTraceFilterBinder` (`traceId`, `workflowInstanceId`, `resourceId`, `serviceName`, `status`, `from`, `to`, `search`). Malformed values (e.g. an invalid `status` or timestamp) produce `InvalidTelemetryQueryException` → `400`. The `POST .../search` query endpoints bind their typed filter from the JSON body.

## SSE event contract (`stream`)

Frames use typed `event:` names with a `data:` JSON line:

- **`event: resource`** — a newly-seen/updated telemetry resource.
- **`event: trace`** — a completed trace matching the filter.
- **`event: metric`** — a metric point.
- **`event: log`** — an OTLP log record.
- **`event: dropped`** — `data:` carries the dropped-items summary (`signalType`, `count`, `reason`); emitted in-band when a slow consumer's bounded queue overflowed (backpressure), so the client learns of loss without a side channel.

Unlike the Structured Logs stream, OpenTelemetry stream items carry **no monotonic sequence/id**, so the OTEL SSE stream offers **no `Last-Event-ID` resume** — a reconnecting client simply resumes the live tail. Durable Groundwork query/history does not change that live-stream contract.

## Redaction

`OpenTelemetryRedactor` runs on every ingested batch before storage:

- **Sensitive attribute names** (`SensitiveNames`) — attribute values whose key matches are replaced with a redaction marker. Defaults cover `authorization`, `token`, `password`, `secret`, `api-key`/`apikey`, `cookie`, connection strings.
- **Sensitive text patterns** (`SensitiveTextPatterns`) — regexes applied to free-text fields (span/event names, span status descriptions, log bodies) to mask bearer tokens, `key=value` secrets, and storage account keys. Pattern evaluation is bounded by `SensitiveTextPatternTimeout`.

Both lists are surfaced through options so a host can extend or replace them.

## Post-redaction contributions and acknowledgement

`IOpenTelemetryIngestionContributor` is the additive handoff seam for features that need an ingested batch without replacing the collector, redactor, diagnostics store, or live feed. Every contributor receives the same redacted `OpenTelemetryBatch` instance plus the immutable `OpenTelemetryIngestionContext` established by the receiving server. Contributors must treat both as read-only and must use only authenticated context claims—not sender-controlled resource attributes—for authoritative tenant mapping. `OpenTelemetryIngestor` invokes contributors sequentially in registration order after redaction and before either diagnostics destination.

Contributor completion participates in OTLP ingestion acknowledgement. A contributor that promises durable handoff must return only after its own durable store has accepted the batch. If a contributor throws or observes cancellation, later contributors are not called, the diagnostics store and live feed are not updated, and the exception reaches the ingestion endpoint; therefore the endpoint does not report the batch as accepted. Once a durable contributor has accepted a batch, its independent background processing can be unavailable without requiring the original sender to resubmit it.

The contribution contract itself does not provide persistence, retries, de-duplication, or an outbox. Those semantics belong to each contributor. In particular, `IOpenTelemetryLiveFeed` remains a volatile UI tail; durable Groundwork history affects query/history endpoints, not the one-way live feed.

## Groundwork persistence

Without a persistence feature, `InMemoryOpenTelemetryStore` remains the default. The reference `Elsa.Workbench`
composition selects `DiagnosticsGroundworkPersistence` alongside the diagnostics domain features. That aggregate
atomically installs the two concrete Groundwork persistence features; the OpenTelemetry feature replaces
`IOpenTelemetryStore` with `GroundworkOpenTelemetryStore`, contributes its immutable signal streams, and joins
the shared document schema in Groundwork's deployment manifest.

The live SSE feed remains in-process (`IOpenTelemetryLiveFeed`) for every storage backend; persistence affects
query/history endpoints, not the one-way live tail. Stream frames still carry no monotonic event id, so the
OTEL SSE stream still has no `Last-Event-ID` resume.

`Elsa.Diagnostics.OpenTelemetry.Persistence.EFCore` and
`DiagnosticsOpenTelemetryPersistenceEFCoreSqlite` remain intact as temporary comparison, oracle, and
compatibility implementations. #646 must finish the retained performance measurement before #647 deletes the
EF diagnostics surface; this documentation does not claim that deletion or a performance verdict.

Operators who still need that temporary path enable
`DiagnosticsOpenTelemetryPersistenceEFCoreSqlite` alongside `DiagnosticsOpenTelemetry`. It replaces
`IOpenTelemetryStore` with `EfCoreOpenTelemetryStore`, disables generic EF command/query machinery, routes
the diagnostics DbContext's logging to `NullLoggerFactory` to prevent capture feedback, and uses a singleton
`IDbContextFactory<OpenTelemetryDbContext>`. The SQLite provider runs migrations before starting the bounded
drain; graceful shell termination flushes that drain before the DbContext factory is disposed, with async
store disposal as the fallback when shell terminators do not run. This retained path is not selected by the
reference `Elsa.Workbench` composition.

## Deferred (kept behind contracts/options)

- **gRPC ingestion** — `EnableGrpc` defaults to `false` and no gRPC route is mapped; the binding is host-specific. The option, `GrpcEndpointPath`, and `GrpcDisabledReason` are kept so a host can light it up later. (Source parity: elsa-core also ships gRPC disabled.)

## Deviations from the elsa-core source

This domain was ported from `Elsa.Diagnostics.OpenTelemetry` in elsa-core. Its notable transport choices are:

1. **SSE over SignalR.** The source streamed live telemetry via a SignalR hub (`OpenTelemetryHub` + `OpenTelemetrySubscriptionManager` + `IOpenTelemetryClient`). This port drops SignalR entirely and replaces it with an SSE `stream` endpoint mirroring the Structured Logs slice — for consistency, native `EventSource` reconnect, and no `@microsoft/signalr` dependency in studio.
2. **One receiver, one route surface.** `OtlpHttpIngestionHandler` owns authentication-before-body-read, decompression, size limits, parsing, and context-aware ingestion. `MapOpenTelemetryOtlpReceiver()` exposes the receiver to the host without duplicating routes.

## Replacing the defaults

All store/feed/ingestor/redactor/registry/provider contracts are overridable, while `IOpenTelemetryIngestionContributor` is additive — see [`EXTENSION_POINTS.md`](EXTENSION_POINTS.md). The shipped durable replacement is `GroundworkOpenTelemetryStore`; it leaves ingestion, redaction, transport, and the UI unchanged.

## Owned exception surface

- **`InvalidTelemetryQueryException`** — raised by `OpenTelemetryTraceFilterBinder` for malformed query input; surfaced as `400` by the SSE endpoint. Replaces raw parse failures.

## Cross-references

- Domain extension points: [`EXTENSION_POINTS.md`](EXTENSION_POINTS.md).
- Repo-wide index: [`../../../../EXTENSION_POINTS.md`](../../../../EXTENSION_POINTS.md).
- Sibling diagnostics domain (SSE precedent): [`../StructuredLogs/README.md`](../StructuredLogs/README.md).
