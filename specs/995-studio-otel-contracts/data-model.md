# Data Model: Studio OpenTelemetry Trace Inspection

This feature adds no backend or persisted entity. The page projects the existing read DTOs into separate trace-list, trace-detail, storage, resource, and request-state views.

## Host-provided data

| Concept | Relevant fields and relationship | Display rule |
|---|---|---|
| Trace search result | Trace ID, optional name, start/end times, `TimeSpan` duration, span count, `Unset`/`Ok`/`Error` status, and resource IDs | Show the ID even without a name. Format the host duration using the existing millisecond/second display. A row count is the bounded search result count, never a total of traces produced. |
| Trace detail | Nested `trace`, `spans`, `resources`, and `logs` | Project supported trace/span/resource fields. Keep nested logs out of both UI and export. |
| Span | Span ID, optional parent ID, start/end times, status, attributes, events, and resource ID | Preserve parent-child relationships and available metadata. Calculate elapsed time from the span's own timestamps and format consistently with the trace. |
| Resource | Resource ID, service name, optional service-instance ID, SDK language, attributes, last-seen time, and `Unknown`/`Active`/`Stale` resource status | Show only supplied optional metadata. Resource status is not system health. An unresolved resource uses an explicit unknown fallback. |
| Storage summary | Host-reported storage counters and dropped data | Label visible resource/span values as stored data. Do not show an unqualified trace-total card, infer a trace total from a bounded result, or present these values as health. Do not show Metrics/Logs as supported experiences. |
| Search envelope | `items` and `droppedCount` | Empty `items` after success is successful-empty. If dropped data is shown, identify it as dropped data, not a trace total. |

## Client view projections

- **Trace row service identity** is resolved from summary resource IDs and the resource search result. Keep every known identity and mark unresolved relationships unknown; if no service identity can be resolved, show a resource count plus an explicit unknown indicator. Because resource search is bounded, do not treat a partial map as complete or invent a name.
- **Selected trace service identity** is resolved by joining each span's `resourceId` to the selected detail's `resources`. A trace may therefore display multiple services while each span remains associated with its own resource.
- **Trace and span status** are host values. A trace with `Error` is not a one-error count; the view contains no summary `errorCount` field.
- **Trace duration** comes from the host's `TimeSpan`. **Span duration** comes from `endTime - startTime`. Reuse the existing OTel-local normalization and millisecond/second formatter; do not introduce a shared converter for this correction.
- **Export** continues to include supported trace/span data and does not serialize the raw nested detail envelope. This keeps logs out of exported content.

## Request state

Model each read independently so one result does not overwrite another:

| Read | Loading | Success | Permission | Unavailable / missing | Failure |
|---|---|---|---|---|---|
| Trace search | Loading results | Rows or successful-empty explanation | Explicit denied state for 401/403 | A 404 on the search surface means diagnostics unavailable | Separate request-failure state |
| Selected trace detail | Loading detail | Trace with nested supported span/resource data | Explicit denied state for 401/403 | A 404 means this selected trace is missing; it does not mark the whole API unavailable | Separate request-failure state |
| Storage summary | Loading stored counts | Host-reported counts, including a truthful zero | Explicit denied state for 401/403 | Surface absence remains distinct from zero | Failure does not become zero/healthy |
| Resource search | Loading resource details | Supplied resource metadata or successful-empty | Explicit denied state for 401/403 | Surface absence remains distinct from an unresolved mapping | Failure does not erase independent storage data |

## User filter projection

The page's filter state maps only to supported search fields: exact trace ID to `traceId`, service identity to `serviceName`, status to `status`, result bound to `take`, and the **Trace name or ID** text input to `search`. The backend's `search` semantic matches trace name or ID; the client does not invent an operation filter. The normal-host proof may use `workflowInstanceId` to correlate the disposable execution with a trace; this is not a new page control.
