# Extension points — Diagnostics: Structured Logs domain

The per-domain catalog (framework §2.22.1). Anchored at `Elsa.Diagnostics.StructuredLogs` — the server feature that captures host log events into an in-memory store and exposes them over HTTP + Server-Sent Events. All seams are **overridable `.Core` contracts**; there are no contributor interfaces or published events in v1.

The capture/serve pipeline is decomposed into three single-responsibility roles so a durable backend can replace just one of them:

- **`IStructuredLogSink`** assigns display-only `Sequence` metadata, submits to the store, and publishes a local wake hint only after commitment.
- **`IStructuredLogStore`** owns append commit, lifetime high-water, recent history, opaque-cursor bounded tail reads, and exact retention. Swap this to make logs durable.
- **`IStructuredLogLiveFeed` / `IStructuredLogLivePublisher`** is an in-process wake channel for SSE durable tails. It stays in-process for every storage backend.

---

## Overridable contracts

All contracts live in `Elsa.Diagnostics.StructuredLogs.Core`. The feature registers `InMemoryStructuredLogStore` (history), `InMemoryStructuredLogLiveFeed` (live fan-out, also the publisher), a `StructuredLogSink` (sequencing/dispatch), and a `LocalStructuredLogSourceProvider`. The store is registered with `TryAddSingleton` so a persistence feature can override it; the others use `AddSingleton`.

### `IStructuredLogStore` *(Core — `Elsa.Diagnostics.StructuredLogs.Core`)*
- **Signature:** `ValueTask<StructuredLogEntry> AppendAsync(...)`, `Task<long> GetHighWaterMarkAsync(...)`, `Task<IReadOnlyList<StructuredLogEntry>> GetRecentAsync(...)`, `Task<StructuredLogReplayCursor?> GetTailCursorAsync(...)`, `Task<StructuredLogReadPage> ReadAfterAsync(...)`, and `Task TrimAsync(int keepNewest, ...)`.
- **Contract:** append returns the committed entry carrying the authoritative cursor. `ReadAfterAsync` validates an optional source/scope/stream-bound opaque anchor and returns one oldest-first bounded snapshot page strictly after it, plus the next scanned cursor and `HasMore`. Cursor codecs and decoded provider positions stay internal to each adapter. The lifetime logical high-water never rewinds after retention or restart. `Sequence` is display metadata and is neither unique nor a replay identity.
- **Default impl:** `InMemoryStructuredLogStore` — a bounded ring buffer with process-lifetime cursor and high-water state.
- **Override:** register your own `IStructuredLogStore` to persist entries and serve recent/bounded tail reads. Durable adapters own a bounded nonblocking ingest queue and complete `AppendAsync` only after commit. `ReadAfterAsync` must scan in committed cursor order and advance its next cursor over filtered-out records. The default uses `TryAddSingleton`, so a persistence feature's `AddSingleton<IStructuredLogStore>` wins regardless of feature order.

### `IStructuredLogLiveFeed` *(Core — `Elsa.Diagnostics.StructuredLogs.Core`)*
- **Signature:** `IAsyncEnumerable<StructuredLogStreamItem> Subscribe(StructuredLogFilter filter, CancellationToken cancellationToken)`.
- **Default impl:** `InMemoryStructuredLogLiveFeed` — each subscriber gets an independent bounded channel; a slow consumer never blocks the logging path, its overflowed entries are dropped and a `DroppedEntriesSignal` is delivered in-band.
- **Override:** replace to tune local wake distribution. SSE correctness does not depend on this feed: it is
  only a wake hint for the durable store tail, which also polls on a bound interval.

### `IStructuredLogLivePublisher` *(Core — `Elsa.Diagnostics.StructuredLogs.Core`)*
- **Signature:** `void Publish(StructuredLogEntry entry)`.
- **Default impl:** `InMemoryStructuredLogLiveFeed` (same instance as the feed). This is the write side the sink uses for committed local wake hints.
- **Override:** replace alongside `IStructuredLogLiveFeed` when changing wake distribution. Durable store reads remain authoritative.

