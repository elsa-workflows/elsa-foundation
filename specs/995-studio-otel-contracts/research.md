# Research: Studio OpenTelemetry Trace Inspection

All wire and host decisions needed for this plan are resolved from the approved #3131 readiness review and the existing source contracts. No open clarification remains. The readiness notes are preparation, not runtime acceptance evidence.

## Decisions

### 1. Read through the existing backend API and client

**Decision**: Use the host-supplied `moduleApi.backend.http` for the existing trace search, trace detail, storage, and resource search endpoints. Keep their route, HTTP method, DTO, and existing `diagnostics/opentelemetry:read` authorization contract. Replace the client's nonexistent `GET .../traces` and `GET .../overview` assumptions with the actual API read shapes.

**Rationale**: The current API already provides the data required for trace inspection and the stored-data overview. It requires the existing read permission. There is no overview endpoint, and no need to alter the API, SDK, proxy, host, or permission system.

**Alternatives considered**: Add an overview endpoint or adapt the backend to the current fabricated client models. Both expand backend surface for a client mapping defect and are out of scope.

**Sources**: `Essentials/src/Diagnostics/OpenTelemetry/Elsa.Diagnostics.OpenTelemetry/Endpoints/OpenTelemetryApi.cs`; `Essentials/src/Diagnostics/OpenTelemetry/Core/Models/OpenTelemetryContracts.cs`; current client `Extensions/Diagnostics.OpenTelemetry/studio/Elsa.Studio.Diagnostics.OpenTelemetry/Client/src/module.tsx`.

### 2. Project actual envelopes and nested trace detail

**Decision**: Search with `POST /diagnostics/opentelemetry/traces/search`; consume its `{ items, droppedCount }` envelope. Open a selected trace with `GET /diagnostics/opentelemetry/traces/{traceId}` and project `{ trace, spans, resources, logs }` into the supported trace, span, and resource view. Use the string trace statuses `Unset`, `Ok`, and `Error`. The trace summary's duration is a .NET `TimeSpan`; span elapsed time is derived from its start and end times. Keep the existing millisecond/second presentation behavior.

**Rationale**: The flat response model and `durationMs` assumption do not match the server's wire contract. The trace's status is not a summary error count. Existing local duration normalization already covers the supported display precision.

**Alternatives considered**: Add backend DTO fields or a new shared `TimeSpan` converter. Neither is required by the current contract or display requirement; do not broaden the change unless implementation evidence shows the existing OTel-local normalization cannot consume the server's value.

**Sources**: `Essentials/src/Diagnostics/OpenTelemetry/Core/Models/OpenTelemetryContracts.cs`; `Essentials/src/Diagnostics/OpenTelemetry/Elsa.Diagnostics.OpenTelemetry/OpenTelemetryJsonContext.cs`; `Essentials/src/Diagnostics/OpenTelemetry/Core/Models/OpenTelemetryModels.cs`; `Extensions/Diagnostics.OpenTelemetry/studio/Elsa.Studio.Diagnostics.OpenTelemetry/Client/src/module.tsx`.

### 3. Bind visible filters only to supported semantics

**Decision**: Send the trace ID filter as `traceId`, service filter as `serviceName`, status as `status`, and the bounded result limit as `take`. Rename the existing Operation control to **Trace name or ID** and bind it to `search`, which matches trace name or ID. Retain any separate exact trace-ID control without sending unsupported `operation` or `service` fields. Use `workflowInstanceId` only in the normal-host proof to find the trace from its returned execution ID.

**Rationale**: The backend search filter has no Operation field. Its `Search` value already represents the user-facing name-or-ID search, and `WorkflowInstanceId` is supported for proof correlation.

**Alternatives considered**: Keep the Operation label or pass the current query keys unchanged. This would claim a filter the host does not implement. Remove name search entirely. This would discard a supported host filter.

**Sources**: `Essentials/src/Diagnostics/OpenTelemetry/Core/Models/OpenTelemetryContracts.cs`; `Essentials/src/Diagnostics/OpenTelemetry/Elsa.Diagnostics.OpenTelemetry/Endpoints/OpenTelemetryTraceFilterBinder.cs`.

### 4. Preserve resource relationships and avoid inferred totals

**Decision**: Resolve a trace's service names from its `ResourceIds` and resource records; resolve each selected span through its own `ResourceId` against the detail's `resources`. Preserve every known service for a multi-service trace and label an unresolved identity unknown. Do not fetch detail per list row. Label storage values as stored resource/span counts. Omit an unqualified trace-total card, omit summary `errorCount`, and never convert failed reads to zero/healthy.

**Rationale**: Search summaries carry resource IDs, not an authoritative single service name. Resource search is bounded, so an incomplete map cannot be treated as complete. EF storage counts trace summaries, while the in-memory store counts raw records even though search groups repeated TraceIds; a search page's bounded result count is not a provider-neutral total. The server does not return a summary error count.

**Alternatives considered**: Choose the first service for a trace, show unknowns as a global service, or make per-row detail calls. These lose provenance, mislabel unresolved data, or add N+1 traffic. Derive error count from summary status or label a trace/search/storage count as a total. Neither value has that contract.

