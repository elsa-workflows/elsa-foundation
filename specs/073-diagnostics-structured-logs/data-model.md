# Phase 1 Data Model — Structured Logs Diagnostics

Entities live in `Elsa.Diagnostics.StructuredLogs.Core`. They are transport/storage-neutral so the
later persistence slice can map them without changing consumers.

## StructuredLogEntry

A single captured log event.

| Field | Type | Notes |
|---|---|---|
| `Sequence` | `long` | Monotonic per-host ordering id assigned by the store on append. |
| `Timestamp` | `DateTimeOffset` | When the event was emitted. |
| `Level` | `LogLevel` | `Microsoft.Extensions.Logging.LogLevel`. |
| `Category` | `string` | Logger category name. |
| `EventId` | `int?` / `string?` | Optional event id/name. |
| `Message` | `string` | Rendered message (formatted). |
| `MessageTemplate` | `string?` | Optional original template (FR-001 — permitted, optional). |
| `Properties` | `IReadOnlyList<LogProperty>` | Bounded set; capped by `MaxCapturedProperties` / `MaxPropertyValueLength`. |
| `Scopes` | `IReadOnlyList<LogScope>` | Bounded scope chain; depth capped by `MaxCapturedScopeDepth`. |
| `Exception` | `LogExceptionInfo?` | Present when the event carried an exception. |
| `SourceId` | `string` | Id of the originating `LogSource` (v1: the single local source). |

**Validation / rules**:
- Properties and scopes are truncated to the configured caps; truncation is silent (no throw).
- `Message` is always populated (formatter applied); `MessageTemplate` may be null.
- Sensitive-data redaction is **not** applied this slice (documented assumption).

### LogProperty
`Name: string`, `Value: string` (stringified, length-capped by `MaxPropertyValueLength`).

### LogScope
`Items: IReadOnlyList<LogProperty>` (or a rendered `string` for non-keyed scopes).

### LogExceptionInfo
`Type: string`, `Message: string`, `StackTrace: string?`, `Inner: LogExceptionInfo?` (depth-bounded).

## LogSource

A logical origin of entries. v1 = the single local host.

| Field | Type | Notes |
|---|---|---|
| `Id` | `string` | Stable id (e.g. service/instance). |
| `DisplayName` | `string` | UI label (e.g. `Elsa.Server`). |
| `ServiceName` | `string?` | Optional. |
| `MachineName` | `string?` | Optional. |
| `ProcessId` | `int?` | Optional. |

Retained shape so future remote sources fit without a contract change (FR-005).

## StructuredLogFilter

Selection criteria for both recent queries and live subscriptions.

| Field | Type | Notes |
|---|---|---|
| `MinimumLevel` | `LogLevel?` | At-or-above filter. |
| `Category` | `string?` | Category match (prefix/exact — exact this slice). |
| `SourceId` | `string?` | Restrict to a source. |
| `MaxCount` | `int?` | Recent-query cap; clamped to `MaxRecentQuerySize`. |

`Matches(entry)` is pure and branch-tested (each criterion present/absent).

## DroppedEntriesSignal

Delivered to a subscriber when backpressure forced eviction.

| Field | Type | Notes |
|---|---|---|
| `DroppedCount` | `long` | Cumulative dropped for this subscriber. |
| `Since` | `DateTimeOffset` | When dropping started/last signalled. |

## StructuredLogStreamItem

The live-feed envelope yielded by `IStructuredLogLiveFeed.Subscribe` so entries and drop signals share
one ordered stream.

| Field | Type | Notes |
|---|---|---|
| `Entry` | `StructuredLogEntry?` | Set when this item is a log entry. |
| `Dropped` | `DroppedEntriesSignal?` | Set when this item is a backpressure drop notice. |

Exactly one of `Entry`/`Dropped` is non-null. Construct via factory helpers
(`StructuredLogStreamItem.ForEntry(...)` / `.ForDropped(...)`); `public sealed`.

## StructuredLogsOptions

Bound under the feature name `DiagnosticsStructuredLogs` (§2.19).

| Option | Default | Purpose |
|---|---|---|
| `MinimumLevel` | `Information` | Minimum captured level. |
| `BufferCapacity` | `2000` | Ring-buffer max retained entries (SC-003). |
| `SubscriberQueueCapacity` | `1000` | Per-subscriber bounded channel size (FR-006). |
| `MaxRecentQuerySize` | `2000` | Upper clamp for recent queries. |
| `MaxCapturedProperties` | `50` | Property cap per entry (FR-001/FR-009). |
| `MaxCapturedScopeDepth` | `10` | Scope-chain depth cap. |
| `MaxPropertyValueLength` | `4096` | Per-value string cap. |
| `RecentPath` | `/_elsa/studio/diagnostics/structured-logs/recent` | HTTP GET path. |
| `SourcesPath` | `/_elsa/studio/diagnostics/structured-logs/sources` | HTTP GET path. |
| `StreamPath` | `/_elsa/studio/diagnostics/structured-logs/stream` | SSE (`text/event-stream`) GET path. |

## Contracts (interfaces — `.Core`)

- `IStructuredLogStore` — `void Append(StructuredLogEntry entry)`, `IReadOnlyList<StructuredLogEntry> GetRecent(StructuredLogFilter filter)`.
- `IStructuredLogLiveFeed` — `IAsyncEnumerable<StructuredLogStreamItem> Subscribe(StructuredLogFilter filter, CancellationToken ct)`. The stream yields an **envelope** so drop signals travel in-band on the same sequence (resolves the "how do drops reach the consumer" seam):
  - `StructuredLogStreamItem` is a discriminated shape carrying **either** an `Entry` (`StructuredLogEntry`) **or** a `Dropped` (`DroppedEntriesSignal`); exactly one is set. Consumers switch on which is present (the SSE endpoint maps `Entry`→`event: entry`, `Dropped`→`event: dropped`).
- `IStructuredLogSink` — `void Emit(StructuredLogEntry entry)` (capture → store boundary; lets persistence interpose).
- `IStructuredLogSourceProvider` — `LogSource GetLocalSource()`, `IReadOnlyList<LogSource> GetKnownSources()`.

## Default implementations (feature package)

| Contract | Default impl | Visibility |
|---|---|---|
| `IStructuredLogStore` + `IStructuredLogLiveFeed` | `InMemoryStructuredLogStore` | `public sealed` |
| `IStructuredLogSink` | `InMemoryStructuredLogStore` (or a thin sink that forwards to it) | `public sealed` |
| `IStructuredLogSourceProvider` | `LocalStructuredLogSourceProvider` | `public sealed` |
| capture | `StructuredLogCaptureProvider : ILoggerProvider` | `public sealed` |

The feature class `StructuredLogsFeature : IShellFeature` is `public` and **not** sealed (§2.23.3).
