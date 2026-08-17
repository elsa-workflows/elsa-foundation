# Extension points — Diagnostics: OpenTelemetry domain

The per-domain catalog (framework §2.22.1). Anchored at `Elsa.Diagnostics.OpenTelemetry` — the server feature that collects OpenTelemetry signals (traces, metrics, logs) over OTLP/HTTP protobuf into capacity-bounded in-memory stores by default and exposes them over HTTP query endpoints + Server-Sent Events. Data-pipeline seams are **overridable `.Core` contracts**; the ASP.NET Core package adds a scoped receiver-authentication seam, and post-redaction ingestion uses an additive contributor contract.

The collect/serve pipeline is decomposed into single-responsibility roles so a durable backend or external transport can replace just one of them:

- **`IOpenTelemetryIngestor`** is the write path: redact → await additive contributors → store → publish to the live feed (keeping history and the tail consistent).
- **`IOpenTelemetryIngestionContributor`** is the additive post-redaction handoff for independently composed features. Contributor completion gates ingestion acknowledgement.
- **`IOtlpRequestAuthenticator`** is the scoped ASP.NET Core ingress-authentication seam. It establishes immutable, server-authoritative source identity and claims before the receiver reads the request body.
- **`IOpenTelemetryStore`** is pure history (`WriteAsync` + the `Query*`/`Get*` reads). On write it also populates the source registry (`MarkSeen`). Swap this to make telemetry durable.
- **`IOpenTelemetryLiveFeed`** is the in-process fan-out to SSE subscribers. It stays in-process for every storage backend.
- **`IOpenTelemetryRedactor`**, **`IOpenTelemetrySourceRegistry`**, **`IOpenTelemetryProvider`**, **`ICollectorConfigurationProvider`** round out the redaction, resource-tracking, read-facade, and collector-config roles.

---

## Overridable contracts

Data-pipeline contracts live in `Elsa.Diagnostics.OpenTelemetry.Core`; the HTTP-specific `IOtlpRequestAuthenticator` lives in `Elsa.Diagnostics.OpenTelemetry`. The feature registers each default with `TryAdd*` (see `AddOpenTelemetryDiagnosticsServices`), so a persistence or transport feature can replace a single role regardless of feature order. The in-memory store and live feed are each registered as a concrete singleton plus a `TryAddSingleton` factory binding the interface to the same instance.

### `IOpenTelemetryIngestionContributor` *(Core — `Elsa.Diagnostics.OpenTelemetry.Core`)*
- **Signature:** `ValueTask ContributeAsync(OpenTelemetryBatch redactedBatch, OpenTelemetryIngestionContext ingestionContext, CancellationToken cancellationToken)`.
- **Invocation:** every registered contributor is awaited sequentially in registration order after the batch is redacted and before it reaches `IOpenTelemetryStore` or `IOpenTelemetryLiveFeed`. Each receives the same redacted batch and immutable server ingestion context and must treat both as read-only. Only authenticated context claims are authoritative; resource attributes remain sender-controlled evidence.
- **Register:** `services.AddOpenTelemetryIngestionContributor<MyContributor>()`. Registration is additive and duplicate-safe for the same implementation type.
- **Acknowledgement semantics:** exceptions and cancellation propagate, stop later contributors, and prevent store/live publication. A contributor offering durable handoff must complete only after its own durable acceptance; its background processing can then proceed independently. The contract does not itself add retries, persistence, de-duplication, or outbox behavior.
- **Lifetime:** the default ingestor is a singleton, so the helper registers contributors as singletons. Contributors needing database access should depend on singleton-safe factories rather than scoped contexts.

### `IOpenTelemetryStore` *(Core — `Elsa.Diagnostics.OpenTelemetry.Core`)*
- **Signature:** `ValueTask WriteAsync(OpenTelemetryBatch batch, …)`, `ValueTask<OpenTelemetryResourceResult> QueryResourcesAsync(…)`, `ValueTask<OpenTelemetryTraceResult> QueryTracesAsync(…)`, `ValueTask<OpenTelemetryTraceDetail?> GetTraceAsync(string traceId, …)`, `ValueTask<OpenTelemetryMetricResult> QueryMetricsAsync(…)`, `ValueTask<OpenTelemetryLogResult> QueryLogsAsync(…)`, `ValueTask<OpenTelemetryStorageDiagnostics> GetDiagnosticsAsync(…)`.
- **Default impl:** `InMemoryOpenTelemetryStore` — capacity-bounded ring buffers per signal (trace/span/metric-point/log-record/resource), bounds from `OpenTelemetryDiagnosticsOptions.*Capacity`. Stores normalized batches; oldest entries roll off and are counted as dropped. **It also populates the source registry** (`IOpenTelemetrySourceRegistry.MarkSeen`) for each resource it writes, and serves resource queries/storage diagnostics from that registry.
- **Override:** register your own `IOpenTelemetryStore` to persist telemetry and serve queries from durable storage. Pure *replace-one-keep-rest* override. Because the default uses `TryAddSingleton`, a persistence feature's plain `AddSingleton<IOpenTelemetryStore>` wins regardless of feature order. The first-party `DiagnosticsGroundworkPersistence` aggregate installs `GroundworkOpenTelemetryStore`, which persists resources/traces/spans/metric instruments/metric points/logs through Groundwork and still calls `IOpenTelemetrySourceRegistry.MarkSeen` on write. It is the reference-host durable composition and contributes its diagnostic-record streams and document schema to Groundwork deployment. Any other durable override MUST also call `MarkSeen` (or otherwise surface resources) or the resources/storage views will be empty.