### `IStructuredLogSink` *(Core — `Elsa.Diagnostics.StructuredLogs.Core`)*
- **Signature:** `void Emit(StructuredLogEntry entry)`.
- **Default impl:** `StructuredLogSink` — assigns a process-local `Sequence` seeded from the store's lifetime logical high-water, starts `AppendAsync` without blocking the logging hot path, and publishes a wake hint only for the committed result. Append failures never publish and never escape into host logging.
- **Override:** replace to tee captured entries elsewhere (e.g. forward to an external collector) while keeping the in-memory store for the UI.

### `IStructuredLogSourceProvider` *(Core — `Elsa.Diagnostics.StructuredLogs.Core`)*
- **Signature:** `LogSource GetLocalSource()`, `IReadOnlyList<LogSource> GetKnownSources()`.
- **Default impl:** `LocalStructuredLogSourceProvider` — exposes the single local host as the only known source; stamps every captured entry with the local source id.
- **Override:** replace to enumerate multiple remote sources in a multi-host deployment without changing the entry contract.

---

## Persistence

### Groundwork diagnostic records

`Elsa.Diagnostics.StructuredLogs.Persistence.Groundwork` ships **`GroundworkStructuredLogStore`**, the
conformance adapter over Groundwork's specialized `IDiagnosticRecordStore`. It uses provider-issued opaque
cursors, idempotent batch operation ids, bounded declared predicates, snapshot continuation, exact trim, and
provider inspection state for lifetime high-water. The Elsa Core contract has no Groundwork dependency; hosts
construct and register the adapter with their selected Groundwork provider and a
`StructuredLogStoreBinding` (tenant, host storage scope, and logical stream).

### Temporary EF Core compatibility override

`Elsa.Diagnostics.StructuredLogs.Persistence.EFCore` ships **`EfCoreStructuredLogStore`**, an `IStructuredLogStore` override that makes captured logs durable. It is enabled per-provider, e.g. the `DiagnosticsStructuredLogsPersistenceEFCoreSqlite` shell feature.

Key design points (so the override stays safe on a high-volume, hot logging path):
- **Non-blocking append** writes to the existing bounded `Channel` (drop-oldest); a background drain loop batch-inserts into the `StructuredLogsDbContext` and completes accepted append operations after commit.
- **Feedback-loop break:** the persistence feature configures the DbContext factory with `UseLoggerFactory(NullLoggerFactory.Instance)`, so the store's own "Executed DbCommand" logs are not captured and re-persisted.
- **Compatibility cursor/state:** `PersistedStructuredLogEntry` uses its existing auto-increment `long Id` rather than `Sequence`. The adapter-private codec wraps it in the opaque boundary, and reserved hidden state rows in the existing table preserve lifetime logical high-water independently of retained tail rows. State and appended data commit in one `SaveChanges`; failed batches cannot advance high-water. State is excluded from recent history, read-after pages, and retention counts; exact reserved input is rejected and concurrent initializers safely converge on the maximum. No EF schema or migration surface was added for this change; Groundwork is the durable cursor conformance target.
- **Retention:** the drain loop periodically prunes rows below `maxId - maxRetainedEntries` so the table stays bounded like the in-memory ring buffer.
- **Startup ordering:** a migration startup task runs first; a draining startup task (`StartStructuredLogDrainingStartupTask`) then calls `store.StartDraining()`. Batch inserts retry briefly to tolerate the pre-migration window.
- **Shutdown ordering:** `StopStructuredLogDrainingShellTerminator` completes and flushes the channel
  during graceful shell termination, after `Start`-phase task producers have stopped and while the
  DbContext factory is still usable. Async store disposal remains the bounded fallback for plain DI
  containers and emergency paths where CShells skips terminators.

---

## Notes

- The capture path (`StructuredLogCaptureProvider` → `StructuredLogCapturingLogger` → `StructuredLogEntryFactory`) is **not** an extension point: it is the internal bridge from `Microsoft.Extensions.Logging` into `IStructuredLogSink`. It ignores its own category to prevent feedback loops and swallows sink failures so capture never throws into host logging (FR-010).
- The HTTP/SSE wire shape is owned by `StructuredLogEntrySerializer` and `StructuredLogSseFormatter`; see [`README.md`](README.md) for the contract.

---

## Cross-references

- Repo-wide index: [`../../../../EXTENSION_POINTS.md`](../../../../EXTENSION_POINTS.md).
- Feature documentation: [`README.md`](README.md).
- Constitutional basis: §2.6.2 + §2.22.1.