**Sources**: `Essentials/src/Diagnostics/OpenTelemetry/Core/Models/OpenTelemetryContracts.cs`; `Essentials/src/Diagnostics/OpenTelemetry/Persistence/EntityFrameworkCore/Stores/EfOpenTelemetryStore.cs`; `Essentials/src/Diagnostics/OpenTelemetry/Elsa.Diagnostics.OpenTelemetry/Providers/InMemory/InMemoryOpenTelemetryStore.cs`; `Essentials/src/Diagnostics/OpenTelemetry/Core/Models/OpenTelemetryModels.cs`.

### 5. Keep independent and selected-trace request states distinct

**Decision**: Model trace search, selected trace detail, storage summary, and resource list as separate request outcomes. Preserve loading, successful empty, permission denial, missing diagnostics surface, and other failure states. A list/storage/resource 404 signals an unavailable diagnostics capability; a selected-trace detail 404 identifies that trace as gone. Keep storage and resource outcomes independent so either successful portion can still render.

**Rationale**: A successful empty response is not an outage. The API's read permission protects each read; permission denial is not empty data. A selected record can disappear while the broader diagnostics API remains available. The overview currently collapses independent failures and can present an absent count as zero.

**Alternatives considered**: Reuse one shared error flag, turn all 404s into global unavailability, or convert failures to zero/empty values. These conceal which read failed and misstate the result.

**Sources**: `Extensions/Diagnostics.OpenTelemetry/studio/Elsa.Studio.Diagnostics.OpenTelemetry/Client/src/module.tsx`; `Essentials/src/Diagnostics/OpenTelemetry/Elsa.Diagnostics.OpenTelemetry/Endpoints/OpenTelemetryApi.cs`; approved #3131 readiness decision.

### 6. Keep Metrics and Logs outside this trace experience

**Decision**: Continue to identify Metrics and OpenTelemetry Logs as unsupported and retain the Diagnostics handoff. Do not render the detail envelope's nested `logs` in the page or export. Preserve the current trace/span export capability by projecting only the supported trace, spans, and resources instead of serializing the raw backend envelope. Add a detail fixture with a sentinel log to verify that it does not appear in the UI or exported content.

**Rationale**: Presence of logs in the detail DTO does not make the OpenTelemetry Logs experience supported. Exporting the raw envelope would silently cross the stated capability boundary.

**Alternatives considered**: Render/export all nested detail fields or remove existing trace/span export. The former leaks an unsupported signal; the latter removes supported behavior unnecessarily.

**Sources**: `Essentials/src/Diagnostics/OpenTelemetry/Core/Models/OpenTelemetryContracts.cs`; current OpenTelemetry client module and its export behavior; issue #3131 acceptance scope.

### 7. Prove behavior first with backend-shaped fixtures, then the normal host

**Decision**: Before production edits, add behavior-level module regressions that fail against the current fabricated contract. Cover real response envelopes and nested detail, statuses and duration units, multiple resources plus unknown mappings, search/take body semantics, independently failing storage/resource requests, denied versus unavailable, selected-trace 404 versus missing surface, and the sentinel-log export/UI boundary. Share fixtures and teardown. Keep the backend's existing TestServer authorization suites as a distinct denial proof; pair them with a rendered HTTP 403 UI regression. Then prove the happy path in the normal composed Workbench using the existing engine bridge and existing WriteLine workflow flow.

**Rationale**: Module fixtures isolate client mapping and state behavior cheaply, while the normal host demonstrates that the supplied bridge emits and serves an actual workflow trace. Existing TestServer principal tests prove the protected endpoint boundary without inventing an Identity/admin setup. An anonymous baseline composition is not denial proof.

**Alternatives considered**: Treat mocked browser data or a fixture trace as the live acceptance proof, use an anonymous composition as a denial test, add manual trace injection, or enable another provider/collector. These do not prove the normal bridge plus authenticated Studio path and would expand scope.

**Sources**: `Extensions/Diagnostics.OpenTelemetry/studio/Elsa.Studio.Diagnostics.OpenTelemetry/Client/src/__tests__/module.test.ts`; `Essentials/tests/Diagnostics/OpenTelemetry/Tests/Elsa.Diagnostics.OpenTelemetry.Tests.csproj`; `Essentials/apps/Elsa.Workbench/OpenTelemetryEngineTracingBridge.cs`; `Essentials/apps/Elsa.Workbench/shells.json`; `e2e-tests/Test-WorkflowFlow.ps1`; `e2e-tests/_ElsaCommon.ps1`; [Spec 993 composed-host validation guide](../993-studio-diagnostics-index/quickstart.md#composed-workbench-comparison).

## Remaining uncertainty

No product-contract decision remains open. The exact temporary identity's sign-in value and disposable test data are supplied by the private Development host configuration; they are not recorded in artifacts or validation receipts. Later root-owned QA must choose an available private port pair, verify the owned host is ready, and clean up the exact run root. That is execution setup, not a feature clarification.
