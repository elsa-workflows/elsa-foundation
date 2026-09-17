# Elsa.Diagnostics.StructuredLogs

Captures the log events (`Microsoft.Extensions.Logging`) of the shell that enables it into a store and exposes them to Elsa Studio over HTTP (recent history, known sources) and Server-Sent Events (live tail). It is a **server** shell feature. Without a persistence feature, `InMemoryStructuredLogStore` remains the default. The store role is isolated behind `IStructuredLogStore` so durable storage can replace that default without changing capture, transport, or the UI. The current first-party durable composition is EF Core (see _Persistence_).

Feature name (manifest / appsettings key): **`DiagnosticsStructuredLogs`**.

## What this feature provides

- **Three decomposed roles** behind separate contracts so a durable backend can replace just the history store:
  - **`StructuredLogSink`** → `IStructuredLogSink` — assigns process-local display-only `Sequence` metadata without reading the store, submits entries to the store without blocking capture, and publishes process-local wake hints after commitment. A durable store assigns the committed sequence from its own lifetime high-water in its append path. Capture starts before a durable store is ready, and an entry the store rejects then is dropped without affecting later entries.
  - **`InMemoryStructuredLogStore`** → `IStructuredLogStore` — a bounded ring buffer holding recent history. Registered with `TryAddSingleton` so a persistence feature can override it.
  - **`InMemoryStructuredLogLiveFeed`** → `IStructuredLogLiveFeed` + `IStructuredLogLivePublisher` — an independent bounded channel per subscriber. For SSE it is only a wake hint; durable storage remains the payload and ordering authority.
- **`LocalStructuredLogSourceProvider`** → `IStructuredLogSourceProvider` — exposes the single local host as the only known source and stamps every captured entry with its source id.
- **`StructuredLogCaptureProvider`** — an `ILoggerProvider` (registered via `TryAddEnumerable`) that bridges the shell's logging into `IStructuredLogSink` (see _Capture scope_). It ignores its own categories (prefix `Elsa.Diagnostics.StructuredLogs`) to prevent feedback loops and swallows sink failures so capture never throws into the host logging path.
- **Endpoints** (explicit Minimal APIs mapped by `StructuredLogsFeature.MapEndpoints` through
  `StructuredLogsApi.MapStructuredLogsApi`):
  - `GET /_elsa/studio/diagnostics/structured-logs/recent` — newest-aligned recent entries as a JSON array.
  - `GET /_elsa/studio/diagnostics/structured-logs/sources` — known log sources.
  - `GET /_elsa/studio/diagnostics/structured-logs/stream` — live tail as Server-Sent Events with
    versioned opaque committed cursors in `id`/`Last-Event-ID`.

`StructuredLogEntry.Sequence` is display-only logical metadata. Concurrent writers may produce the same
value, so replay, ordering, and replay/live de-duplication use `StructuredLogReplayCursor` exclusively.
Malformed, expired, trimmed, wrong-scope, and wrong-stream cursors all return the same non-disclosing
`409 Conflict` response.

`EfStructuredLogStore` is the first-party durable adapter. It consumes the module's own
diagnostic-record contract, returns the committed entry only after durable append acknowledgement, serves
bounded read-after pages in committed order, and preserves lifetime logical high-water independently of
retention. `StructuredLogSink` publishes the process-local wake hint from that committed result.
- **Serialization helpers** (`public sealed`, branch-tested): `StructuredLogEntrySerializer` (wire JSON shape) and `StructuredLogSseFormatter` (SSE framing); `StructuredLogFilterBinder` (query-string → `StructuredLogFilter`, rejecting malformed input with `InvalidLogQueryException`).

## Options (`StructuredLogsOptions`)

Exposed manifest settings: **Minimum level** (default `Information`), **Buffer capacity** (default `2000`), **Service name** (defaults to the process name), **Source display name**. Additional tunables on the options type: `SubscriberQueueCapacity`, `MaxRecentQuerySize`, `TailPollInterval`, the capture caps (`MaxCapturedProperties`, `MaxCapturedScopeDepth`, `MaxPropertyValueLength`), and the three endpoint paths.

## Authorization