### `IOpenTelemetryLiveFeed` *(Core — `Elsa.Diagnostics.OpenTelemetry.Core`)*
- **Signature:** `ValueTask PublishAsync(OpenTelemetryBatch batch, …)`, `IAsyncEnumerable<OpenTelemetryStreamItem> SubscribeAsync(OpenTelemetryTraceFilter filter, CancellationToken cancellationToken)`.
- **Default impl:** `InMemoryOpenTelemetryLiveFeed` — each subscriber gets an independent bounded channel (capacity `SubscriberChannelCapacity`); a slow consumer never blocks the ingestion path, its overflowed items are dropped and a `DroppedItems` summary is delivered in-band. Per-signal filtering (`MatchesResource`/`MatchesTrace`/`MatchesLog`/`MatchesMetricPoint`) is applied per subscriber.
- **Override:** replace to source the live feed from an external broker (Redis Streams, a message bus) so multiple hosts fan into one stream.

### `IOpenTelemetryIngestor` *(Core — `Elsa.Diagnostics.OpenTelemetry.Core`)*
- **Signature:** `ValueTask IngestAsync(OpenTelemetryBatch batch, OpenTelemetryIngestionContext ingestionContext, …)`. The built-in compatibility overload without context supplies `OpenTelemetryIngestionContext.Untrusted`. Existing custom ingestors that implement only the legacy overload remain compatible and safely ignore authority context until they explicitly override the new overload.
- **Default impl:** `OpenTelemetryIngestor` — redacts the batch (`IOpenTelemetryRedactor`), awaits all `IOpenTelemetryIngestionContributor` instances, writes it to `IOpenTelemetryStore` (which in turn marks resources seen in the source registry), then publishes to `IOpenTelemetryLiveFeed`. This is the seam the OTLP collector endpoints write to.
- **Override:** replace to tee ingested batches elsewhere (e.g. forward to an external collector) while keeping the in-memory store for the UI.

### `IOtlpRequestAuthenticator` *(`Elsa.Diagnostics.OpenTelemetry`)*
- **Signature:** `ValueTask<OtlpRequestAuthenticationResult> AuthenticateAsync(HttpContext httpContext, CancellationToken cancellationToken)`.
- **Default impl:** scoped `DefaultOtlpRequestAuthenticator`. It preserves the API-key/loopback policy, produces no tenant claims for the global API key, and marks accepted loopback calls explicitly untrusted.
- **Override:** register a host authenticator before `AddOpenTelemetryDiagnosticsServices(...)` to validate per-source credentials and return an `OpenTelemetryIngestionContext.Authenticated(...)` containing authoritative source identity and scope claims. Authentication runs before body read, decompression, or protobuf parsing.

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
- **`OtlpHttpIngestionHandler`** is the public, non-replaceable orchestration surface shared by both receiver compositions: authenticate → read/decompress under the configured limit → parse → call the context-aware ingestor. The parser remains internal machinery.
- **`MapOpenTelemetryOtlpReceiver()`** maps exactly `POST {base}/traces`, `metrics`, and `logs` on any ASP.NET Core `IEndpointRouteBuilder`. Shell composition uses this same owner mapper; do not map the collector routes a second time in one route table.
- The **SSE `stream` endpoint** is a separate transport surface; the wire JSON/SSE shape is owned by `OpenTelemetryStreamItemSerializer` + `OpenTelemetrySseFormatter` (see [`README.md`](README.md)).

## Deferred

- **gRPC ingestion** — kept behind `OpenTelemetryDiagnosticsOptions.EnableGrpc` (default `false`); no gRPC route is mapped. The binding is host-specific.

## Temporary EF Core compatibility override

`Elsa.Diagnostics.OpenTelemetry.Persistence.EFCore` and the
`DiagnosticsOpenTelemetryPersistenceEFCoreSqlite` feature remain as temporary comparison, oracle, and
compatibility implementations. They have not been removed: #646 owns the retained performance measurement,
and #647 owns the subsequent EF deletion. They are not the first-party/reference-host durable composition.

---

## Notes

- Live stream items (`OpenTelemetryStreamItem`) carry **no monotonic sequence/id**, so the SSE stream offers **no `Last-Event-ID` resume** (unlike Structured Logs, which resumes from bounded `ReadAfterAsync` pages using an opaque committed cursor). SSE frames use typed `event:` names (`resource`/`trace`/`metric`/`log`/`dropped`) with no `id:`.
- This domain uses **SSE instead of SignalR** and exposes the same OTLP handler through the owner Minimal API route mapper. See [`README.md`](README.md#deviations-from-the-elsa-core-source).

---

## Cross-references

- Repo-wide index: [`../../../../EXTENSION_POINTS.md`](../../../../EXTENSION_POINTS.md).
- Feature documentation: [`README.md`](README.md).
- Sibling diagnostics domain: [`../StructuredLogs/EXTENSION_POINTS.md`](../StructuredLogs/EXTENSION_POINTS.md).
- Constitutional basis: §2.6.2 + §2.22.1.
