# Existing Backend Read Contract

This document records the backend contract the Studio page consumes. It proposes no endpoint, permission, SDK member, or backend change. The Studio module uses the host-provided `moduleApi.backend.http` and the existing `diagnostics/opentelemetry:read` permission.

## Reads

| Purpose | Method and path | Request | Response shape used by the page |
|---|---|---|---|
| Search traces | `POST /diagnostics/opentelemetry/traces/search` | JSON filter fields include `traceId`, `serviceName`, `status`, `take`, and `search`; the backend also supports `workflowInstanceId` for the disposable workflow proof. `search` matches trace name or ID. | `{ items, droppedCount }`, where each item is a trace summary with resource IDs and host status/timing data. |
| Read selected trace | `GET /diagnostics/opentelemetry/traces/{traceId}` | URL-encode the selected trace ID as a path segment. | `{ trace, spans, resources, logs }`. Project trace/span/resource fields; do not render or export `logs`. |
| Read stored counts | `GET /diagnostics/opentelemetry/storage` | None | Host storage summary. Show only supported stored-data counts; there is no universal logical trace total or health field. |
| Search resource metadata | `POST /diagnostics/opentelemetry/resources/search` | Use the existing resource-filter body with a bounded `take`; retain the backend's field names and bound semantics. | `{ items, droppedCount }` with supplied resource identifiers and optional service metadata. Treat an incomplete resource result as a partial map. |

The route paths above are backend route paths. The later browser proof records the actual host request path through Studio's existing client, including the host's current prefix, without changing route composition.

## Mapping constraints

- Trace filter fields are `traceId`, `serviceName`, `status`, `take`, `search`, and other server-supported fields. Do not send unsupported `operation` or `service` properties.
- The trace status values are the JSON strings `Unset`, `Ok`, and `Error`. Resource status values, when shown, are `Unknown`, `Active`, and `Stale`; they are not system-health values.
- Trace summary duration is serialized from .NET `TimeSpan`; spans carry start/end time rather than a span `durationMs`. Normalize with the existing OTel client behavior.
- Trace summaries identify associated resources by ID. Trace detail resources are used to join each span's `resourceId`. Missing join targets remain unknown. Do not fetch detail once per list row.
- Storage `traceCount` does not have provider-neutral logical-trace meaning: EF counts summaries, while the in-memory store counts raw records and search groups repeated TraceIds. Omit a general trace-total card and never substitute a bounded result count.
- The server returns no trace-summary error count. Render status without deriving `errorCount` or adding per-row detail reads.
- The detail envelope carries `logs`, but Metrics and OpenTelemetry Logs are unsupported. Preserve trace/span export while projecting only supported trace/span/resource data, so the nested logs are absent from display and export.
- Each read remains independently observable. A 401/403 is permission denial. A search/storage/resource 404 is an unavailable surface. A 404 for a selected trace detail means that trace is missing. Other HTTP failures remain request failures; none becomes a successful empty or zero count.

## Source authority

- `Essentials/src/Diagnostics/OpenTelemetry/Elsa.Diagnostics.OpenTelemetry/Endpoints/OpenTelemetryApi.cs`
- `Essentials/src/Diagnostics/OpenTelemetry/Core/Models/OpenTelemetryContracts.cs`
- `Essentials/src/Diagnostics/OpenTelemetry/Elsa.Diagnostics.OpenTelemetry/OpenTelemetryJsonContext.cs`
- `Essentials/src/Diagnostics/OpenTelemetry/Core/Models/OpenTelemetryModels.cs`
- `Essentials/src/Diagnostics/OpenTelemetry/Persistence/EntityFrameworkCore/Stores/EfOpenTelemetryStore.cs`
- `Extensions/Diagnostics.OpenTelemetry/studio/Elsa.Studio.Diagnostics.OpenTelemetry/Client/src/module.tsx`
