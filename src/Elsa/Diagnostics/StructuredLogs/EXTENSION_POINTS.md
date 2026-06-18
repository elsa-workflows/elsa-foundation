# Extension points — Diagnostics: Structured Logs domain

The per-domain catalog (framework §2.22.1). Anchored at `Elsa.Diagnostics.StructuredLogs` — the server feature that captures host log events into an in-memory store and exposes them over HTTP + Server-Sent Events. All seams are **overridable `.Core` contracts**; there are no contributor interfaces or published events in v1.

---

## Overridable contracts

All four contracts live in `Elsa.Diagnostics.StructuredLogs.Core`. The feature registers a single `InMemoryStructuredLogStore` as the default implementation of the three store-side contracts, plus a `LocalStructuredLogSourceProvider`. Each is registered with `AddSingleton` (not `TryAdd`), so a host replaces one by registering its own implementation *after* the feature.

### `IStructuredLogStore` *(Core — `Elsa.Diagnostics.StructuredLogs.Core`)*
- **Signature:** `void Append(StructuredLogEntry entry)`, `IReadOnlyList<StructuredLogEntry> GetRecent(StructuredLogFilter filter)`, `IReadOnlyList<StructuredLogEntry> GetAfter(long afterSequence, StructuredLogFilter filter)`.
- **Default impl:** `InMemoryStructuredLogStore` — a bounded ring buffer (capacity from `StructuredLogsOptions.BufferCapacity`). Assigns a monotonic per-host `Sequence` on append.
- **Override:** register your own `IStructuredLogStore` to persist entries (e.g. EF Core, a time-series database) and serve `recent`/Last-Event-ID resume from durable storage. Pure *replace-one-keep-rest* override.

### `IStructuredLogLiveFeed` *(Core — `Elsa.Diagnostics.StructuredLogs.Core`)*
- **Signature:** `IAsyncEnumerable<StructuredLogStreamItem> Subscribe(StructuredLogFilter filter, CancellationToken cancellationToken)`.
- **Default impl:** `InMemoryStructuredLogStore` — each subscriber gets an independent bounded channel; a slow consumer never blocks the logging path, its overflowed entries are dropped and a `DroppedEntriesSignal` is delivered in-band.
- **Override:** replace to source the live feed from an external broker (Redis Streams, a message bus) so multiple hosts fan into one tail.

### `IStructuredLogSink` *(Core — `Elsa.Diagnostics.StructuredLogs.Core`)*
- **Signature:** `void Emit(StructuredLogEntry entry)`.
- **Default impl:** `InMemoryStructuredLogStore` (`Emit` forwards to `Append`). This is the seam the capture `ILoggerProvider` writes to.
- **Override:** replace to tee captured entries elsewhere (e.g. forward to an external collector) while keeping the in-memory store for the UI.

### `IStructuredLogSourceProvider` *(Core — `Elsa.Diagnostics.StructuredLogs.Core`)*
- **Signature:** `LogSource GetLocalSource()`, `IReadOnlyList<LogSource> GetKnownSources()`.
- **Default impl:** `LocalStructuredLogSourceProvider` — exposes the single local host as the only known source; stamps every captured entry with the local source id.
- **Override:** replace to enumerate multiple remote sources in a multi-host deployment without changing the entry contract.

---

## Notes

- The capture path (`StructuredLogCaptureProvider` → `StructuredLogCapturingLogger` → `StructuredLogEntryFactory`) is **not** an extension point: it is the internal bridge from `Microsoft.Extensions.Logging` into `IStructuredLogSink`. It ignores its own category to prevent feedback loops and swallows sink failures so capture never throws into host logging (FR-010).
- The HTTP/SSE wire shape is owned by `StructuredLogEntrySerializer` and `StructuredLogSseFormatter`; see [`README.md`](README.md) for the contract.

---

## Cross-references

- Repo-wide index: [`../../../../EXTENSION_POINTS.md`](../../../../EXTENSION_POINTS.md).
- Feature documentation: [`README.md`](README.md).
- Constitutional basis: §2.6.2 + §2.22.1.
