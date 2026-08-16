# Quickstart: Structured Logs API Minimal API Migration

## Verify the focused migration

```bash
dotnet test tests/Elsa/Diagnostics/StructuredLogs/Tests/Elsa.Diagnostics.StructuredLogs.Tests.csproj --no-restore
dotnet test tests/Elsa/Diagnostics/StructuredLogs/Persistence/Tests/Elsa.Diagnostics.StructuredLogs.Persistence.Tests.csproj --no-restore
dotnet test tests/Elsa/Diagnostics/StructuredLogs/Persistence/Groundwork/Tests/Elsa.Diagnostics.StructuredLogs.Persistence.Groundwork.Tests.csproj --no-restore
dotnet test tests/Elsa/Api/Compatibility/Testing/Tests/Elsa.Api.Compatibility.Testing.Tests.csproj --no-restore
dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj --no-restore
dotnet run --project tools/maps/Elsa.Maps.Generator -- check
```

Before merge, run the complete affected solution build and every repository gate required by the feature-delivery loop.

## Inspect the streaming migration

1. Confirm the module exposes exactly the three reviewed configured GET routes.
2. Confirm every endpoint carries module ownership, Minimal API authoring metadata, and the Foundation wildcard-or-`Diagnostics:StructuredLogs` permission disposition.
3. Confirm the permission has one catalog owner and no implication.
4. Compare recent/sources HTTP, bounded SSE, and actual OpenAPI evidence with immutable FastEndpoints-before baselines.
5. Verify the initial durable boundary, valid resume, generic invalid/unavailable cursor failure, remote-only commit, filtering, polling fallback, exact frame bytes, and 15-second heartbeat.
6. Verify cancellation, client disconnect, pending-reader timeout, feed failure, slow consumer, and repeated connect/disconnect cleanup.
7. Confirm the production endpoint does not start emitting formatter-supported dropped events.
8. Confirm one unrelated FastEndpoints route still works and reaches the same instrumented Foundation evaluator.
9. Inspect actual OpenAPI operation-context cache evidence for module-owned metadata, then confirm materialized route, live stream, services, serializer, and document owners release.
10. Confirm no production FastEndpoints dependency or Structured Logs transition-exception entry remains.

## Diagnose failures

- **HTTP/query delta**: inspect raw missing/blank/repeated/culture-sensitive values and preserve binder behavior; never broaden an invalid query.
- **JSON delta**: keep the dedicated serializer's names, nulls, and PascalCase `LogLevel` values.
- **SSE delta**: compare headers, response-start order, frame bytes, flushes, heartbeat interval, cursor identity, ordering, and bounded terminal state.
- **Replay gap/duplicate**: ensure only durable pages produce payloads and the initial tail is captured before wake hints are relied upon.
- **Cancellation leak**: identify the response writer, durable iterator, wake enumerator, timer, linked token source, or pending `MoveNextAsync` owner.
- **Authorization delta**: inspect the canonical `Any(*, Diagnostics:StructuredLogs)` policy and evaluator instance; do not add handler claim checks.
- **Duplicate route**: remove the legacy registration rather than relying on endpoint selection order.
- **OpenAPI delta**: compare the actual legacy operation before adding explicit metadata; keep metadata types host/framework/Core-owned where possible.
- **Collectibility delta**: inspect the OpenAPI operation-context cache and capture GC-root evidence before attributing the owner; do not infer unloadability from process memory.
