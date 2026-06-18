# Elsa.Diagnostics.StructuredLogs

Captures host log events (`Microsoft.Extensions.Logging`) into a store and exposes them to Elsa Studio over HTTP (recent history, known sources) and Server-Sent Events (live tail). It is a **server** shell feature. The store role is isolated behind `IStructuredLogStore` so durable storage can be swapped in without touching capture, transport, or the UI — shipped today by `Elsa.Diagnostics.StructuredLogs.Persistence.EFCore` (see _Persistence_).

Feature name (manifest / appsettings key): **`DiagnosticsStructuredLogs`**.

## What this feature provides

- **Three decomposed roles** behind separate contracts so a durable backend can replace just the history store:
  - **`StructuredLogSink`** → `IStructuredLogSink` — assigns the monotonic `Sequence` (seeded from the store's high-water-mark on startup), stamps each entry once, then dispatches to the store **and** the live publisher.
  - **`InMemoryStructuredLogStore`** → `IStructuredLogStore` — a bounded ring buffer holding recent history. Registered with `TryAddSingleton` so a persistence feature can override it.
  - **`InMemoryStructuredLogLiveFeed`** → `IStructuredLogLiveFeed` + `IStructuredLogLivePublisher` — an independent bounded channel per live subscriber (in-process fan-out, stays in-process for every storage backend).
- **`LocalStructuredLogSourceProvider`** → `IStructuredLogSourceProvider` — exposes the single local host as the only known source and stamps every captured entry with its source id.
- **`StructuredLogCaptureProvider`** — an `ILoggerProvider` (registered via `TryAddEnumerable`) that bridges host logging into `IStructuredLogSink`. It ignores its own categories (prefix `Elsa.Diagnostics.StructuredLogs`) to prevent feedback loops and swallows sink failures so capture never throws into the host logging path.
- **Endpoints** (FastEndpoints, auto-mapped via `app.MapShells()`):
  - `GET /_elsa/studio/diagnostics/structured-logs/recent` — newest-aligned recent entries as a JSON array.
  - `GET /_elsa/studio/diagnostics/structured-logs/sources` — known log sources.
  - `GET /_elsa/studio/diagnostics/structured-logs/stream` — live tail as Server-Sent Events.
- **Serialization helpers** (`public sealed`, branch-tested): `StructuredLogEntrySerializer` (wire JSON shape) and `StructuredLogSseFormatter` (SSE framing); `StructuredLogFilterBinder` (query-string → `StructuredLogFilter`, rejecting malformed input with `InvalidLogQueryException`).

## Options (`StructuredLogsOptions`)

Exposed manifest settings: **Minimum level** (default `Information`), **Buffer capacity** (default `2000`), **Service name** (defaults to the process name), **Source display name**. Additional tunables on the options type: `SubscriberQueueCapacity`, `MaxRecentQuerySize`, the capture caps (`MaxCapturedProperties`, `MaxCapturedScopeDepth`, `MaxPropertyValueLength`), and the three endpoint paths.

## Authorization

All three endpoints call `ConfigurePermissions("Diagnostics:StructuredLogs")` (the FastEndpoints permission model used across the foundation). They are **default-permissive (anonymous)** while host endpoint security is disabled; once the host enables security (`EndpointSecurityOptions`), it assigns this permission to authorized principals. There is no separate ASP.NET named authorization policy.

## Query parameters (recent + stream)

| Parameter | Applies to | Effect |
|---|---|---|
| `minLevel` | recent, stream | Only entries at or above this `LogLevel`. Invalid value → `400`. |
| `category` | recent, stream | Exact category match. |
| `source` | recent, stream | Exact source-id match. |
| `take` | recent | Max entries returned (clamped to `MaxRecentQuerySize`). Invalid value → `400`. |

## SSE event contract (`stream`)

- **`event: entry`** — carries `id: <sequence>` and a `data:` line with the entry JSON. The `id` lets a reconnecting client send `Last-Event-ID`; the server replays buffered entries after that sequence before resuming the live tail.
- **`event: dropped`** — no `id`; `data:` carries `{ droppedCount, since }`. Emitted in-band when a slow consumer's bounded queue overflowed and entries were dropped (backpressure), so the client learns of loss without a side channel.
- **`: keep-alive`** — an SSE comment heartbeat (every 15s) that keeps idle connections open.

## Capture scope (host-wide logging)

`StructuredLogCaptureProvider` is registered as an `ILoggerProvider` in the feature's `ConfigureServices`. Because the CShells default shell shares the host's root `ILoggerFactory`, this captures **host-wide** log events — validated against `Elsa.Server`, where the feed surfaces EF Core, FastEndpoints, and other host-level logs, not just the feature's own scope. If a future deployment topology gives a shell an isolated logger factory and capture only sees that scope, register the provider at the host level instead (on the host `builder.Services`).

## Persistence

`Elsa.Diagnostics.StructuredLogs.Persistence.EFCore` provides **`EfCoreStructuredLogStore`**, a durable `IStructuredLogStore` override; the `DiagnosticsStructuredLogsPersistenceEFCoreSqlite` shell feature wires it onto SQLite. Enabling it makes captured logs survive restarts and serve `recent` / Last-Event-ID resume from the database, while capture, the live tail, and the UI stay unchanged.

It is built for the hot logging path: `Append` is non-blocking (writes to a bounded channel; a background drain loop batch-inserts), the store's own DbContext logs are silenced (`NullLoggerFactory`) to avoid a capture→persist feedback loop, the table is pruned to a retention cap, and a durable auto-increment `Id` (not the per-process `Sequence`) is the cursor. See the persistence project's [`EXTENSION_POINTS.md`](EXTENSION_POINTS.md#persistence-ef-core-override) section for the full rationale.

## Replacing the defaults

All store/source contracts are overridable — see [`EXTENSION_POINTS.md`](EXTENSION_POINTS.md). The common extension is replacing `IStructuredLogStore` with a persistent implementation (as the EF Core persistence feature does) while leaving capture and transport unchanged.

## Owned exception surface

- **`StructuredLogsException`** — the feature-boundary base exception (framework §2.23.5).
- **`InvalidLogQueryException`** — raised by `StructuredLogFilterBinder` for malformed query input; surfaced as `400` by the endpoints. Replaces raw parse failures.

## Cross-references

- Domain extension points: [`EXTENSION_POINTS.md`](EXTENSION_POINTS.md).
- Repo-wide index: [`../../../../EXTENSION_POINTS.md`](../../../../EXTENSION_POINTS.md).
