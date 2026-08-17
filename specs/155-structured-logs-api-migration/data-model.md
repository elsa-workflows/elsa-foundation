# Data Model: Structured Logs API Minimal API Migration

This migration does not redesign Structured Logs domain data. It preserves the existing Core models and introduces only transport/evidence concepts needed to state lifecycle contracts.

## StructuredLogEntry

Existing committed diagnostic record returned by recent history and entry SSE frames.

| Field group | Contract |
|---|---|
| Identity/order | Display sequence plus an optional opaque replay cursor; streamed entries require a valid committed cursor and use it as SSE `id`. |
| Time/severity | Timestamp and `LogLevel`; JSON retains the current property names and PascalCase enum value. |
| Message/source | Message, category and source identifier retain current null/empty representation. |
| Structured context | Properties and scopes retain ordering, truncation and serialized value behavior established by the capture pipeline. |
| Failure | Optional exception type, message, stack and inner representation remains unchanged. |

## LogSource

Existing safe discovery record returned by the sources route. Its identifier, display name and ordering remain the provider's current contract.

## StructuredLogFilter

Existing query object shared by recent history, replay and durable tailing.

| Input | Domain value | Validation |
|---|---|---|
| `minLevel` | Optional `LogLevel` excluding `None` | Case-insensitive name; invalid values fail with the current 400 text. |
| `category` | Optional string | Blank becomes absent; otherwise retained. |
| `source` | Optional string | Blank becomes absent; otherwise retained. |
| `take` | Optional non-negative integer for recent only | Blank becomes absent; invalid/negative values fail; store clamps to configured maximum. |

## StructuredLogReplayCursor

Existing opaque, provider-qualified committed position. It is never parsed by the endpoint beyond the Core cursor codec, round-trips through SSE `id`/`Last-Event-ID`, and collapses malformed, stale and wrong-binding states into one non-disclosing unavailable result.

## StructuredLogReadPage

Existing bounded result from `ReadAfterAsync`.

Invariants:

- Entries are in committed cursor order.
- Every emitted entry has a valid cursor.
- `NextCursor` is valid when entries exist or `HasMore` is true.
- `HasMore` causes an immediate next page read; otherwise the tail waits for a wake or poll delay.

## StructuredLogStreamItem

Existing in-process live-feed item carrying either an entry or a dropped-entry signal. In the public durable-tail endpoint these items are wake hints only and are not serialized directly.

## SSE frame

Bounded wire record produced by the stream:

- Entry: `id`, `event: entry`, one JSON `data` line, blank-line terminator.
- Heartbeat: `: keep-alive` comment plus blank-line terminator.
- Dropped-entry formatting remains available internally but is not newly emitted by the durable-tail endpoint.

## Permission ownership record

One active catalog definition:

| Name | Owner | Implications |
|---|---|---|
| `Diagnostics:StructuredLogs` | `Elsa.Diagnostics.StructuredLogs` | None |

Wildcard `*` remains an evaluator grant and is not a catalog entry.

## Compatibility observation

A deterministic record containing endpoint identity, case, binding, JSON, status, media type, relevant headers, body or bounded stream bytes, paging/filtering facts, and terminal state. Volatile timing/cursor values may be normalized only through a reviewed rule that separately validates their presence and format.

## Collectibility observation

String/boolean/count diagnostics plus weak references for the load context, assembly and representative module type. It records stage, OpenAPI cache entry count, any module-owned metadata kinds found, release action, and bounded collection result without retaining a collectible object.
