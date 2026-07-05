# Extension points — Diagnostics: OpenTelemetry domain

The per-domain catalog (framework §2.22.1). Anchored at `Elsa.Diagnostics.OpenTelemetry` — the server feature that collects OpenTelemetry signals (traces, metrics, logs) over OTLP/HTTP protobuf into capacity-bounded in-memory stores and exposes them over HTTP query endpoints + Server-Sent Events. All seams are **overridable `.Core` contracts**; there are no contributor interfaces or published events in v1.

The collect/serve pipeline is decomposed into single-responsibility roles so a durable backend or external transport can replace just one of them:

- **`IOpenTelemetryIngestor`** is the write path: redact → store → publish to the live feed (keeping history and the tail consistent).
- **`IOpenTelemetryStore`** is pure history (`WriteAsync` + the `Query*`/`Get*` reads). On write it also populates the source registry (`MarkSeen`). Swap this to make telemetry durable.
- **`IOpenTelemetryLiveFeed`** is the in-process fan-out to SSE subscribers. It stays in-process for every storage backend.
- **`IOpenTelemetryRedactor`**, **`IOpenTelemetrySourceRegistry`**, **`IOpenTelemetryProvider`**, **`ICollectorConfigurationProvider`** round out the redaction, resource-tracking, read-facade, and collector-config roles.

---

## Overridable contracts

All contracts live in `Elsa.Diagnostics.OpenTelemetry.Core`. The feature registers each default with `TryAdd*` (see `AddOpenTelemetryDiagnosticsServices`), so a persistence or transport feature can replace a single role regardless of feature order. The in-memory store and live feed are each registered as a concrete singleton plus a `TryAddSingleton` factory binding the interface to the same instance.

### `IOpenTelemetryStore` *(Core — `Elsa.Diagnostics.OpenTelemetry.Core`)*
- **Signature:** `ValueTask WriteAsync(OpenTelemetryBatch batch, …)`, `ValueTask<OpenTelemetryResourceResult> QueryResourcesAsync(…)`, `ValueTask<OpenTelemetryTraceResult> QueryTracesAsync(…)`, `ValueTask<OpenTelemetryTraceDetail?> GetTraceAsync(string traceId, …)`, `ValueTask<OpenTelemetryMetricResult> QueryMetricsAsync(…)`, `ValueTask<OpenTelemetryLogResult> QueryLogsAsync(…)`, `ValueTask<OpenTelemetryStorageDiagnostics> GetDiagnosticsAsync(…)`.
- **Default impl:** `InMemoryOpenTelemetryStore` — capacity-bounded ring buffers per signal (trace/span/metric-point/log-record/resource), bounds from `OpenTelemetryDiagnosticsOptions.*Capacity`. Stores normalized batches; oldest entries roll off and are counted as dropped. **It also populates the source registry** (`IOpenTelemetrySourceRegistry.MarkSeen`) for each resource it writes, and serves resource queries/storage diagnostics from that registry.
- **Override:** register your own `IOpenTelemetryStore` to persist telemetry and serve queries from durable storage. Pure *replace-one-keep-rest* override. Because the default uses `TryAddSingleton`, a persistence feature's plain `AddSingleton<IOpenTelemetryStore>` wins regardless of feature order. The shipped `DiagnosticsOpenTelemetryPersistenceEFCoreSqlite` feature registers `EfCoreOpenTelemetryStore`, which persists resources/traces/spans/metric instruments/metric points/logs through EF Core and still calls `IOpenTelemetrySourceRegistry.MarkSeen` on write. Any other durable override MUST also call `MarkSeen` (or otherwise surface resources) or the resources/storage views will be empty.

### `IOpenTelemetryLiveFeed` *(Core — `Elsa.Diagnostics.OpenTelemetry.Core`)*
- **Signature:** `ValueTask PublishAsync(OpenTelemetryBatch batch, …)`, `IAsyncEnumerable<OpenTelemetryStreamItem> SubscribeAsync(OpenTelemetryTraceFilter filter, CancellationToken cancellationToken)`.
- **Default impl:** `InMemoryOpenTelemetryLiveFeed` — each subscriber gets an independent bounded channel (capacity `SubscriberChannelCapacity`); a slow consumer never blocks the ingestion path, its overflowed items are dropped and a `DroppedItems` summary is delivered in-band. Per-signal filtering (`MatchesResource`/`MatchesTrace`/`MatchesLog`/`MatchesMetricPoint`) is applied per subscriber.
- **Override:** replace to source the live feed from an external broker (Redis Streams, a message bus) so multiple hosts fan into one stream.

### `IOpenTelemetryIngestor` *(Core — `Elsa.Diagnostics.OpenTelemetry.Core`)*
- **Signature:** `ValueTask IngestAsync(OpenTelemetryBatch batch, …)`.
- **Default impl:** `OpenTelemetryIngestor` — redacts the batch (`IOpenTelemetryRedactor`), writes it to `IOpenTelemetryStore` (which in turn marks resources seen in the source registry), then publishes to `IOpenTelemetryLiveFeed`. This is the seam the OTLP collector endpoints write to.
- **Override:** replace to tee ingested batches elsewhere (e.g. forward to an external collector) while keeping the in-memory store for the UI.

