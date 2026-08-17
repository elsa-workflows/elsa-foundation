# Structured Logs HTTP and SSE Contract

## Configured routes

Defaults:

| Method | Path | Capability |
|---|---|---|
| GET | `/_elsa/studio/diagnostics/structured-logs/recent` | Recent committed entries |
| GET | `/_elsa/studio/diagnostics/structured-logs/sources` | Known log sources |
| GET | `/_elsa/studio/diagnostics/structured-logs/stream` | Resumable live tail |

Each path is replaced by its corresponding `StructuredLogsOptions` value when configured. The module registers exactly one endpoint per capability.

## Recent

Query parameters: optional `minLevel`, `category`, `source`, and `take`.

- Valid request: `200`, `application/json`, serialized entry array in current store order.
- Invalid `minLevel` or `take`: `400`, current plain-text message.
- `take = 0` is valid; omitted/blank means no requested override; the store retains its configured maximum clamp.

## Sources

No request body or query parameters. Success is `200` JSON containing the provider's current `LogSource` collection.

## Stream preconditions

Query parameters: optional `minLevel`, `category`, and `source`. Header: optional `Last-Event-ID`.

Before the response starts, the endpoint:

1. validates the filter;
2. parses or captures the durable cursor;
3. subscribes to the local wake feed;
4. performs and validates the first bounded durable read.

Invalid query is the same `400` contract as recent. Malformed or unavailable cursor is `409` with `The structured log replay cursor is unavailable.`. No SSE headers/body are committed on these failures.

## Stream response

Successful stream:

```text
Status: 200
Content-Type: text/event-stream
Cache-Control: no-cache
Connection: keep-alive
X-Accel-Buffering: no
```

Entry frame:

```text
id: <opaque committed replay cursor>
event: entry
data: <StructuredLogEntry JSON>

```

Heartbeat frame after the legacy 15-second idle interval:

```text
: keep-alive

```

Every frame is flushed. The current endpoint does not emit process-local dropped-entry frames.

## Ordering and resume

- New connection: capture the durable tail before relying on the local wake feed; emit only later committed pages.
- Resume: begin strictly after a valid `Last-Event-ID`.
- Payloads always come from `IStructuredLogStore.ReadAfterAsync`; local feed items only wake the reader.
- `HasMore` pages are drained immediately; otherwise polling occurs no later than the configured interval, with a 10ms effective lower bound for non-positive values.

## Termination and cleanup

- Request cancellation/client disconnect cancels the linked stream.
- Pending writer enumeration cleanup remains bounded to five seconds.
- Local wake-enumerator cleanup retains its bounded behavior and never blocks durable polling indefinitely.
- Feed failure or completion degrades to polling.
- Operation cancellation caused by disconnect is not translated into a second HTTP response.
