# Extension points — Diagnostics: Structured Logs domain

The per-domain catalog (framework §2.22.1). Anchored at `Elsa.Diagnostics.StructuredLogs` — the server feature that captures host log events into an in-memory store and exposes them over HTTP + Server-Sent Events. All seams are **overridable `.Core` contracts**; there are no contributor interfaces or published events in v1.

The capture/serve pipeline is decomposed into three single-responsibility roles so a durable backend can replace just one of them:

- **`IStructuredLogSink`** assigns the monotonic `Sequence`, stamps the entry once, then dispatches to the store **and** the live publisher (keeping history and the tail consistent).
- **`IStructuredLogStore`** is pure history (`Append`/`GetRecentAsync`/`GetAfterAsync`). Swap this to make logs durable.
- **`IStructuredLogLiveFeed` / `IStructuredLogLivePublisher`** is the in-process fan-out to SSE subscribers. It stays in-process for every storage backend.

---

## Overridable contracts

All contracts live in `Elsa.Diagnostics.StructuredLogs.Core`. The feature registers `InMemoryStructuredLogStore` (history), `InMemoryStructuredLogLiveFeed` (live fan-out, also the publisher), a `StructuredLogSink` (sequencing/dispatch), and a `LocalStructuredLogSourceProvider`. The store is registered with `TryAddSingleton` so a persistence feature can override it; the others use `AddSingleton`.

### `IStructuredLogStore` *(Core — `Elsa.Diagnostics.StructuredLogs.Core`)*
- **Signature:** `void Append(StructuredLogEntry entry)`, `Task<IReadOnlyList<StructuredLogEntry>> GetRecentAsync(StructuredLogFilter filter, CancellationToken ct = default)`, `Task<IReadOnlyList<StructuredLogEntry>> GetAfterAsync(long afterSequence, StructuredLogFilter filter, CancellationToken ct = default)`, `Task<long> GetHighWaterMarkAsync(CancellationToken ct = default)`. Queries are async so persistent backends never block a request thread on storage I/O; `Append` stays synchronous because it sits on the logging hot path.
- **Default impl:** `InMemoryStructuredLogStore` — a bounded ring buffer (capacity from `StructuredLogsOptions.BufferCapacity`). Stores entries verbatim; the externally-assigned `Sequence` is the cursor.
- **Override:** register your own `IStructuredLogStore` to persist entries and serve `recent`/Last-Event-ID resume from durable storage. Pure *replace-one-keep-rest* override. Shipped override: **`EfCoreStructuredLogStore`** (see _Persistence_ below). Because the default uses `TryAddSingleton`, a persistence feature's plain `AddSingleton<IStructuredLogStore>` wins regardless of feature order.

### `IStructuredLogLiveFeed` *(Core — `Elsa.Diagnostics.StructuredLogs.Core`)*
- **Signature:** `IAsyncEnumerable<StructuredLogStreamItem> Subscribe(StructuredLogFilter filter, CancellationToken cancellationToken)`.
- **Default impl:** `InMemoryStructuredLogLiveFeed` — each subscriber gets an independent bounded channel; a slow consumer never blocks the logging path, its overflowed entries are dropped and a `DroppedEntriesSignal` is delivered in-band.
- **Override:** replace to source the live feed from an external broker (Redis Streams, a message bus) so multiple hosts fan into one tail.

### `IStructuredLogLivePublisher` *(Core — `Elsa.Diagnostics.StructuredLogs.Core`)*
- **Signature:** `void Publish(StructuredLogEntry entry)`.
- **Default impl:** `InMemoryStructuredLogLiveFeed` (same instance as the feed). This is the write side the sink pushes stamped entries into.
- **Override:** replace alongside `IStructuredLogLiveFeed` when relocating the fan-out to an external broker.

### `IStructuredLogSink` *(Core — `Elsa.Diagnostics.StructuredLogs.Core`)*
- **Signature:** `void Emit(StructuredLogEntry entry)`.
- **Default impl:** `StructuredLogSink` — assigns the monotonic `Sequence` (seeded from `store.GetHighWaterMarkAsync()` lazily on the first emit, guaranteed to complete before the first sequence is assigned), stamps the entry, then calls `store.Append(stamped)` + `publisher.Publish(stamped)`. This is the seam the capture `ILoggerProvider` writes to.
- **Override:** replace to tee captured entries elsewhere (e.g. forward to an external collector) while keeping the in-memory store for the UI.

### `IStructuredLogSourceProvider` *(Core — `Elsa.Diagnostics.StructuredLogs.Core`)*
- **Signature:** `LogSource GetLocalSource()`, `IReadOnlyList<LogSource> GetKnownSources()`.
- **Default impl:** `LocalStructuredLogSourceProvider` — exposes the single local host as the only known source; stamps every captured entry with the local source id.
- **Override:** replace to enumerate multiple remote sources in a multi-host deployment without changing the entry contract.

---

## Persistence (EF Core override)

`Elsa.Diagnostics.StructuredLogs.Persistence.EFCore` ships **`EfCoreStructuredLogStore`**, an `IStructuredLogStore` override that makes captured logs durable. It is enabled per-provider, e.g. the `DiagnosticsStructuredLogsPersistenceEFCoreSqlite` shell feature.

Key design points (so the override stays safe on a high-volume, hot logging path):
- **Non-blocking `Append`** writes to a bounded `Channel` (drop-oldest); a background drain loop batch-inserts into the `StructuredLogsDbContext`. `Append` never touches the database synchronously.
- **Feedback-loop break:** the persistence feature configures the DbContext factory with `UseLoggerFactory(NullLoggerFactory.Instance)`, so the store's own "Executed DbCommand" logs are not captured and re-persisted.
- **Durable cursor:** `PersistedStructuredLogEntry` uses its own auto-increment `long Id` (not the per-process `Sequence`, which can repeat across restarts and which SQLite would not honour as a `RowNumber`). Queries order by `Id`. The entity deliberately does **not** derive from `Entity`.
- **Retention:** the drain loop periodically prunes rows below `maxId - maxRetainedEntries` so the table stays bounded like the in-memory ring buffer.
- **Startup ordering:** a migration startup task runs first; a draining startup task (`StartStructuredLogDrainingStartupTask`) then calls `store.StartDraining()`. Batch inserts retry briefly to tolerate the pre-migration window.

---

## Notes

- The capture path (`StructuredLogCaptureProvider` → `StructuredLogCapturingLogger` → `StructuredLogEntryFactory`) is **not** an extension point: it is the internal bridge from `Microsoft.Extensions.Logging` into `IStructuredLogSink`. It ignores its own category to prevent feedback loops and swallows sink failures so capture never throws into host logging (FR-010).
- The HTTP/SSE wire shape is owned by `StructuredLogEntrySerializer` and `StructuredLogSseFormatter`; see [`README.md`](README.md) for the contract.

---

## Cross-references

- Repo-wide index: [`../../../../EXTENSION_POINTS.md`](../../../../EXTENSION_POINTS.md).
- Feature documentation: [`README.md`](README.md).
- Constitutional basis: §2.6.2 + §2.22.1.
