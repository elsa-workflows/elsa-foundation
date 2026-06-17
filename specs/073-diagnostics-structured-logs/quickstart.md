# Quickstart — Structured Logs Diagnostics (validation guide)

This is a run/validation guide proving the slice works end-to-end. Implementation detail lives in
`data-model.md`, `contracts/structured-logs.md`, and `tasks.md`.

## Prerequisites

- .NET 10 SDK (repo `net10.0`).
- Build the solution: `dotnet build` from the repo root.

## Enable the feature

1. Reference `Elsa.Diagnostics.StructuredLogs` from the host (`Elsa.Server`) and add it to the
   CShells assembly list in `src/Apps/Elsa.Server/Program.cs` (alongside the other
   `typeof(...Feature).Assembly` entries). That is the **only** host change — the `recent`, `sources`,
   and SSE `stream` endpoints are FastEndpoints auto-mapped by the existing `app.MapShells()`.
2. (Optional) Configure options under the feature name `DiagnosticsStructuredLogs` in
   `shells.json`/`appsettings.json` (e.g. `BufferCapacity`, `MinimumLevel`, paths).

## Scenario 1 — Live tail (User Story 1, P1)

1. Run the server (`dotnet run --project src/Apps/Elsa.Server`).
2. Open an SSE connection, e.g. `new EventSource('/_elsa/studio/diagnostics/structured-logs/stream')`
   (or `curl -N -H 'Accept: text/event-stream' .../stream`).
3. Trigger activity that logs (e.g. publish/execute a workflow).
4. **Expected**: an `event: entry` arrives within ~1s (SC-001) with level/timestamp/category/message
   and any properties/scopes/exception, each carrying an `id:` (sequence) line.
5. Kill and restore the connection.
6. **Expected**: `EventSource` auto-reconnects sending `Last-Event-ID`; the stream resumes after that
   sequence (or the client backfills via `recent`), server keeps running (Acceptance 1.2).

## Scenario 2 — Recent history on connect (User Story 2, P1)

1. Produce N log entries.
2. `GET /_elsa/studio/diagnostics/structured-logs/recent?take=100`.
3. **Expected**: up to 100 newest entries returned immediately (SC-002).
4. Exceed `BufferCapacity`, then re-query.
5. **Expected**: only the most recent `BufferCapacity` entries remain (oldest evicted).

## Scenario 3 — Filtering (User Story 3, P2)

1. Emit entries across multiple levels/categories.
2. `GET .../recent?minLevel=Warning&category=Elsa.Workflows.Runtime.Worker`.
3. **Expected**: only matching entries (SC-004). Same filter on the SSE stream
   (`.../stream?minLevel=Warning&category=...`) emits only matching `entry` events.
4. `GET .../sources`.
5. **Expected**: the single local source (v1).

## Scenario 4 — Backpressure / drop signalling

1. Subscribe with a deliberately slow consumer; emit a burst exceeding `SubscriberQueueCapacity`.
2. **Expected**: host memory stays bounded (SC-003), host keeps processing, and the client receives
   an `event: dropped` with a cumulative count (FR-006).

## Scenario 5 — Disabled feature (SC-005)

1. Remove the feature from the host assembly list.
2. **Expected**: no capture, the `recent`/`sources`/`stream` paths are unreachable, host logging is
   unchanged.

## Scenario 6 — Authorization (SC-006)

1. Override the `Diagnostics:StructuredLogs` policy in the host to require an authenticated user.
2. Call `recent` / open the `stream` unauthenticated.
3. **Expected**: rejected (401/403); authorized calls succeed.

## Automated test entry points

- Feature-registration test (§2.23.1): build the SP from `StructuredLogsFeature.ConfigureServices`
  and assert `IStructuredLogStore`, `IStructuredLogLiveFeed`, `IStructuredLogSink`,
  `IStructuredLogSourceProvider`, the `ILoggerProvider`, and the endpoints resolve.
- Per-impl branch tests (§2.23.2): `InMemoryStructuredLogStore` (append/evict/recent/filter/drop),
  `StructuredLogFilter.Matches`, capture mapping + caps, `LocalStructuredLogSourceProvider`.

Run: `dotnet test tests/Elsa/Diagnostics/StructuredLogs/Tests`.