### `IOpenTelemetryRedactor` *(Core — `Elsa.Diagnostics.OpenTelemetry.Core`)*
- **Signature:** `OpenTelemetryBatch Redact(OpenTelemetryBatch batch)`.
- **Default impl:** `OpenTelemetryRedactor` — replaces attribute values whose key matches `SensitiveNames` and masks free-text fields matching `SensitiveTextPatterns` (bounded by `SensitiveTextPatternTimeout`).
- **Override:** replace to plug in a different redaction policy (e.g. a centralized DLP service) without touching ingestion or storage.

### `IOpenTelemetrySourceRegistry` *(Core — `Elsa.Diagnostics.OpenTelemetry.Core`)*
- **Signature:** `void MarkSeen(TelemetryResource resource)`, `IReadOnlyCollection<TelemetryResource> List()`.
- **Default impl:** `OpenTelemetrySourceRegistry` — tracks the most-recently-seen telemetry resources (services/instances). Populated by `IOpenTelemetryStore` on write (`MarkSeen`), and read back by the store's resource queries and storage diagnostics.
- **Override:** replace to enumerate sources from an external inventory in a multi-host deployment.

### `IOpenTelemetryProvider` *(Core — `Elsa.Diagnostics.OpenTelemetry.Core`)*
- **Signature:** the read facade — `GetResourcesAsync`, `GetTracesAsync`, `GetTraceAsync`, `GetMetricsAsync`, `GetLogsAsync`, `GetStorageDiagnosticsAsync`, `GetCollectorConfigurationAsync`.
- **Default impl:** `DefaultOpenTelemetryProvider` — delegates to `IOpenTelemetryStore` (+ `ICollectorConfigurationProvider`). This is what the query endpoints call.
- **Override:** replace to compose reads across multiple stores or add a caching/aggregation layer without changing the endpoints.

### `ICollectorConfigurationProvider` *(Core — `Elsa.Diagnostics.OpenTelemetry.Core`)*
- **Signature:** `ValueTask<CollectorConfiguration> GetAsync(…)`.
- **Default impl:** `CollectorConfigurationProvider` — surfaces the OTLP endpoint paths and auth shape a collector needs to push to this host (consumed by `GET /diagnostics/opentelemetry/collector-configuration`).
- **Override:** replace to advertise a different push topology (e.g. a fronting gateway address).

---

## Ingestion & transport (not extension points)

- The **OTLP/HTTP protobuf parser** (`OtlpHttpProtobufParser` + the internal `ProtobufReader`) is the verbatim, dependency-free wire decoder; it is internal machinery behind the collector endpoints, not a seam. It is exercised directly by unit tests via `InternalsVisibleTo`.
- **`OtlpIngestionSecurity`** (API-key header / loopback allowance) is the ingestion auth gate used by the collector endpoints. It is orthogonal to the FastEndpoints permission model that guards the query + SSE endpoints.
- The **collector endpoints** (`Endpoints/Ingestion/{Traces,Metrics,Logs}IngestionEndpoint`) and the **SSE `stream` endpoint** are transport surfaces; the wire JSON/SSE shape is owned by `OpenTelemetryStreamItemSerializer` + `OpenTelemetrySseFormatter` (see [`README.md`](README.md)).

## Deferred

- **gRPC ingestion** — kept behind `OpenTelemetryDiagnosticsOptions.EnableGrpc` (default `false`); no gRPC route is mapped. The binding is host-specific.
- **Persistence providers beyond SQLite** — `IOpenTelemetryStore` is the override seam for durable backends. SQLite EFCore persistence is shipped; other EF providers or external stores can use the same replacement-contract shape.

---

## Notes

- Live stream items (`OpenTelemetryStreamItem`) carry **no monotonic sequence/id**, so the SSE stream offers **no `Last-Event-ID` resume** (unlike Structured Logs, which resumes from `store.GetAfterAsync(sequence)`). SSE frames use typed `event:` names (`resource`/`trace`/`metric`/`log`/`dropped`) with no `id:`.
- This domain was ported from elsa-core with two deliberate deviations — **SSE over SignalR** and the **OTLP collector as FastEndpoints** (not `EndpointRouteBuilderExtensions`/`IWebShellFeature`). See [`README.md`](README.md#deviations-from-the-elsa-core-source).

---

## Cross-references

- Repo-wide index: [`../../../../EXTENSION_POINTS.md`](../../../../EXTENSION_POINTS.md).
- Feature documentation: [`README.md`](README.md).
- Sibling diagnostics domain: [`../StructuredLogs/EXTENSION_POINTS.md`](../StructuredLogs/EXTENSION_POINTS.md).
- Constitutional basis: §2.6.2 + §2.22.1.