All three endpoints use the shared Foundation policy model and require **any** of
`Diagnostics:StructuredLogs` or the retained administrative wildcard. Authentication establishes a
normalized principal before endpoint selection; anonymous and rejected, non-normalized identities receive
`401`, while authenticated callers without the permission receive `403`. `StructuredLogsPermissionContributor` catalogs
the non-wildcard permission once under owner `Elsa.Diagnostics.StructuredLogs`, with no implication. The
wildcard remains a grant and is deliberately not contributed to the catalog.

The routes carry explicit module ownership, Minimal API authoring, and security-disposition metadata, so
they can coexist with third-party FastEndpoints routes while using the same Foundation evaluator.

## Query parameters (recent + stream)

| Parameter | Applies to | Effect |
|---|---|---|
| `minLevel` | recent, stream | Only entries at or above this `LogLevel`. Invalid value → `400`. |
| `category` | recent, stream | Exact category match. |
| `source` | recent, stream | Exact source-id match. |
| `take` | recent | Max entries returned (clamped to `MaxRecentQuerySize`). Invalid value → `400`. |

## SSE event contract (`stream`)

- **`event: entry`** — carries `id: <opaque committed cursor>` and a `data:` line with the entry JSON. The id lets a reconnecting client send `Last-Event-ID`; the server validates that opaque anchor and tails bounded durable read-after pages from it. The process-local feed can wake the tail early, while bounded polling discovers commits from other hosts. Feed payloads and drop signals are not forwarded.
- **`: keep-alive`** — an SSE comment heartbeat (every 15s) that keeps idle connections open.

`StructuredLogSseWriter` is module-local and framework-neutral. It flushes each frame, links request
cancellation, and bounds cleanup of a pending async read to five seconds. The endpoint validates filters,
replay cursors, and its first durable page before starting the SSE response.

## Capture scope (the shell's loggers, not the host's)

`StructuredLogCaptureProvider` is registered as an `ILoggerProvider` in the feature's `ConfigureServices`, so it joins the service container of the shell that enables the feature. CShells builds that container from copies of the host's service registrations plus the shell's features. The shell therefore gets its own `ILoggerFactory`, which writes to the logger providers the host registered, such as the console, and to capture. The host's root `ILoggerFactory` never receives the capture provider.

- **Captured:** loggers resolved from the shell's container. That covers the shell's features and background tasks, and EF Core for the shell's `DbContext`s.
- **Not captured:** loggers resolved from the root host. In `Elsa.Workbench` these include `CShells.*` (shell activation, draining and endpoint registration), `Microsoft.Hosting.Lifetime`, and `Elsa.Workbench.Readiness`. They reach the console but never the store.

Capturing root host loggers would need a host-level provider that forwards into a shell's sink. That provider would have to choose a shell, drop events logged before that shell's store starts or after it stops, and follow shell reloads. No such forwarder exists.

## Persistence

`DiagnosticsStructuredLogsEntityFrameworkCore` and `DiagnosticsOpenTelemetryEntityFrameworkCore` is the catalog-discoverable, first-party durable diagnostics feature. They are the reference `Elsa.Workbench` composition and install the domain-owned Structured Logs and OpenTelemetry EF features. `StructuredLogsEntityFrameworkCoreFeature` replaces `IStructuredLogStore` with `EfStructuredLogStore` and owns the immutable diagnostic-record table. Capture, the in-process live wake feed, and the UI remain unchanged.

`EfStructuredLogStore` uses its diagnostic-record table for durable committed cursors, bounded read-after pages, and lifetime logical high-water independent of retention. Its bounded drain completes accepted appends only after durable acknowledgement.

## Replacing the defaults

All store/source contracts are overridable — see [`EXTENSION_POINTS.md`](EXTENSION_POINTS.md). The shipped durable replacement is EF Core; custom hosts can replace `IStructuredLogStore` while leaving capture and transport unchanged.

## Owned exception surface

- **`StructuredLogsException`** — the feature-boundary base exception (framework §2.23.5).
- **`InvalidLogQueryException`** — raised by `StructuredLogFilterBinder` for malformed query input; surfaced as `400` by the endpoints. Replaces raw parse failures.

## Cross-references

- Domain extension points: [`EXTENSION_POINTS.md`](EXTENSION_POINTS.md).
- Repo-wide index: [`../../../../EXTENSION_POINTS.md`](../../../../EXTENSION_POINTS.md).
- Migration evidence: [`../../../../docs/reports/structured-logs-minimal-api-migration-2026-08.md`](../../../../docs/reports/structured-logs-minimal-api-migration-2026-08.md).
