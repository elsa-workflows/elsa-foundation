# Contracts — Structured Logs Diagnostics

The external surfaces this feature exposes. Paths are defaults (configurable via
`StructuredLogsOptions`); they mirror the existing console-stream shape so the studio host config and
bottom-panel client stay uniform.

## HTTP — Recent history

`GET {RecentPath}` (default `/_elsa/studio/diagnostics/structured-logs/recent`)

Query parameters (all optional):

| Param | Type | Maps to |
|---|---|---|
| `minLevel` | string (`Trace`..`Critical`) | `StructuredLogFilter.MinimumLevel` |
| `category` | string | `StructuredLogFilter.Category` |
| `source` | string | `StructuredLogFilter.SourceId` |
| `take` | int | `StructuredLogFilter.MaxCount` (clamped to `MaxRecentQuerySize`) |

**200 Response** — newest-aligned array of entries:

```json
[
  {
    "sequence": 1042,
    "timestamp": "2026-06-18T10:31:22.114+00:00",
    "level": "Warning",
    "category": "Elsa.Workflows.Runtime.Worker",
    "eventId": 17,
    "message": "Activity 'HttpRequest' retried (attempt 2)",
    "messageTemplate": "Activity '{ActivityType}' retried (attempt {Attempt})",
    "properties": [
      { "name": "ActivityType", "value": "HttpRequest" },
      { "name": "Attempt", "value": "2" }
    ],
    "scopes": [ { "items": [ { "name": "WorkflowInstanceId", "value": "wf-123" } ] } ],
    "exception": null,
    "sourceId": "elsa-server"
  }
]
```

**Authorization**: requires the `Diagnostics:StructuredLogs` policy (default-permissive; host-overridable).

**Errors**: invalid `minLevel`/`take` → `400` with a domain-scoped problem detail (no raw infra
exception leaks, §2.23.5).

## HTTP — Known sources

`GET {SourcesPath}` (default `/_elsa/studio/diagnostics/structured-logs/sources`)

**200 Response**:

```json
[
  { "id": "elsa-server", "displayName": "Elsa.Server", "serviceName": "elsa-server", "machineName": "host-01", "processId": 4123 }
]
```

v1 returns exactly one (the local source). Same authorization as above.

## SSE — Live feed

`GET {StreamPath}` (default `/_elsa/studio/diagnostics/structured-logs/stream`),
`Accept: text/event-stream`. A FastEndpoint that writes the live feed as Server-Sent Events.

Query parameters (optional, same shape as `recent` minus `take`): `minLevel`, `category`, `source`.

**Response**: `Content-Type: text/event-stream`, chunked/streamed. Each captured entry is written as:

```text
id: <versioned opaque committed cursor>
event: entry
data: {"sequence":1042,"timestamp":"2026-06-18T10:31:22.114+00:00","level":"Warning", ... }

```

When the subscriber falls behind and entries are evicted (FR-006), a drop signal is written:

```text
event: dropped
data: {"droppedCount":128,"since":"2026-06-18T10:32:00Z"}

```

Periodic comment heartbeats (`: keep-alive\n\n`) keep intermediaries from closing idle connections.

**Reconnection / resume**: the browser's native `EventSource` auto-reconnects and sends the last
seen opaque `id` in the `Last-Event-ID` request header. The endpoint subscribes before beginning a
validated bounded durable read-after pages. The in-process feed only wakes the durable tail early; it never
supplies authoritative SSE payloads, and periodic polling discovers commits from other processes.
The cursor value is provider-neutral at the endpoint seam; its layout and provider-specific position are
interpreted only by the store adapter that issued it. Invalid, expired/trimmed, wrong-scope, wrong-stream, and wrong-source values all return the same
non-disclosing `409 Conflict`; clients then refresh from `recent`.

**Authorization**: requires the `Diagnostics:StructuredLogs` policy (default-permissive;
host-overridable). Note: native `EventSource` cannot send an `Authorization` header — hosts that
tighten the policy use cookie auth or a token query-string parameter (see research R2/R5).

## DI / host wiring contracts

- Feature registration (CShells): `StructuredLogsFeature : IShellFeature`, `name =
  "DiagnosticsStructuredLogs"`. `ConfigureServices` registers options, the capture `ILoggerProvider`,
  `InMemoryStructuredLogStore` (as `IStructuredLogStore` + `IStructuredLogLiveFeed` + `IStructuredLogSink`),
  `LocalStructuredLogSourceProvider`, the three FastEndpoints (`recent`, `sources`, `stream`), and the
  named authz policy (default-permissive).
- Live-feed seam: `IStructuredLogLiveFeed.Subscribe` yields `StructuredLogStreamItem` envelopes
  (`Entry` or `Dropped`); the `stream` endpoint maps `Entry`→`event: entry` and `Dropped`→`event:
  dropped`. This is how drop signals reach the consumer in-band (see data-model.md).
- **No host hub wiring.** All three endpoints are FastEndpoints, auto-mapped by the existing
  `app.MapShells()`; the host only adds the feature assembly to the CShells assembly list. There is
  no `MapStructuredLogStreaming()` extension (that was only needed under the rejected SignalR option).

## Capability detection (FR-012)

Presence of the feature (enumerable via the modularity/feature registry by its stable `name`
`DiagnosticsStructuredLogs`) is how a remote diagnostics UI detects availability on a host. No
separate capability endpoint is introduced this slice.

## Contract test obligations

Per `quickstart.md`, each surface has at least one test:
- `recent` returns ≤ cap, newest-aligned, filter-honouring.
- `sources` returns the local source.
- `stream` (SSE) delivers entries to a subscriber and writes a `dropped` event under forced backpressure.
- live-feed envelope (`StructuredLogStreamItem`) carries exactly one of `Entry`/`Dropped`, and the endpoint maps each to `event: entry` / `event: dropped`.
- unauthorized request is rejected when the host tightens the policy (SC-006).
